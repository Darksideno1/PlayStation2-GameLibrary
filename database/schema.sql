PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS SchemaVersion (
    Version INTEGER PRIMARY KEY,
    AppliedUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Game (
    GameId TEXT PRIMARY KEY,
    CanonicalTitle TEXT NOT NULL,
    SortTitle TEXT NOT NULL,
    FirstReleaseYear INTEGER NULL CHECK (FirstReleaseYear IS NULL OR FirstReleaseYear BETWEEN 1970 AND 2100),
    Developer TEXT NULL,
    Description TEXT NULL,
    PrimaryGameImagePath TEXT NULL,
    CreatedUtc TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_Game_CanonicalTitle ON Game(CanonicalTitle COLLATE NOCASE);
CREATE INDEX IF NOT EXISTS IX_Game_SortTitle ON Game(SortTitle COLLATE NOCASE);

CREATE TABLE IF NOT EXISTS GameAlias (
    AliasId TEXT PRIMARY KEY,
    GameId TEXT NOT NULL REFERENCES Game(GameId) ON DELETE CASCADE,
    AliasTitle TEXT NOT NULL,
    LanguageCode TEXT NULL,
    AliasType TEXT NOT NULL DEFAULT 'AlternateTitle',
    SourceRecordId TEXT NULL
);
CREATE INDEX IF NOT EXISTS IX_GameAlias_AliasTitle ON GameAlias(AliasTitle COLLATE NOCASE);

CREATE TABLE IF NOT EXISTS Release (
    ReleaseId TEXT PRIMARY KEY,
    GameId TEXT NOT NULL REFERENCES Game(GameId) ON DELETE RESTRICT,
    ReleaseTitle TEXT NOT NULL,
    RegionCode TEXT NOT NULL DEFAULT 'Unknown',
    Edition TEXT NULL,
    ReleaseType TEXT NOT NULL DEFAULT 'Retail',
    Publisher TEXT NULL,
    ReleaseYear INTEGER NULL CHECK (ReleaseYear IS NULL OR ReleaseYear BETWEEN 1970 AND 2100),
    Barcode TEXT NULL,
    Notes TEXT NULL,
    IsPrimary INTEGER NOT NULL DEFAULT 0 CHECK (IsPrimary IN (0,1)),
    CreatedUtc TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_Release_GameId ON Release(GameId);
CREATE INDEX IF NOT EXISTS IX_Release_RegionCode ON Release(RegionCode);
CREATE INDEX IF NOT EXISTS IX_Release_Barcode ON Release(Barcode);

CREATE TABLE IF NOT EXISTS Disc (
    DiscId TEXT PRIMARY KEY,
    ReleaseId TEXT NOT NULL REFERENCES Release(ReleaseId) ON DELETE CASCADE,
    DiscNumber INTEGER NOT NULL CHECK (DiscNumber > 0),
    SerialRaw TEXT NULL,
    SerialNormalized TEXT NULL,
    DiscVersion TEXT NULL,
    DiscLabel TEXT NULL,
    UNIQUE (ReleaseId, DiscNumber)
);
CREATE INDEX IF NOT EXISTS IX_Disc_SerialNormalized ON Disc(SerialNormalized);

CREATE TABLE IF NOT EXISTS ReleaseLanguage (
    ReleaseId TEXT NOT NULL REFERENCES Release(ReleaseId) ON DELETE CASCADE,
    LanguageCode TEXT NOT NULL,
    EvidenceSourceRecordId TEXT NULL,
    Confidence REAL NOT NULL DEFAULT 0.5 CHECK (Confidence >= 0 AND Confidence <= 1),
    PRIMARY KEY (ReleaseId, LanguageCode, EvidenceSourceRecordId)
);

CREATE TABLE IF NOT EXISTS Source (
    SourceId TEXT PRIMARY KEY,
    SourceName TEXT NOT NULL UNIQUE,
    SourceUrl TEXT NOT NULL,
    TermsStatus TEXT NOT NULL DEFAULT 'ReviewRequired',
    Notes TEXT NULL
);

CREATE TABLE IF NOT EXISTS SourceRecord (
    SourceRecordId TEXT PRIMARY KEY,
    SourceId TEXT NOT NULL REFERENCES Source(SourceId),
    ExternalId TEXT NULL,
    SourceUrl TEXT NULL,
    EntityType TEXT NOT NULL,
    RawTitle TEXT NULL,
    RawSerial TEXT NULL,
    RawRegion TEXT NULL,
    RawPayloadJson TEXT NOT NULL,
    PayloadSha256 TEXT NOT NULL,
    ImportedUtc TEXT NOT NULL,
    UNIQUE (SourceId, ExternalId, EntityType)
);
CREATE INDEX IF NOT EXISTS IX_SourceRecord_Serial ON SourceRecord(RawSerial);
CREATE INDEX IF NOT EXISTS IX_SourceRecord_Title ON SourceRecord(RawTitle COLLATE NOCASE);

CREATE TABLE IF NOT EXISTS EntitySourceLink (
    SourceRecordId TEXT NOT NULL REFERENCES SourceRecord(SourceRecordId) ON DELETE CASCADE,
    EntityType TEXT NOT NULL CHECK (EntityType IN ('Game','Release','Disc')),
    EntityId TEXT NOT NULL,
    MatchMethod TEXT NOT NULL,
    Confidence REAL NOT NULL CHECK (Confidence >= 0 AND Confidence <= 1),
    PRIMARY KEY (SourceRecordId, EntityType, EntityId)
);

CREATE TABLE IF NOT EXISTS ReviewQueue (
    ReviewId TEXT PRIMARY KEY,
    ReviewType TEXT NOT NULL,
    CandidateEntityId TEXT NULL,
    CandidateSourceRecordId TEXT NULL REFERENCES SourceRecord(SourceRecordId),
    Reason TEXT NOT NULL,
    Confidence REAL NOT NULL DEFAULT 0.0 CHECK (Confidence >= 0 AND Confidence <= 1),
    Status TEXT NOT NULL DEFAULT 'Pending',
    CreatedUtc TEXT NOT NULL,
    ResolvedUtc TEXT NULL,
    ResolutionNotes TEXT NULL
);

-- User-owned data belongs in the USER database. It is intentionally separate
-- from catalogue tables and must not be overwritten by source refreshes.
CREATE TABLE IF NOT EXISTS CollectionCopy (
    CopyId TEXT PRIMARY KEY,
    ReleaseId TEXT NULL REFERENCES Release(ReleaseId) ON DELETE SET NULL,
    GameId TEXT NULL REFERENCES Game(GameId) ON DELETE SET NULL,
    CopyType TEXT NOT NULL CHECK (CopyType IN ('Physical','Digital','OPL')),
    OwnershipStatus TEXT NOT NULL DEFAULT 'Owned',
    Condition TEXT NULL,
    Completeness TEXT NULL,
    StorageLocation TEXT NULL,
    DigitalSizeBytes INTEGER NULL CHECK (DigitalSizeBytes IS NULL OR DigitalSizeBytes >= 0),
    Notes TEXT NULL,
    CreatedUtc TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL,
    CHECK (ReleaseId IS NOT NULL OR GameId IS NOT NULL)
);

CREATE TABLE IF NOT EXISTS CopyMedia (
    CopyMediaId TEXT PRIMARY KEY,
    CopyId TEXT NOT NULL REFERENCES CollectionCopy(CopyId) ON DELETE CASCADE,
    MediaType TEXT NOT NULL CHECK (MediaType IN ('FrontCover','FullCover','DiscImage','ManualPdf','PrimaryGameImage','Other')),
    RelativePath TEXT NOT NULL,
    Sha256 TEXT NULL,
    Notes TEXT NULL
);

CREATE TABLE IF NOT EXISTS OplTestHistory (
    TestId TEXT PRIMARY KEY,
    CopyId TEXT NOT NULL REFERENCES CollectionCopy(CopyId) ON DELETE CASCADE,
    TestedUtc TEXT NOT NULL,
    OplVersion TEXT NULL,
    Result TEXT NOT NULL,
    Notes TEXT NULL
);
CREATE TABLE IF NOT EXISTS ImportRun (
    ImportRunId TEXT PRIMARY KEY,
    SourceId TEXT NOT NULL REFERENCES Source(SourceId),
    StartedUtc TEXT NOT NULL,
    CompletedUtc TEXT NULL,
    InputSha256 TEXT NOT NULL,
    InputName TEXT NOT NULL,
    Status TEXT NOT NULL,
    SummaryJson TEXT NULL
);
CREATE TABLE IF NOT EXISTS SourceRecordSnapshot (
    SnapshotId TEXT PRIMARY KEY,
    SourceRecordId TEXT NOT NULL REFERENCES SourceRecord(SourceRecordId),
    PayloadSha256 TEXT NOT NULL,
    RawPayloadJson TEXT NOT NULL,
    CapturedUtc TEXT NOT NULL,
    UNIQUE(SourceRecordId,PayloadSha256)
);
CREATE TABLE IF NOT EXISTS ImportRow (
    ImportRunId TEXT NOT NULL REFERENCES ImportRun(ImportRunId) ON DELETE CASCADE,
    RowNumber INTEGER NOT NULL,
    ExternalId TEXT,
    PayloadSha256 TEXT NOT NULL,
    Action TEXT NOT NULL,
    Reason TEXT NOT NULL,
    WarningsJson TEXT NOT NULL,
    RawPayloadJson TEXT NOT NULL,
    PRIMARY KEY(ImportRunId,RowNumber)
);
CREATE TABLE IF NOT EXISTS FieldAssertion (
    AssertionId TEXT PRIMARY KEY,
    SourceRecordId TEXT NOT NULL REFERENCES SourceRecord(SourceRecordId),
    EntityType TEXT NOT NULL,
    EntityId TEXT NOT NULL,
    FieldName TEXT NOT NULL,
    FieldValue TEXT NOT NULL,
    PayloadSha256 TEXT NOT NULL,
    ObservedUtc TEXT NOT NULL,
    UNIQUE(SourceRecordId, FieldName, PayloadSha256)
);
