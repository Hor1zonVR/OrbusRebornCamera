#!/usr/bin/env python3
"""Read-only offline scanner for Trickster Goblin / TreasureGoblin Unity assets.

Scans only the selected OrbusVR client directory and writes a small report next
to this script. Does not modify game files, reveal world coordinates, connect
to game servers, or spawn anything.

Optional: pip install UnityPy
Then run with --deep to inspect Unity object and bundle names as well.
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import os
from pathlib import Path
import sys

EXACT_TERMS = (
    "treasuregoblin",
    "treasure goblin",
    "trickstergoblin",
    "trickster goblin",
    "goblinstore",
)
BROAD_TERMS = ("trickster", "goblin")
TERMS = EXACT_TERMS + BROAD_TERMS
SEARCH_BYTES = tuple(
    (word, encoding, word.encode(encoding).lower())
    for word in TERMS
    for encoding in ("utf-8", "utf-16-le")
)
SKIP_DIRS = {"bepinex", "logs", "saves", "screenshots", "mods", "plugins"}
SKIP_SUFFIXES = {
    ".png", ".jpg", ".jpeg", ".webp", ".ogg", ".wav", ".mp3", ".mp4",
    ".avi", ".dds", ".pdb", ".log", ".cache", ".tmp",
}
DEEP_TYPES = {
    "GameObject", "Mesh", "Material", "Texture2D", "Sprite",
    "AnimatorController", "AnimationClip", "TextAsset", "MonoScript",
    "MonoBehaviour", "AssetBundle",
}
DEEP_SUFFIXES = {".assets", ".bundle", ".unity3d", ".assetbundle", ".ab"}
CHUNK_SIZE = 4 * 1024 * 1024
OVERLAP = 80
MAX_FILE_HITS = 25
MAX_DEEP_HITS = 250


def choose_folder() -> Path:
    try:
        import tkinter as tk
        from tkinter import filedialog
        root = tk.Tk()
        root.withdraw()
        root.attributes("-topmost", True)
        selected = filedialog.askdirectory(
            parent=root,
            title="Choose extracted Orbus Reborn / Renewed game folder",
        )
        root.destroy()
        if selected:
            return Path(selected)
    except Exception as exc:
        print("Folder picker unavailable:", exc)
    return Path(input("Enter extracted Orbus game folder path: ").strip('" '))


def resolve_client(folder: Path) -> tuple[Path, Path]:
    folder = folder.expanduser().resolve()
    if folder.is_file():
        folder = folder.parent
    if folder.name.lower().endswith("_data"):
        folder = folder.parent
    if (folder / "New-Client").is_dir():
        folder = folder / "New-Client"
    data_dirs = [
        folder / "vrclient_Data",
        folder / "OrbusVR_Data",
        folder / "OrbusVRRenewed_Data",
        folder / "OrbusRenewed_Data",
        folder / "OrbusVR Renewed_Data",
    ]
    data = next((p for p in data_dirs if p.is_dir()), None)
    if not data or not (folder / "GameAssembly.dll").is_file():
        raise ValueError(
            "Select the extracted client folder containing GameAssembly.dll "
            "and vrclient_Data or OrbusVR_Data (not the ZIP or server files)."
        )
    return folder, data


def scan_files(client: Path, data: Path) -> list[Path]:
    files = []
    for current, dirs, names in os.walk(data):
        dirs[:] = sorted(x for x in dirs if x.lower() not in SKIP_DIRS)
        for name in sorted(names):
            path = Path(current) / name
            if path.suffix.lower() in SKIP_SUFFIXES:
                continue
            if path.is_file():
                files.append(path)
    for extra in (client / "GameAssembly.dll",):
        if extra.is_file():
            files.append(extra)
    return files


def raw_scan(path: Path, client: Path) -> list[dict]:
    hits = []
    tail = b""
    position = 0
    with path.open("rb") as f:
        while True:
            chunk = f.read(CHUNK_SIZE)
            if not chunk:
                break
            content = tail + chunk
            lower = content.lower()
            initial = position - len(tail)
            for term, encoding, needle in SEARCH_BYTES:
                start = 0
                while True:
                    offset = lower.find(needle, start)
                    if offset < 0:
                        break
                    # Only count a match once when it straddles a chunk.
                    absolute = initial + offset
                    if absolute >= position or (
                        offset + len(needle) > len(tail)
                    ):
                        if len(hits) < MAX_FILE_HITS:
                            hits.append({
                                "term": term,
                                "encoding": encoding,
                                "file": str(path.relative_to(client)),
                                "file_offset": absolute,
                                "confidence": "strong"
                                if term in EXACT_TERMS else "candidate",
                            })
                    start = offset + 1
            tail = content[-OVERLAP:]
            position += len(chunk)
    return hits


def looks_like_unity_file(path: Path) -> bool:
    name = path.name.lower()
    return (
        path.suffix.lower() in DEEP_SUFFIXES
        or name in {"globalgamemanagers", "globalgamemanagers.assets"}
        or name.startswith("level") and name[5:].isdigit()
        or name == "resources.assets"
    )


def deep_scan(paths: list[Path], client: Path) -> tuple[list[dict], list[str]]:
    try:
        import UnityPy
    except ImportError:
        return [], [
            "UnityPy is not installed. Run: py -3 -m pip install UnityPy "
            "and repeat with --deep to inspect compressed Unity assets."
        ]

    results = []
    warnings = []
    candidates = [p for p in paths if looks_like_unity_file(p)]
    for index, path in enumerate(candidates, 1):
        relative = str(path.relative_to(client))
        try:
            size = path.stat().st_size
            if size > 900 * 1024 * 1024:
                warnings.append(
                    f"Skipped large Unity file ({size} bytes): {relative}"
                )
                continue
            print(f"    Unity assets {index}/{len(candidates)}: {relative}")
            env = UnityPy.load(str(path))
            for obj in env.objects:
                if len(results) >= MAX_DEEP_HITS:
                    warnings.append(
                        "Unity-name report limit reached; stopped deep parsing."
                    )
                    return results, warnings
                kind = obj.type.name
                if kind not in DEEP_TYPES:
                    continue
                try:
                    # peek_name reads only the serialized name, not the full
                    # texture/mesh payload, keeping the scan relatively light.
                    if hasattr(obj, "peek_name"):
                        name = obj.peek_name()
                    else:
                        value = obj.read()
                        name = getattr(value, "m_Name",
                                       getattr(value, "name", None))
                    if not name or not isinstance(name, str):
                        continue
                    matches = [
                        term for term in TERMS if term in name.lower()
                    ]
                    if matches:
                        results.append({
                            "file": relative,
                            "object_type": kind,
                            "asset_name": name.replace("\r", " ").replace("\n", " ")[:200],
                            "terms": matches,
                            "confidence": "strong" if any(
                                m in EXACT_TERMS for m in matches
                            ) else "candidate",
                        })
                except Exception:
                    # Some IL2CPP MonoBehaviours have no serialized typetree.
                    continue
        except Exception as exc:
            warnings.append(
                f"Could not parse {relative}: {type(exc).__name__}: {exc}"
            )
    return results, warnings


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "game_folder", nargs="?", help="Extracted client folder. "
        "Omit to choose it using a Windows folder picker."
    )
    parser.add_argument(
        "--deep", action="store_true",
        help="Also parse Unity asset bundles and serialized object names; "
        "requires UnityPy (pip install UnityPy).",
    )
    parser.add_argument(
        "--output", type=Path, default=None,
        help="Output report JSON path (default: next to this script).",
    )
    args = parser.parse_args()
    try:
        client, data = resolve_client(
            Path(args.game_folder) if args.game_folder else choose_folder()
        )
    except (ValueError, OSError) as exc:
        print("ERROR:", exc, file=sys.stderr)
        return 2

    print("Scanning client:", client)
    print("Unity asset data:", data)
    print("Read-only scan; game files will not be changed.")
    files = scan_files(client, data)
    raw_hits = []
    warnings = []
    print("Stage 1: searching readable asset strings in", len(files), "files...")
    for index, path in enumerate(files, 1):
        if index == 1 or index % 20 == 0 or index == len(files):
            print(f"    {index}/{len(files)}: {path.name}")
        try:
            raw_hits.extend(raw_scan(path, client))
        except Exception as exc:
            warnings.append(
                f"Unable to read {path.name}: {type(exc).__name__}: {exc}"
            )

    deep_hits = []
    if args.deep:
        print("Stage 2: parsing Unity asset object names...")
        deep_hits, deep_warnings = deep_scan(files, client)
        warnings.extend(deep_warnings)
    else:
        print("Stage 2 skipped. Add --deep for compressed Unity asset inspection.")

    report = {
        "tool": "OrbusRebornCamera offline Trickster asset inspector v1",
        "created_at_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "client_kind": "Renewed" if (client / "OrbusVR.exe").exists()
                       else "Reborn",
        "files_scanned": len(files),
        "raw_hits": raw_hits,
        "unity_named_objects": deep_hits,
        "warnings": warnings[:120],
        "notes": [
            "treasuregoblin is the server's rare enemy identifier.",
            "goblinstore is the separate vendor and must not be filtered.",
            "Raw string hits can include unrelated text or stale identifiers.",
            "No results do not prove the prefab is absent; asset bundles may "
            "be compressed or not included in the scanned files.",
            "This report does not establish that a freecam filter is safe.",
        ],
    }
    destination = (
        args.output.expanduser().resolve()
        if args.output
        else Path(__file__).resolve().with_name("trickster_asset_report.json")
    )
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print()
    print("Report:", destination)
    print(f"Raw hits: {len(raw_hits)} | Unity-named objects: {len(deep_hits)}")
    for hit in deep_hits[:15]:
        print("  Unity:", hit["object_type"], hit["asset_name"],
              "in", hit["file"])
    for hit in raw_hits[:15]:
        print("  Raw:", hit["term"], "in", hit["file"])
    if warnings:
        print(f"Warnings: {len(warnings)} (see report).")
    print("Send me the small report JSON; do not upload the full game.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
