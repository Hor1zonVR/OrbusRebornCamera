import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createRoom, makeServer } from '../server.mjs';

test('private room relay exchanges only consenting party clips and save requests', async () => {
  const root = await mkdtemp(join(tmpdir(), 'rc-relay-'));
  const server = makeServer({ root });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  try {
    const origin = 'http://127.0.0.1:' + server.address().port;
    const room = await createRoom(root);
    const other = await createRoom(root);
    const base = origin + '/v1/rooms/' + room.id;
    const secret = { authorization: 'Bearer ' + room.secret };
    const alice = 'a'.repeat(32);
    const jordan = 'b'.repeat(32);
    const clip = 'c'.repeat(32);
    const otherClip = 'd'.repeat(32);
    const url = base + '/clips/' + alice + '/' + clip;

    let r = await fetch(base + '/clips');
    assert.equal(r.status, 401);
    r = await fetch(origin + '/v1/rooms/' + other.id + '/clips',
      { headers: secret });
    assert.equal(r.status, 401, 'room isolation');
    r = await fetch(base + '/clips', { headers: secret });
    assert.equal(r.status, 200);
    assert.deepEqual((await r.json()).clips, []);

    const bytes = Buffer.from('Synthetic replay ZIP payload ONLY');
    r = await fetch(url, { method: 'PUT',
      headers: { ...secret, 'content-length': String(bytes.length) },
      body: bytes });
    assert.equal(r.status, 201);
    r = await fetch(url, { method: 'PUT',
      headers: { ...secret, 'content-length': String(bytes.length) },
      body: bytes });
    assert.equal(r.status, 200, 'idempotent clip upload');
    r = await fetch(base + '/clips', { headers: secret });
    const list = (await r.json()).clips;
    assert.equal(list.length, 1);
    assert.equal(list[0].participant, alice);
    assert.equal(list[0].clip_id, clip);

    r = await fetch(url, { headers: secret });
    assert.equal(r.status, 200);
    assert.deepEqual(Buffer.from(await r.arrayBuffer()), bytes);
    r = await fetch(base + '/clips/' + jordan + '/' + otherClip, { headers: secret });
    assert.equal(r.status, 404);

    r = await fetch(base + '/events', {
      method: 'POST',
      headers: { ...secret, 'content-type': 'application/json' },
      body: JSON.stringify({ from: alice, request_id: clip, action: 'save-last' })
    });
    assert.equal(r.status, 201);
    const event = await r.json();
    r = await fetch(base + '/events?after=0', { headers: secret });
    const events = (await r.json()).events;
    assert.equal(events.length, 1);
    assert.equal(events[0].seq, event.seq);
    r = await fetch(base + '/events?after=' + event.seq, { headers: secret });
    assert.deepEqual((await r.json()).events, [], 'incremental polling');
    r = await fetch(base + '/events', {
      method: 'POST',
      headers: { ...secret, 'content-type': 'application/json' },
      body: JSON.stringify({ from: alice, request_id: clip, action: 'save-last' })
    });
    assert.equal(r.status, 200, 'idempotent save command');

    // Both participants saving in the same instant must not erase either
    // other's command through a racing JSON read/modify/write.
    const send = (from, requestId) => fetch(base + '/events', {
      method: 'POST',
      headers: { ...secret, 'content-type': 'application/json' },
      body: JSON.stringify({
        from, request_id: requestId, action: 'save-last'
      })
    });
    const [first, second] = await Promise.all([
      send(alice, otherClip), send(jordan, otherClip)
    ]);
    assert.equal(first.status, 201);
    assert.equal(second.status, 201);
    r = await fetch(base + '/events?after=' + event.seq, { headers: secret });
    const simultaneous = (await r.json()).events;
    assert.equal(simultaneous.length, 2, 'concurrent save requests survive');
    assert.equal(new Set(simultaneous.map(x => x.seq)).size, 2,
      'monotonic event sequence');

    // Invalid routes are rejected before filesystem access.
    r = await fetch(base + '/clips/./' + clip, { method: 'PUT',
      headers: { ...secret, 'content-length': String(bytes.length) }, body: bytes });
    assert.equal(r.status, 404);
    // Missing length is rejected without allocating the body in memory.
    const unknownLength = new ReadableStream({
      start(controller) { controller.enqueue(bytes); controller.close(); }
    });
    r = await fetch(base + '/clips/' + jordan + '/' + otherClip,
      { method: 'PUT', headers: secret, body: unknownLength, duplex: 'half' });
    assert.equal(r.status, 413);
  } finally {
    await new Promise(resolve => server.close(resolve));
    await rm(root, { recursive: true, force: true });
  }
});
