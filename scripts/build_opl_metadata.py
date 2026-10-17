#!/usr/bin/env python3
import argparse
import json
import re
from pathlib import Path


def normalize_serial(value: str) -> str:
    return re.sub(r"[^A-Z0-9]", "", value.upper())


def parse_cfg(path: Path) -> dict[str, str]:
    result: dict[str, str] = {}
    try:
        text = path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return result
    for raw in text.splitlines():
        if "=" not in raw:
            continue
        key, value = raw.split("=", 1)
        key = key.strip()
        value = value.strip()
        if key and value:
            result[key] = value
    return result


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--cfg-dir", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    cfg_dir = Path(args.cfg_dir)
    if not cfg_dir.is_dir():
        raise SystemExit(f"CFG directory not found: {cfg_dir}")

    output: dict[str, dict[str, str]] = {}
    with_description = 0
    with_genre = 0

    for cfg in sorted(cfg_dir.glob("*.cfg")):
        serial = normalize_serial(cfg.stem)
        if not serial:
            continue
        data = parse_cfg(cfg)
        item = {
            "title": data.get("Title", ""),
            "description": data.get("Description", ""),
            "genre": data.get("Genre", ""),
            "developer": data.get("Developer", ""),
            "publisher": data.get("Publisher", ""),
            "releaseDate": data.get("Release", ""),
        }
        if item["description"]:
            with_description += 1
        if item["genre"]:
            with_genre += 1
        if any(item.values()):
            output[serial] = item

    out = Path(args.output)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(output, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"OPL metadata: {len(output):,} serials; {with_description:,} descriptions; {with_genre:,} genres -> {out}")


if __name__ == "__main__":
    main()
