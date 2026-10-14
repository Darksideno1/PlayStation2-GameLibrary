# PLAYSTATION 2 GAME LIBRARY v0.1 BETA — Source research and data plan

Research snapshot: 2026-10-10. This is an engineering decision record, not a claim that the resulting catalogue is complete.

## Source assessment

### 1. GameDB-PS2 (primary candidate for initial metadata import)
- Project: https://github.com/niemasd/GameDB-PS2
- Structured files: `PS2.data.json`, `PS2.data.tsv`, `PS2.titles.json`.
- Its README says it aggregates GameFAQs, MiSTer Addons, PlayStation Datacenter, Redump and VGCollect, and explains that PS2 executable/volume identifiers often yield serials.
- Repository exposes a GPL-3.0 license. We must preserve notices and evaluate GPL obligations before redistributing any imported data or derived database. Do not assume that a public repository means unrestricted data reuse.
- Strength: ready-to-import structured seed and serial-to-title mapping.
- Limitation: its own README says some discs lack a standard serial and may require UUID/volume ID/file-list clues. It is not proof of every retail SKU, regional variant, demo, reprint or packaging edition.

### 2. Redump / redump.info (disc/release evidence)
- https://redump.org/discs/system/ps2/region/Eu/
- https://redump.info/discs?system=PS2
- Search result snapshot for redump.org's European filter reported 4,188 records. redump.info search results reported 11,832 PS2 disc records across its database at the time of research. These are database/disc records, not counts of unique commercial games; do not compare directly with game-title counts.
- Visible fields include title, region, system, version, edition, languages, serial and dump/status. redump.info additionally exposes filters such as barcode and disc serial.
- Strength: disc-level detail, region and serial evidence, editions/revisions, language and status.
- Limitation: preservation/disc database rather than a guaranteed list of every marketed product. One game can have many records; some entries lack serial/language. Counts change. Use data downloads only in ways permitted by the site; do not assume bulk reuse is licensed.

### 3. PlayStation DataCenter / PSX Data Center (regional and language enrichment)
- Site: https://psxdatacenter.com/psx2/intro2.html
- Data serialization project: https://github.com/ticky/psxdata
- The site describes serial, region, image size, languages, game features, screenshots and covers. Its format docs describe per-region JSON records with release ID(s), title, disc count, optional language list, URL and included content.
- Strength: useful regional, serial, language and packaging-content clues; direct PS2-specific context.
- Limitation: the data serialization repository explicitly says the data set has no known license and its format is in flux. Treat as research/link enrichment until permission/licensing is resolved; don't bulk redistribute its data or images by default.

### 4. IGDB API (optional enrichment, not the canonical release spine)
- Docs: https://api-docs.igdb.com/
- Documents games, alternative names, companies, covers, language support and release dates with regions including Europe, North America, Australia, Japan, Asia, Korea and worldwide.
- Strength: game-level metadata, company/genre/language support, regional date records and alternate titles.
- Limitation: requires Twitch developer account, client credentials and OAuth; free API is described for non-commercial usage under Twitch Developer Service Agreement. Region/date metadata is not a reliable substitute for disc serial and exact retail SKU. Do not hard-code credentials into the app. Verify terms for intended distribution.

### 5. MobyGames API (optional enrichment only if cost/terms justify it)
- Docs: https://www.mobygames.com/info/api/
- Official API page lists API plans, authentication, request limits, games/platforms/releases, companies, genres and cover images. Hobbyist API currently listed at USD 9.99/month; commercial access is much more expensive. The API page says credit must be given, data cannot be repackaged/resold or claimed as one's own, and it cannot compete directly with MobyGames.
- Strength: rich editorial metadata and platform-level release details/product codes on some records.
- Limitation: paid/permissioned API, terms constrain repackaging. Not suitable as a bulk embedded catalogue source without confirming rights.

## Recommended policy

