using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PlayStation2GameLibrary;

internal static class MetadataEnrichmentService
{
    private const string StateKey = "opl-cfg-enrichment-sha256";
    private static readonly object CacheLock = new();
    private static string? _cachedPath;
    private static Dictionary<string, OplMetadata>? _cachedMetadata;

    public static async Task EnsureSchemaAsync(SqliteConnection db)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(Game)";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
        }

        if (!columns.Contains("Genres"))
        {
            await using var alter = db.CreateCommand();
            alter.CommandText = "ALTER TABLE Game ADD COLUMN Genres TEXT NULL";
            await alter.ExecuteNonQueryAsync();
        }

        await using var state = db.CreateCommand();
        state.CommandText = @"CREATE TABLE IF NOT EXISTS MetadataImportState(
StateKey TEXT PRIMARY KEY,
StateValue TEXT NOT NULL,
UpdatedUtc TEXT NOT NULL
)";
        await state.ExecuteNonQueryAsync();
    }

    public static async Task<int> ApplyIfAvailableAsync(SqliteConnection db, string metadataPath)
    {
        await EnsureSchemaAsync(db);
        if (!File.Exists(metadataPath)) return 0;

        var bytes = await File.ReadAllBytesAsync(metadataPath);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        await using (var check = db.CreateCommand())
        {
            check.CommandText = "SELECT StateValue FROM MetadataImportState WHERE StateKey=$key";
            check.Parameters.AddWithValue("$key", StateKey);
            var existing = Convert.ToString(await check.ExecuteScalarAsync());
            if (string.Equals(existing, sha, StringComparison.OrdinalIgnoreCase)) return 0;
        }

        var metadata = ParseMetadata(bytes);
        var chosen = new Dictionary<string, OplMetadata>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT r.GameId,COALESCE(d.SerialNormalized,''),r.IsPrimary,r.RegionCode
FROM Release r JOIN Disc d ON d.ReleaseId=r.ReleaseId
ORDER BY r.GameId,
r.IsPrimary DESC,
CASE WHEN r.RegionCode IN ('NorthAmerica','USA','NTSC-U','NTSC-U/C') THEN 0
     WHEN r.RegionCode IN ('Europe','PAL','Europe/PAL','Australia') THEN 1
     WHEN r.RegionCode IN ('Japan','NTSC-J') THEN 2 ELSE 3 END,
d.DiscNumber";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var gameId = reader.GetString(0);
                var serial = reader.GetString(1);
                if (serial.Length == 0 || !metadata.TryGetValue(serial, out var candidate)) continue;
                if (!chosen.TryGetValue(gameId, out var current))
                {
                    chosen[gameId] = candidate;
                    continue;
                }
                chosen[gameId] = current.MergeMissing(candidate);
            }
        }

        await using var tx = await db.BeginTransactionAsync();
        var changed = 0;
        foreach (var pair in chosen)
        {
            await using var cmd = db.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = @"UPDATE Game SET
Description=CASE WHEN COALESCE(TRIM(Description),'')='' AND $description<>'' THEN $description ELSE Description END,
Genres=CASE WHEN COALESCE(TRIM(Genres),'')='' AND $genres<>'' THEN $genres ELSE Genres END,
Developer=CASE WHEN COALESCE(TRIM(Developer),'')='' AND $developer<>'' THEN $developer ELSE Developer END,
UpdatedUtc=$now
WHERE GameId=$id";
            cmd.Parameters.AddWithValue("$description", pair.Value.Description);
            cmd.Parameters.AddWithValue("$genres", pair.Value.Genre);
            cmd.Parameters.AddWithValue("$developer", pair.Value.Developer);
            cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$id", pair.Key);
            changed += await cmd.ExecuteNonQueryAsync();
        }

        await using (var state = db.CreateCommand())
        {
            state.Transaction = (SqliteTransaction)tx;
            state.CommandText = @"INSERT INTO MetadataImportState(StateKey,StateValue,UpdatedUtc)
VALUES($key,$value,$now)
ON CONFLICT(StateKey) DO UPDATE SET StateValue=excluded.StateValue,UpdatedUtc=excluded.UpdatedUtc";
            state.Parameters.AddWithValue("$key", StateKey);
            state.Parameters.AddWithValue("$value", sha);
            state.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            await state.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return changed;
    }

    public static MetadataLookup FindBestForSerials(string metadataPath, IEnumerable<string> serials)
    {
        var metadata = GetCachedMetadata(metadataPath);
        var best = new OplMetadata(string.Empty, string.Empty, string.Empty);
        foreach (var rawSerial in serials)
        {
            var serial = NormalizeSerial(rawSerial);
            if (serial.Length == 0 || !metadata.TryGetValue(serial, out var candidate)) continue;
            best = best.MergeMissing(candidate);
            if (!string.IsNullOrWhiteSpace(best.Description) && !string.IsNullOrWhiteSpace(best.Genre) && !string.IsNullOrWhiteSpace(best.Developer)) break;
        }
        return new MetadataLookup(best.Description, best.Genre, best.Developer);
    }

    private static Dictionary<string, OplMetadata> GetCachedMetadata(string metadataPath)
    {
        if (!File.Exists(metadataPath)) return new Dictionary<string, OplMetadata>(StringComparer.OrdinalIgnoreCase);
        var fullPath = Path.GetFullPath(metadataPath);
        lock (CacheLock)
        {
            if (_cachedMetadata is not null && string.Equals(_cachedPath, fullPath, StringComparison.OrdinalIgnoreCase)) return _cachedMetadata;
            _cachedMetadata = ParseMetadata(File.ReadAllBytes(fullPath));
            _cachedPath = fullPath;
            return _cachedMetadata;
        }
    }

    private static Dictionary<string, OplMetadata> ParseMetadata(byte[] bytes)
    {
        var metadata = new Dictionary<string, OplMetadata>(StringComparer.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return metadata;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object) continue;
            var item = property.Value;
            metadata[property.Name] = new OplMetadata(
                Read(item, "description"),
                Read(item, "genre"),
                Read(item, "developer"));
        }
        return metadata;
    }

    private static string NormalizeSerial(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private static string Read(JsonElement item, string name)
        => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    internal sealed record MetadataLookup(string Description, string Genre, string Developer);

    private sealed record OplMetadata(string Description, string Genre, string Developer)
    {
        public OplMetadata MergeMissing(OplMetadata other) => new(
            string.IsNullOrWhiteSpace(Description) ? other.Description : Description,
            string.IsNullOrWhiteSpace(Genre) ? other.Genre : Genre,
            string.IsNullOrWhiteSpace(Developer) ? other.Developer : Developer);
    }
}
