// Optional, independent transport for consent-based OrbusRebornCamera party clips.
// No GameMaster admin API, gameplay packets or Orbus account credentials involved.
// Keep behind HTTPS reverse proxy/tunnel for remote participants.
import http from 'node:http';
import { randomBytes, createHash, timingSafeEqual } from 'node:crypto';
import { createReadStream, createWriteStream } from 'node:fs';
import { mkdir, readFile, writeFile, rename, readdir, stat, unlink } from 'node:fs/promises';
import { Transform } from 'node:stream';
import { pipeline } from 'node:stream/promises';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

export const MAX_CLIP_BYTES = 100 * 1024 * 1024;
const MAX_CLIPS_PER_ROOM = 40;
const MAX_EVENTS = 128;
const MAX_EVENT_BYTES = 2048;
const safeId = value => typeof value === 'string' && /^[A-Za-z0-9_-]{8,64}$/.test(value);
const hash = value => createHash('sha256').update(value, 'utf8').digest('hex');
const response = (res, status, obj) => {
  res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' });
  res.end(JSON.stringify(obj));
};
const rootDefault = join(dirname(fileURLToPath(import.meta.url)), 'data');
const roomDir = (root, id) => join(root, 'rooms', id);
const metadataPath = (root, id) => join(roomDir(root, id), 'room.json');
const eventsPath = (root, id) => join(roomDir(root, id), 'events.json');
const clipDir = (root, id, participant) => join(roomDir(root, id), 'clips', participant);
const validClip = value => safeId(value);

export async function createRoom(root = rootDefault) {
  const id = randomBytes(16).toString('hex');
  const secret = randomBytes(32).toString('hex');
  const folder = roomDir(root, id);
  await mkdir(folder, { recursive: true, mode: 0o700 });
  await writeFile(metadataPath(root, id), JSON.stringify({
    sha256: hash(secret), created_utc: new Date().toISOString()
  }), { flag: 'wx', mode: 0o600 });
  return { id, secret };
}

async function authorized(req, root, room) {
  const header = req.headers.authorization;
  if (typeof header !== 'string' || !header.startsWith('Bearer ')) return false;
  const key = header.slice(7);
  if (key.length !== 64 || !/^[0-9a-f]{64}$/.test(key)) return false;
  try {
    const meta = JSON.parse(await readFile(metadataPath(root, room), 'utf8'));
    const actual = Buffer.from(hash(key), 'hex');
    const expected = Buffer.from(meta.sha256, 'hex');
    return actual.length === expected.length && timingSafeEqual(actual, expected);
  } catch { return false; }
}

async function readSmallJson(req) {
  let size = 0; const chunks = [];
  for await (const part of req) {
    size += part.length;
    if (size > MAX_EVENT_BYTES) throw Error('Request too large');
    chunks.push(part);
  }
  return JSON.parse(Buffer.concat(chunks).toString('utf8'));
}

async function readEvents(root, room) {
  try {
    const events = JSON.parse(await readFile(eventsPath(root, room), 'utf8'));
    return Array.isArray(events) ? events.slice(-MAX_EVENTS) : [];
  } catch (err) {
    if (err.code === 'ENOENT') return [];
    throw err;
  }
}

async function listClips(root, room) {
  const clips = [];
  const base = join(roomDir(root, room), 'clips');
  let senders;
  try { senders = await readdir(base, { withFileTypes: true }); }
  catch (err) { if (err.code === 'ENOENT') return clips; throw err; }
  for (const folder of senders) {
    if (!folder.isDirectory() || !safeId(folder.name)) continue;
    const entries = await readdir(join(base, folder.name), { withFileTypes: true });
    for (const entry of entries) {
      if (!entry.isFile() || !entry.name.endsWith('.party.zip')) continue;
      const clipId = entry.name.slice(0, -'.party.zip'.length);
      if (!validClip(clipId)) continue;
      const info = await stat(join(base, folder.name, entry.name));
      clips.push({ participant: folder.name, clip_id: clipId,
        bytes: info.size, uploaded_ms: Math.trunc(info.mtimeMs) });
    }
  }
  return clips.sort((a, b) => a.uploaded_ms - b.uploaded_ms).slice(-MAX_CLIPS_PER_ROOM);
}

