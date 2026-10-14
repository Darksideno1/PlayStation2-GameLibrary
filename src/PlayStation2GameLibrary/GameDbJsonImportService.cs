using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PlayStation2GameLibrary;

internal sealed record ImportPreviewRow(string ExternalId, string Title, string Region, string Serial, string Action, string Reason, string Warnings, string PayloadJson, string Hash, string? Language, string? Publisher, int? Year);
internal sealed record ImportPreviewResult(string FilePath, string Sha256, IReadOnlyList<ImportPreviewRow> Rows)
{
    public int AddCount => Rows.Count(x => x.Action == "Add");
    public int UpdateCount => Rows.Count(x => x.Action == "Update");
    public int SkipCount => Rows.Count(x => x.Action == "Skip");
    public int RejectCount => Rows.Count(x => x.Action == "Reject");
    public int WarningCount => Rows.Count(x => !string.IsNullOrWhiteSpace(x.Warnings));
}

internal sealed class GameDbJsonImportService
{
    private const string SourceId = "gamedb-ps2";
    private const string SourceName = "GameDB-PS2";
    private const string SourceUrl = "https://github.com/niemasd/GameDB-PS2";

    public async Task<ImportPreviewResult> PreviewAsync(string filePath, string databasePath, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var fileHash = Convert.ToHexString(SHA256.HashData(bytes));
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("GameDB-PS2 JSON must have a top-level object keyed by source serial/identifier.");

        var rows = new List<ImportPreviewRow>();
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadWriteCreate");
        await connection.OpenAsync(cancellationToken);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                rows.Add(new(property.Name, "", "Unknown", property.Name, "Reject", "Record value is not a JSON object", "", property.Value.GetRawText(), Hash(property.Value.GetRawText()), null, null, null));
                continue;
            }
            var record = property.Value;
            string? Get(string name) => record.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString().Trim() : null;
            var title = Get("title") ?? "";
            var serial = Get("serial") ?? property.Name;
            var region = Get("region") ?? "Unknown";
            var language = Get("language");
            var publisher = Get("publisher");
            int? year = int.TryParse((Get("release_date") ?? "").Split('-')[0], out var y) && y is >= 1970 and <= 2100 ? y : null;
            var raw = record.GetRawText();
            var hash = Hash(raw);
            var warnings = new List<string>();
            if (language is null) warnings.Add("Language missing in source");
            if (Get("release_date") is null) warnings.Add("Release date missing in source");
            if (publisher is null) warnings.Add("Publisher missing in source");
            if (Get("developer") is null) warnings.Add("Developer missing in source");
            var action = "Add";
            var reason = "New source identifier";
            if (string.IsNullOrWhiteSpace(title)) { action = "Reject"; reason = "Required title is missing"; }
            else
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT PayloadSha256 FROM SourceRecord WHERE SourceId=$source AND ExternalId=$external AND EntityType='Release'";
                cmd.Parameters.AddWithValue("$source", SourceId);
                cmd.Parameters.AddWithValue("$external", property.Name);
                var existing = await cmd.ExecuteScalarAsync(cancellationToken) as string;
                if (existing is not null && existing == hash) { action = "Skip"; reason = "Identical source payload already imported"; }
                else if (existing is not null) { action = "Update"; reason = "Source payload changed; update existing linked release"; }
            }
            rows.Add(new(property.Name, title, region, serial, action, reason, string.Join("; ", warnings), raw, hash, language, publisher, year));
        }
        return new(filePath, fileHash, rows);
    }

    public async Task ApplyAsync(ImportPreviewResult preview, string databasePath, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadWriteCreate");
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        try
        {
            await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO Source(SourceId,SourceName,SourceUrl,TermsStatus,Notes) VALUES($id,$name,$url,'ReviewRequired','Local import; redistribution rights must be reviewed.')",
                cancellationToken, ("$id", SourceId), ("$name", SourceName), ("$url", SourceUrl));
            var runId = Guid.NewGuid().ToString("D");
            var startedUtc = DateTimeOffset.UtcNow.ToString("O");
            await ExecuteAsync(connection, transaction, "INSERT INTO ImportRun(ImportRunId,SourceId,StartedUtc,InputSha256,InputName,Status) VALUES($id,$source,$started,$hash,$name,'Running')", cancellationToken,
                ("$id", runId), ("$source", SourceId), ("$started", startedUtc), ("$hash", preview.Sha256), ("$name", Path.GetFileName(preview.FilePath)));
            var gameCache = new Dictionary<string, string>(StringComparer.Ordinal);
            await using (var gameCmd = connection.CreateCommand())
            {
                gameCmd.Transaction = transaction;
                gameCmd.CommandText = "SELECT GameId,CanonicalTitle FROM Game";
                await using var gameReader = await gameCmd.ExecuteReaderAsync(cancellationToken);
                while (await gameReader.ReadAsync(cancellationToken)) gameCache[NormalizeTitle(gameReader.GetString(1))] = gameReader.GetString(0);
            }
            foreach (var row in preview.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (row.Action is "Reject" or "Skip") continue;
                var old = await FindSourceRecordAsync(connection, transaction, row.ExternalId, cancellationToken);
                string gameId;
                var normalizedTitle = NormalizeTitle(row.Title);
                if (!gameCache.TryGetValue(normalizedTitle, out gameId))
                {
                    gameId = Guid.NewGuid().ToString("D");
                    await ExecuteAsync(connection, transaction, "INSERT INTO Game(GameId,CanonicalTitle,SortTitle,FirstReleaseYear,CreatedUtc,UpdatedUtc) VALUES($id,$title,$sort,$year,$now,$now)", cancellationToken,
                        ("$id", gameId), ("$title", row.Title), ("$sort", row.Title), ("$year", row.Year), ("$now", DateTimeOffset.UtcNow.ToString("O")));
                    gameCache[normalizedTitle] = gameId;
                }

                var releaseId = old?.ReleaseId;
                if (releaseId is null)
                    releaseId = await FindReleaseAsync(connection, transaction, row.Serial, row.Region, row.Title, cancellationToken);
                if (releaseId is null)
                {
                    releaseId = Guid.NewGuid().ToString("D");
                    var now = DateTimeOffset.UtcNow.ToString("O");
                    await ExecuteAsync(connection, transaction, "INSERT INTO Release(ReleaseId,GameId,ReleaseTitle,RegionCode,ReleaseType,Publisher,ReleaseYear,Notes,CreatedUtc,UpdatedUtc) VALUES($id,$game,$title,$region,'Retail',$publisher,$year,'Imported from GameDB-PS2',$now,$now)", cancellationToken,
                        ("$id", releaseId), ("$game", gameId), ("$title", row.Title), ("$region", row.Region), ("$publisher", (object?)row.Publisher ?? DBNull.Value), ("$year", (object?)row.Year ?? DBNull.Value), ("$now", now));
                }
                else
                {
                    await ExecuteAsync(connection, transaction, "UPDATE Release SET ReleaseTitle=$title,Publisher=COALESCE($publisher,Publisher),ReleaseYear=COALESCE($year,ReleaseYear),UpdatedUtc=$now WHERE ReleaseId=$id", cancellationToken,
                        ("$title", row.Title), ("$publisher", (object?)row.Publisher ?? DBNull.Value), ("$year", (object?)row.Year ?? DBNull.Value), ("$now", DateTimeOffset.UtcNow.ToString("O")), ("$id", releaseId));
                }

                var sourceRecordId = old?.SourceRecordId ?? Guid.NewGuid().ToString("D");
                var serialNormalized = NormalizeSerial(row.Serial);
                if (!await ExistsAsync(connection, transaction, "SELECT 1 FROM Disc WHERE ReleaseId=$release AND DiscNumber=1", releaseId, cancellationToken))
                    await ExecuteAsync(connection, transaction, "INSERT INTO Disc(DiscId,ReleaseId,DiscNumber,SerialRaw,SerialNormalized,DiscLabel) VALUES($id,$release,1,$raw,$normalized,NULL)", cancellationToken,
                        ("$id", Guid.NewGuid().ToString("D")), ("$release", releaseId), ("$raw", row.Serial), ("$normalized", serialNormalized));
                if (!string.IsNullOrWhiteSpace(row.Language))
                    await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO ReleaseLanguage(ReleaseId,LanguageCode,EvidenceSourceRecordId,Confidence) VALUES($release,$language,$sourceRecord,0.75)", cancellationToken,
                        ("$release", releaseId), ("$language", row.Language), ("$sourceRecord", sourceRecordId));

                if (old is null)
                    await ExecuteAsync(connection, transaction, "INSERT INTO SourceRecord(SourceRecordId,SourceId,ExternalId,SourceUrl,EntityType,RawTitle,RawSerial,RawRegion,RawPayloadJson,PayloadSha256,ImportedUtc) VALUES($id,$source,$external,$url,'Release',$title,$serial,$region,$raw,$hash,$now)", cancellationToken,
                        ("$id", sourceRecordId), ("$source", SourceId), ("$external", row.ExternalId), ("$url", SourceUrl), ("$title", row.Title), ("$serial", row.Serial), ("$region", row.Region), ("$raw", row.PayloadJson), ("$hash", row.Hash), ("$now", DateTimeOffset.UtcNow.ToString("O")));
                else
                {
                    await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO SourceRecordSnapshot(SnapshotId,SourceRecordId,PayloadSha256,RawPayloadJson,CapturedUtc) VALUES($id,$source,$hash,$raw,$now)", cancellationToken,
                        ("$id", Guid.NewGuid().ToString("D")), ("$source", sourceRecordId), ("$hash", old.Value.PayloadHash), ("$raw", old.Value.RawPayload), ("$now", DateTimeOffset.UtcNow.ToString("O")));
                    await ExecuteAsync(connection, transaction, "UPDATE SourceRecord SET RawTitle=$title,RawSerial=$serial,RawRegion=$region,RawPayloadJson=$raw,PayloadSha256=$hash,ImportedUtc=$now WHERE SourceRecordId=$id", cancellationToken,
                        ("$title", row.Title), ("$serial", row.Serial), ("$region", row.Region), ("$raw", row.PayloadJson), ("$hash", row.Hash), ("$now", DateTimeOffset.UtcNow.ToString("O")), ("$id", sourceRecordId));
                }
                await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO EntitySourceLink(SourceRecordId,EntityType,EntityId,MatchMethod,Confidence) VALUES($source,'Game',$game,'ExactNormalizedTitle',0.65)", cancellationToken, ("$source", sourceRecordId), ("$game", gameId));
                await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO EntitySourceLink(SourceRecordId,EntityType,EntityId,MatchMethod,Confidence) VALUES($source,'Release',$release,'SourceIdSerialRegionTitle',0.85)", cancellationToken, ("$source", sourceRecordId), ("$release", releaseId));
            }
            for (var i = 0; i < preview.Rows.Count; i++)
            {
                var row = preview.Rows[i];
                await ExecuteAsync(connection, transaction, "INSERT INTO ImportRow(ImportRunId,RowNumber,ExternalId,PayloadSha256,Action,Reason,WarningsJson,RawPayloadJson) VALUES($run,$number,$external,$hash,$action,$reason,$warnings,$raw)", cancellationToken,
                    ("$run", runId), ("$number", i + 1), ("$external", row.ExternalId), ("$hash", row.Hash), ("$action", row.Action), ("$reason", row.Reason), ("$warnings", JsonSerializer.Serialize(row.Warnings.Split("; ", StringSplitOptions.RemoveEmptyEntries))), ("$raw", row.PayloadJson));
            }
            var summary = JsonSerializer.Serialize(new { add = preview.AddCount, update = preview.UpdateCount, skip = preview.SkipCount, reject = preview.RejectCount, warnings = preview.WarningCount });
            await ExecuteAsync(connection, transaction, "UPDATE ImportRun SET CompletedUtc=$completed,Status='Completed',SummaryJson=$summary WHERE ImportRunId=$id", cancellationToken,
                ("$completed", DateTimeOffset.UtcNow.ToString("O")), ("$summary", summary), ("$id", runId));
            transaction.Commit();
        }
        catch { transaction.Rollback(); throw; }
    }

    private static async Task<(string SourceRecordId, string PayloadHash, string RawPayload, string? ReleaseId)?> FindSourceRecordAsync(SqliteConnection c, SqliteTransaction t, string externalId, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand(); cmd.Transaction = t;
        cmd.CommandText = "SELECT sr.SourceRecordId,sr.PayloadSha256,sr.RawPayloadJson,es.EntityId FROM SourceRecord sr LEFT JOIN EntitySourceLink es ON es.SourceRecordId=sr.SourceRecordId AND es.EntityType='Release' WHERE sr.SourceId=$source AND sr.ExternalId=$external AND sr.EntityType='Release' LIMIT 1";
        cmd.Parameters.AddWithValue("$source", SourceId); cmd.Parameters.AddWithValue("$external", externalId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? (r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3)) : null;
    }
    private static async Task<string?> FindGameAsync(SqliteConnection c, SqliteTransaction t, string normalizedTitle, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand(); cmd.Transaction = t; cmd.CommandText = "SELECT GameId,CanonicalTitle FROM Game";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) if (NormalizeTitle(r.GetString(1)) == normalizedTitle) return r.GetString(0);
        return null;
    }
    private static async Task<string?> FindReleaseAsync(SqliteConnection c, SqliteTransaction t, string serial, string region, string title, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand(); cmd.Transaction = t;
        cmd.CommandText = "SELECT r.ReleaseId,r.ReleaseTitle FROM Release r JOIN Disc d ON d.ReleaseId=r.ReleaseId WHERE d.SerialNormalized=$serial AND lower(r.RegionCode)=lower($region)";
        cmd.Parameters.AddWithValue("$serial", NormalizeSerial(serial)); cmd.Parameters.AddWithValue("$region", region);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) if (string.Equals(r.GetString(1), title, StringComparison.OrdinalIgnoreCase)) return r.GetString(0);
        return null;
    }
    private static async Task<bool> ExistsAsync(SqliteConnection c, SqliteTransaction t, string sql, string id, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand(); cmd.Transaction = t; cmd.CommandText = sql; cmd.Parameters.AddWithValue("$id", id); return await cmd.ExecuteScalarAsync(ct) is not null;
    }
    private static async Task ExecuteAsync(SqliteConnection c, SqliteTransaction t, string sql, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        await using var cmd = c.CreateCommand(); cmd.Transaction = t; cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }
    private static string NormalizeSerial(string value) => new(value.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
    private static string NormalizeTitle(string value) => string.Join(' ', new string(value.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}