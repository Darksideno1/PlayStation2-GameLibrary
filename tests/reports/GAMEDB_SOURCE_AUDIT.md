# GameDB-PS2 source audit and full test import

- Source: `PS2.data.json`
- File size: 2,656,815 bytes
- SHA-256: `471613adebaf40080ab9e96029fb10fea92bbe6a1af33724690e274e7235d9b8`
- JSON shape: top-level object keyed by source serial/identifier; each value is an object.
- Records: **13,052**

## Region coverage

| Region value in source | Records | Share |
|---|---:|---:|
| NTSC-J | 6,443 | 49.36% |
| PAL | 4,255 | 32.60% |
| NTSC-U | 2,354 | 18.04% |

## Field completeness

| Field | Present | Missing | Present % |
|---|---:|---:|---:|
| `title` | 13,052 | 0 | 100.00% |
| `serial` | 13,052 | 0 | 100.00% |
| `region` | 13,052 | 0 | 100.00% |
| `language` | 12,040 | 1,012 | 92.25% |
| `genre` | 8,810 | 4,242 | 67.50% |
| `release_date` | 8,514 | 4,538 | 65.23% |
| `publisher` | 8,384 | 4,668 | 64.24% |
| `developer` | 8,012 | 5,040 | 61.39% |

## Duplicate and collision analysis

- **Normalized title candidates:** 2,789 groups / 8,450 records. These are candidates, not confirmed duplicates; 1,456 groups span multiple regions and must not be collapsed into one release.
- **Same-region normalized title candidates:** 1,333 groups. These can represent reprints, editions, demos, discs or genuinely duplicate source rows; title equality alone is not proof.
- **Normalized serial collisions:** 1 group(s), involving 2 records. These require review and must not be merged automatically.
- **Identical payload groups after excluding `serial`:** 694. This is a heuristic candidate metric, not a confirmed duplicate count.

### Confirmed vs candidate

- Confirmed exact duplicate source IDs in the JSON object: **0 detectable**, because JSON object keys are unique after parsing; this does not prove that the source has no duplicated concepts.
- Confirmed exact duplicate serial group: **none can be safely declared from serial alone**. The detected `MH051013` collision has two different titles and is therefore a review case, not a merge.
- Title groups and serial collisions are reported as **candidates** pending cross-source evidence.

## Full test database import

- First import: 13,052 add; 0 update; 0 skip; 0 reject.
- Second import: 0 add; 0 update; 13,052 skip; 0 reject.
- Database after repeat: 7,391 conceptual game rows; 13,052 release rows; 13,052 disc rows; 21,052 release-language assertions; 13,052 source records.
- Reimport idempotence: confirmed for this same source file and importer version.
- Missing language warnings: 1,012. Missing release date: 4,538; missing publisher: 4,668; missing developer: 5,040; missing genre: 4,242.
- This is a **test database**, not yet the production/user catalogue. No personal collection records were imported.

## Important limitations

- The source does not provide a separate canonical game identifier or a fully structured edition field in this JSON shape. The current concept grouping uses normalized title and therefore needs duplicate review, especially for limited editions and similarly named releases.
- The current import preserves original source keys and raw JSON in `SourceRecord`; region and language evidence are retained on the release/disc side. Language is the source’s single `language` field, not a guaranteed exhaustive list of all supported languages.
- Rights/redistribution approval is still separate from local technical import. This test import does not establish permission to redistribute the source data or artwork.
- Windows WPF build/runtime remains unverified in this environment because the .NET SDK/MSBuild is not installed.