export function makeServer({ root = rootDefault } = {}) {
  const storage = resolve(root);
  // Two recorders can save simultaneously. Serialize per-room event updates
  // so concurrent read-modify-write operations never lose a save request.
  const eventLocks = new Map();
  async function withEventLock(room, callback) {
    const previous = eventLocks.get(room) || Promise.resolve();
    let release;
    const mine = new Promise(resolve => { release = resolve; });
    const queued = previous.catch(() => {}).then(() => mine);
    eventLocks.set(room, queued);
    await previous.catch(() => {});
    try { return await callback(); }
    finally {
      release();
      if (eventLocks.get(room) === queued) eventLocks.delete(room);
    }
  }
  return http.createServer(async (req, res) => {
    const route = new URL(req.url || '/', 'http://127.0.0.1').pathname.split('/').filter(Boolean);
    if (req.method === 'GET' && route.join('/') === 'health') {
      return response(res, 200, { ok: true, service: 'RebornCamera Party Relay', version: 1 });
    }
    if (route[0] !== 'v1' || route[1] !== 'rooms' || !safeId(route[2]))
      return response(res, 404, { error: 'Not found' });

    const room = route[2];
    if (!await authorized(req, storage, room))
      return response(res, 401, { error: 'Unauthorized' });

    try {
      if (route.length === 4 && route[3] === 'clips' && req.method === 'GET') {
        return response(res, 200, { clips: await listClips(storage, room) });
      }
      if (route.length === 6 && route[3] === 'clips' &&
          safeId(route[4]) && validClip(route[5])) {
        const participant = route[4], clipId = route[5];
        const destination = join(clipDir(storage, room, participant), clipId + '.party.zip');
        if (req.method === 'GET') {
          let info;
          try { info = await stat(destination); }
          catch { return response(res, 404, { error: 'Clip not found' }); }
          if (!info.isFile()) return response(res, 404, { error: 'Clip not found' });
          res.writeHead(200, { 'Content-Type': 'application/zip',
            'Content-Length': info.size, 'Cache-Control': 'private, no-store' });
          return createReadStream(destination).pipe(res);
        }
        if (req.method === 'PUT') {
          const length = Number(req.headers['content-length']);
          if (!Number.isFinite(length) || length <= 0 || length > MAX_CLIP_BYTES)
            return response(res, 413, { error: 'Invalid/oversize upload' });
          const existing = await listClips(storage, room);
          if (existing.some(x => x.participant === participant && x.clip_id === clipId))
            return response(res, 200, { ok: true, existed: true });
          if (existing.length >= MAX_CLIPS_PER_ROOM)
            return response(res, 507, { error: 'Room retention cap reached' });
          await mkdir(dirname(destination), { recursive: true, mode: 0o700 });
          const partial = destination + '.' + randomBytes(8).toString('hex') + '.partial';
          let size = 0;
          try {
            const count = new Transform({
              transform(chunk, enc, callback) {
                size += chunk.length;
                callback(size > MAX_CLIP_BYTES ? Error('Clip too large') : null, chunk);
              }
            });
            await pipeline(req, count, createWriteStream(partial, { flags: 'wx', mode: 0o600 }));
            if (size !== length) throw Error('Incomplete upload');
            await rename(partial, destination);
          } catch (error) {
            await unlink(partial).catch(() => {});
            throw error;
          }
          return response(res, 201, { ok: true, bytes: size });
        }
      }
      if (route.length === 4 && route[3] === 'events') {
        if (req.method === 'GET') {
          const after = Number(new URL(req.url, 'http://localhost').searchParams.get('after') || 0);
          if (!Number.isSafeInteger(after) || after < 0)
            return response(res, 400, { error: 'Invalid cursor' });
          const events = await readEvents(storage, room);
          return response(res, 200, { events: events.filter(x => x.seq > after),
            newest: events.length ? events[events.length - 1].seq : after });
        }
        if (req.method === 'POST') {
          const body = await readSmallJson(req);
          if (!safeId(body.from) || !safeId(body.request_id) ||
              body.action !== 'save-last')
            return response(res, 400, { error: 'Invalid event' });
          return withEventLock(room, async () => {
            const events = await readEvents(storage, room);
            if (events.some(x => x.from === body.from && x.request_id === body.request_id))
              return response(res, 200, { ok: true, duplicate: true });
            const seq = events.length ? events[events.length - 1].seq + 1 : 1;
            events.push({ seq, from: body.from, request_id: body.request_id,
              action: body.action, created_ms: Date.now() });
            const destination = eventsPath(storage, room);
            const partial = destination + '.' + randomBytes(8).toString('hex') + '.partial';
            try {
              await writeFile(partial, JSON.stringify(events.slice(-MAX_EVENTS)),
                { flag: 'wx', mode: 0o600 });
              await rename(partial, destination);
            } finally {
              await unlink(partial).catch(() => {});
            }
            return response(res, 201, { ok: true, seq });
          });
        }
      }
      return response(res, 404, { error: 'Not found' });
    } catch (error) {
      return response(res, 500, { error: 'Relay request failed' });
    }
  });
}

const invoked = process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href;
if (invoked) {
  const root = resolve(process.env.RC_PARTY_DATA_DIR || rootDefault);
  if (process.argv[2] === 'create-room') {
    const room = await createRoom(root);
    console.log('Share these settings with the other consenting recorder ONCE:');
    console.log('Room ID: ' + room.id);
    console.log('Room secret: ' + room.secret);
    console.log('Do not share the room secret publicly or with GameMaster.');
  } else {
    const port = Number(process.env.RC_PARTY_PORT || 4788);
    const host = process.env.RC_PARTY_BIND || '127.0.0.1';
    if (!Number.isSafeInteger(port) || port < 1 || port > 65535) throw Error('Invalid port');
    makeServer({ root }).listen(port, host, () => {
      console.log('Party relay listening on ' + host + ':' + port);
      console.log('For internet use, place behind HTTPS reverse proxy/tunnel. Never expose GameMaster.');
    });
  }
}