1. Build the data model and importer so every field has provenance. Source records are adapters, not the database schema.
2. Use GameDB-PS2 as the first structured candidate for a locally generated baseline only after GPL/data reuse obligations are accepted. Include its license and attribution if used.
3. Use Redump as an evidence source for disc/release records and quality checks; integrate data only through a permitted download/API/use method.
4. Use PSX Data Center as a manual/link-based enrichment source until data reuse permission is clear.
5. Make IGDB/MobyGames optional connectors, disabled until credentials/terms are configured. Never require a paid key for core local catalogue browsing.
6. Covers, back covers, disc scans and manuals are copyrighted media. Store user-supplied local files freely; external media needs separate rights/provenance and must not be silently bundled.

## Identity and merge rules

- `Game` is the conceptual title; `Release` is a regional/edition/product release; `Disc` is a disc within that release.
- Never merge on title alone. Normalize title for candidate search only (Unicode normalization, whitespace/punctuation/case folding), not as proof of identity.
- Strong release match: normalized serial + platform, with region and disc index/context. Serial collisions, serial aliases, demo codes, multi-disc releases and reprints still go to rule checks.
- Medium match: source external ID mapping or matching barcode/product code plus region/edition.
- Weak match: title + region + publisher/year/language similarity; place in review queue, never auto-merge.
- Regional title/localized title remains an alias, not automatically a separate conceptual game. Different product code, region, edition, demo status or materially distinct packaging/release can represent a separate Release.
- Preserve all source IDs, original raw values, source URL, import timestamp and raw record hash. Conflicting values are retained as field-level assertions; preferred display value follows source priority and confidence, not destructive overwrite.
- Multi-disc release has one Release and multiple Disc rows when evidence supports it; don't duplicate Game for each disc.

## Completeness and duplicate audit

Report counts separately for Games, Releases, Discs and source records. For every source import, calculate:
- source row count, accepted rows, rejected rows, records without serial, records without region, records without language, and duplicate source IDs;
- coverage by region (Europe/PAL, North America, Japan, Asia, Korea, Australia, Brazil, China, worldwide/unknown) and by release class (retail, budget/reprint, demo/promo, bundle/compilation, homebrew/unlicensed, other/unknown);
- share of release rows with serial, region, title, release class, publisher, year and language evidence;
- serial collisions across distinct releases, title-normalization collisions, same serial with contradictory region/title, same source ID changing hash, and candidate duplicates awaiting review;
- coverage union/intersection between sources, rather than summing record totals.

A percentage of all PS2 releases cannot honestly be reported until we have a defined denominator. Use Redump's current disc-record universe as a *preservation-reference denominator*, and a separate curated list of known commercial retail releases as a *commercial-catalog denominator*. Label each metric precisely. A sample audit of common games plus region-specific and obscure titles is required before choosing source priorities.

## Technical decision

- Windows desktop: WPF on .NET 8 LTS (`net8.0-windows`), MVVM-friendly separation.
- Local database: SQLite using `Microsoft.Data.Sqlite`; database schema versioned with migrations.
- Data files live only under each package's `USER/Data` or `ADMIN/Data`, not a shared `publish/data` folder.
- `USER` is read-only for global catalogue data and can edit only its own collection/settings; `ADMIN` owns catalogue maintenance/import and reference data.
- Catalogue refresh is additive/reconciling and transactional; personal collection is never overwritten by a catalogue refresh.
- Keep data import logic independent of the UI. Build the importer and schema tests before large source ingestion.

## Delivery gates

Phase 0: source/licence assessment, schema, import contracts and measurable audit reports.
Phase 1: compilable WPF shell, SQLite migration, catalogue list/search/detail with seed/test fixtures.
Phase 2: source adapter(s), import preview, idempotence, duplicate/review queue, coverage dashboard.
Phase 3: personal collection model and safe backup/restore; old archive stays untouched until migration approval.
Phase 4: media, flags, filters, regional releases and statistics.
Phase 5: separate USER/ADMIN builds, runtime/functional tests, version/feature/changelog and release validation.

No release ZIP until Windows compilation, launch, database creation, search, import idempotence, backup/restore isolation, USER/ADMIN separation and clean-install/update tests pass.