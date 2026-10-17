#!/usr/bin/env python3
from __future__ import annotations

import argparse
import collections
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_SOURCE = ROOT / "data" / "GameDB-PS2.seed.json"

US_REGIONS = {"u", "us", "usa", "northamerica", "ntsc-u", "ntsc-u/c"}
PAL_REGIONS = {"e", "eu", "europe", "pal", "europe/pal", "australia", "au"}
JP_REGIONS = {"j", "jp", "japan", "ntsc-j"}
KO_REGIONS = {"k", "kr", "kor", "korea", "south korea"}
ENGLISH = {"en", "eng", "english"}

# Conservative diagnostics only. These are NOT silently removed by this script.
NON_GAME_TITLE_PATTERNS = [
    r"\bdvd\s*player\b",
    r"\bnetwork\s+access\s+disc\b",
    r"\butility\s+disc\b",
    r"\bps2\s+linux\b",
    r"\bbrowser\s+2\.0\b",
]
NON_GAME_GENRES = {"system", "utility"}


def normalize_serial(value: str) -> str:
    return re.sub(r"[^A-Z0-9]", "", value.upper())


def normalize_title(value: str) -> str:
    value = value.casefold().strip()
    value = re.sub(r"\s+", " ", value)
    return value


def language_values(record: dict) -> list[str]:
    raw = record.get("languages", record.get("language", []))
    if raw is None:
        return []
    if isinstance(raw, list):
        values = raw
    elif isinstance(raw, str):
        values = re.split(r"[,;/|]", raw)
    else:
        values = [str(raw)]
    return [str(v).strip() for v in values if str(v).strip()]


def has_english(record: dict) -> bool:
    return any(v.casefold() in ENGLISH for v in language_values(record))


def region_bucket(record: dict) -> str:
    value = str(record.get("region", "")).strip().casefold()
    if value in US_REGIONS:
        return "NTSC-U/C"
    if value in PAL_REGIONS:
        return "PAL"
    if value in JP_REGIONS:
        return "NTSC-J"
    if value in KO_REGIONS:
        return "Korea"
    return value or "Unknown"


def suspected_non_game(record: dict) -> bool:
    title = str(record.get("title", ""))
    genre = str(record.get("genre", "")).strip().casefold()
    if genre in NON_GAME_GENRES:
        return True
    return any(re.search(pattern, title, re.I) for pattern in NON_GAME_TITLE_PATTERNS)


def primary_rank(record: dict) -> tuple[int, int, str]:
    bucket = region_bucket(record)
    # Agreed catalogue preference: NTSC-U/C first, PAL second, JP/KR only when useful.
    region_rank = {"NTSC-U/C": 0, "PAL": 1, "NTSC-J": 2, "Korea": 3}.get(bucket, 4)
    date = str(record.get("releaseDate", ""))
    year = int(date[:4]) if len(date) >= 4 and date[:4].isdigit() else 9999
    return region_rank, year, normalize_serial(str(record.get("serial", "")))


def build_report(source: Path) -> str:
    data = json.loads(source.read_text(encoding="utf-8"))
    if not isinstance(data, dict):
        raise SystemExit("GameDB seed must be a top-level JSON object")

    records: list[dict] = []
    for key, value in data.items():
        if not isinstance(value, dict):
            continue
        record = dict(value)
        record["_sourceKey"] = key
        records.append(record)

    region_counts = collections.Counter(region_bucket(r) for r in records)
    genre_counts = collections.Counter(str(r.get("genre", "") or "Unknown").strip() or "Unknown" for r in records)
    language_counts = collections.Counter(lang for r in records for lang in language_values(r))
    array_languages = sum(isinstance(r.get("languages"), list) for r in records)
    missing_languages = sum(not language_values(r) for r in records)
    missing_serial = sum(not str(r.get("serial", "")).strip() for r in records)
    missing_title = sum(not str(r.get("title", "")).strip() for r in records)

    serial_region = collections.Counter(
        (normalize_serial(str(r.get("serial", ""))), region_bucket(r))
        for r in records if str(r.get("serial", "")).strip()
    )
    duplicate_serial_region = [k for k, count in serial_region.items() if count > 1]

    by_title: dict[str, list[dict]] = collections.defaultdict(list)
    for r in records:
        title = str(r.get("title", "")).strip()
        if title:
            by_title[normalize_title(title)].append(r)

    jp_ko = [r for r in records if region_bucket(r) in {"NTSC-J", "Korea"}]
    jp_ko_no_english = [r for r in jp_ko if not has_english(r)]
    suspected = [r for r in records if suspected_non_game(r)]

    # Policy preview: this is diagnostic, not a destructive import.
    policy_records = [
        r for r in records
        if not suspected_non_game(r)
        and not (region_bucket(r) in {"NTSC-J", "Korea"} and not has_english(r))
        and str(r.get("title", "")).strip()
        and str(r.get("serial", "")).strip()
    ]
    policy_titles: dict[str, list[dict]] = collections.defaultdict(list)
    for r in policy_records:
        policy_titles[normalize_title(str(r.get("title", "")))].append(r)

    primary_preview = [min(group, key=primary_rank) for group in policy_titles.values()]
    primary_regions = collections.Counter(region_bucket(r) for r in primary_preview)

    title_multi_release = sum(len(group) > 1 for group in by_title.values())
    policy_multi_release = sum(len(group) > 1 for group in policy_titles.values())

    lines = [
        "PLAYSTATION 2 GAME LIBRARY — FULL CATALOG VALIDATION",
        "=====================================================",
        f"Source: {source.name}",
        "",
        "RAW SOURCE",
        f"Records: {len(records):,}",
        f"Distinct normalized titles: {len(by_title):,}",
        f"Titles with multiple source releases: {title_multi_release:,}",
        f"Missing title: {missing_title:,}",
        f"Missing serial: {missing_serial:,}",
        f"Records whose languages field is a JSON array: {array_languages:,}",
        f"Records without language evidence: {missing_languages:,}",
        f"Duplicate normalized serial+region keys: {len(duplicate_serial_region):,}",
        "",
        "REGIONS",
    ]
    lines += [f"{name}: {count:,}" for name, count in region_counts.most_common()]
    lines += ["", "LANGUAGES (top 20)"]
    lines += [f"{name}: {count:,}" for name, count in language_counts.most_common(20)]
    lines += ["", "GENRES (top 20)"]
    lines += [f"{name}: {count:,}" for name, count in genre_counts.most_common(20)]
    lines += [
        "",
        "POLICY DIAGNOSTICS",
        f"Suspected system/utility/non-game records: {len(suspected):,}",
        f"JP/KR records: {len(jp_ko):,}",
        f"JP/KR records without English: {len(jp_ko_no_english):,}",
        f"Candidate release records after conservative policy preview: {len(policy_records):,}",
        f"Candidate conceptual titles after exact-title grouping: {len(policy_titles):,}",
        f"Candidate titles with multiple releases: {policy_multi_release:,}",
        "Primary-region preview (NTSC-U/C > PAL > JP/KR):",
    ]
    lines += [f"  {name}: {count:,}" for name, count in primary_regions.most_common()]

    if suspected:
        lines += ["", "SAMPLE SUSPECTED NON-GAME RECORDS"]
        for r in suspected[:25]:
            lines.append(f"- {r.get('serial','?')} | {r.get('title','?')} | {r.get('genre','?')} | {region_bucket(r)}")

    if jp_ko_no_english:
        lines += ["", "SAMPLE JP/KR WITHOUT ENGLISH"]
        for r in jp_ko_no_english[:20]:
            lines.append(f"- {r.get('serial','?')} | {r.get('title','?')} | {', '.join(language_values(r)) or 'no language'}")

    lines += [
        "",
        "IMPORTANT",
        "This report is a validation preview. It does not overwrite catalogue.db.",
        "The full importer must parse every language array item separately and must not silently treat the JSON array as one language string.",
        "Exact-title grouping is only a first pass; ADMIN merge/split remains necessary for regional/localized-title edge cases.",
    ]
    return "\n".join(lines) + "\n"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()

    report = build_report(args.source)
    print(report, end="")
    if args.report:
        args.report.write_text(report, encoding="utf-8")


if __name__ == "__main__":
    main()
