using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace PlayStation2GameLibrary;

/// <summary>
/// Seeds a fresh portable database from the official GameDB-PS2 source.
/// The catalogue keeps every legitimate game/release regardless of language.
/// Obvious demos, system utilities and non-game discs remain excluded.
/// </summary>
public static class StarterCatalogue
{
    private static readonly Regex[] NonGameTitlePatterns =
    {
        new(@"\bdemo\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bsampler\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bpreview\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\btrial\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\btaikenban\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"tentou shiyuu", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"magazine", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"buyers guide", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"players guide", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"yearbook", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bdvd\s*player\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bdvd\s*test\s*disc\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\butility\s*disc\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bnetwork\s+(?:adaptor|adapter|access).*disc\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"online start-up disc", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bps2\s+linux\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"linux \(for playstation 2\)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"seizou kensa", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bkiosk\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bbonus\s+demo\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    private static readonly Regex DiscSuffixPattern = new(
        @"\s*(?:[-:–]\s*)?[\(\[]?\s*(?:disc|disk)\s*(?:no\.?\s*)?([0-9]+|[ivx]+)\s*[\)\]]?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<int> EnsureSeededAsync(string databasePath, string seedPath)
    {
        if (!File.Exists(seedPath)) throw new FileNotFoundException("Bundled starter catalogue is missing.", seedPath);
        await using var db = new SqliteConnection($"Data Source={databasePath}");
        await db.OpenAsync();
        await using (var count = db.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM Game";
            if (Convert.ToInt64(await count.ExecuteScalarAsync()) > 0) return 0;
        }

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(seedPath));
        return document.RootElement.ValueKind switch
        {
            JsonValueKind.Object => await SeedFullSourceAsync(db, document.RootElement),
            JsonValueKind.Array => await SeedLegacyArrayAsync(db, document.RootElement),
            _ => throw new InvalidDataException("Bundled catalogue must be a JSON object or array.")
        };
    }

    private static async Task<int> SeedFullSourceAsync(SqliteConnection db, JsonElement root)
    {
        var releases = new List<SourceRelease>();
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object) continue;
            var record = property.Value;
            var title = ReadString(record, "title");
            var serial = ReadString(record, "serial");
            if (string.IsNullOrWhiteSpace(serial)) serial = property.Name;
            var region = ReadString(record, "region");
            var languages = ReadValues(record, "language", "languages");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(serial)) continue;
            if (IsClearNonGame(title)) continue;

            releases.Add(new SourceRelease(
                title.Trim(),
                ConceptualTitle(title),
                serial.Trim(),
                string.IsNullOrWhiteSpace(region) ? "Unknown" : region.Trim(),
                ReadJoined(record, "developer"),
                ReadJoined(record, "publisher"),
                ReadYear(record),
                languages,
                InferReleaseType(title)));
        }

        var groups = releases
            .GroupBy(r => NormalizeTitle(r.ConceptTitle), StringComparer.Ordinal)
            .OrderBy(g => g.Min(r => r.ConceptTitle), StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var transaction = db.BeginTransaction();
        var now = DateTimeOffset.UtcNow.ToString("O");
        var gameCount = 0;

        foreach (var group in groups)
        {
            var ordered = group.OrderBy(PrimaryRank).ThenBy(r => r.Serial, StringComparer.OrdinalIgnoreCase).ToList();
            var primary = ordered[0];
            var gameId = Guid.NewGuid().ToString("D");
            var firstYear = ordered.Where(r => r.Year.HasValue).Select(r => r.Year!.Value).DefaultIfEmpty().Min();
            var developer = !string.IsNullOrWhiteSpace(primary.Developer)
                ? primary.Developer
                : ordered.Select(r => r.Developer).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
            var canonicalTitle = primary.ConceptTitle;

            await using (var game = db.CreateCommand())
            {
                game.Transaction = transaction;
                game.CommandText = @"INSERT INTO Game(GameId,CanonicalTitle,SortTitle,FirstReleaseYear,Developer,CreatedUtc,UpdatedUtc)
VALUES($id,$title,$sort,$year,$developer,$now,$now)";
                game.Parameters.AddWithValue("$id", gameId);
                game.Parameters.AddWithValue("$title", canonicalTitle);
                game.Parameters.AddWithValue("$sort", canonicalTitle);
                game.Parameters.AddWithValue("$year", firstYear == 0 ? DBNull.Value : firstYear);
                game.Parameters.AddWithValue("$developer", string.IsNullOrWhiteSpace(developer) ? DBNull.Value : developer);
                game.Parameters.AddWithValue("$now", now);
                await game.ExecuteNonQueryAsync();
            }

            foreach (var source in ordered)
            {
                var releaseId = Guid.NewGuid().ToString("D");
                await using (var release = db.CreateCommand())
                {
                    release.Transaction = transaction;
                    release.CommandText = @"INSERT INTO Release(ReleaseId,GameId,ReleaseTitle,RegionCode,ReleaseType,Publisher,ReleaseYear,IsPrimary,Notes,CreatedUtc,UpdatedUtc)
VALUES($id,$game,$title,$region,$type,$publisher,$year,$primary,'Seeded from official GameDB-PS2 full source',$now,$now)";
                    release.Parameters.AddWithValue("$id", releaseId);
                    release.Parameters.AddWithValue("$game", gameId);
                    release.Parameters.AddWithValue("$title", source.Title);
                    release.Parameters.AddWithValue("$region", source.Region);
                    release.Parameters.AddWithValue("$type", source.ReleaseType);
                    release.Parameters.AddWithValue("$publisher", string.IsNullOrWhiteSpace(source.Publisher) ? DBNull.Value : source.Publisher);
                    release.Parameters.AddWithValue("$year", source.Year.HasValue ? source.Year.Value : DBNull.Value);
                    release.Parameters.AddWithValue("$primary", source == primary ? 1 : 0);
                    release.Parameters.AddWithValue("$now", now);
                    await release.ExecuteNonQueryAsync();
                }

                await using (var disc = db.CreateCommand())
                {
                    disc.Transaction = transaction;
                    disc.CommandText = "INSERT INTO Disc(DiscId,ReleaseId,DiscNumber,SerialRaw,SerialNormalized) VALUES($id,$release,1,$raw,$normalized)";
                    disc.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
                    disc.Parameters.AddWithValue("$release", releaseId);
                    disc.Parameters.AddWithValue("$raw", source.Serial);
                    disc.Parameters.AddWithValue("$normalized", NormalizeSerial(source.Serial));
                    await disc.ExecuteNonQueryAsync();
                }

                foreach (var language in source.Languages.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    await using var lang = db.CreateCommand();
                    lang.Transaction = transaction;
                    lang.CommandText = "INSERT OR IGNORE INTO ReleaseLanguage(ReleaseId,LanguageCode,EvidenceSourceRecordId,Confidence) VALUES($release,$language,NULL,0.9)";
                    lang.Parameters.AddWithValue("$release", releaseId);
                    lang.Parameters.AddWithValue("$language", language);
                    await lang.ExecuteNonQueryAsync();
                }
            }

            gameCount++;
        }

        transaction.Commit();
        return gameCount;
    }

    private static async Task<int> SeedLegacyArrayAsync(SqliteConnection db, JsonElement root)
    {
        using var transaction = db.BeginTransaction();
        var now = DateTimeOffset.UtcNow.ToString("O");
        var added = 0;
        foreach (var entry in root.EnumerateArray())
        {
            var title = ReadString(entry, "title");
            if (title.Length == 0) continue;
            var id = Guid.NewGuid().ToString("D");
            var releaseId = Guid.NewGuid().ToString("D");
            var year = ReadYear(entry);
            await using (var game = db.CreateCommand())
            {
                game.Transaction = transaction;
                game.CommandText = @"INSERT INTO Game(GameId,CanonicalTitle,SortTitle,FirstReleaseYear,Developer,CreatedUtc,UpdatedUtc)
VALUES($id,$title,$sort,$year,$dev,$now,$now)";
                game.Parameters.AddWithValue("$id", id);
                game.Parameters.AddWithValue("$title", title);
                game.Parameters.AddWithValue("$sort", title);
                game.Parameters.AddWithValue("$year", year.HasValue ? year.Value : DBNull.Value);
                game.Parameters.AddWithValue("$dev", ReadJoined(entry, "developer"));
                game.Parameters.AddWithValue("$now", now);
                await game.ExecuteNonQueryAsync();
            }
            await using (var release = db.CreateCommand())
            {
                release.Transaction = transaction;
                release.CommandText = @"INSERT INTO Release(ReleaseId,GameId,ReleaseTitle,RegionCode,Publisher,ReleaseYear,IsPrimary,CreatedUtc,UpdatedUtc)
VALUES($id,$game,$title,$region,$publisher,$year,1,$now,$now)";
                release.Parameters.AddWithValue("$id", releaseId);
                release.Parameters.AddWithValue("$game", id);
                release.Parameters.AddWithValue("$title", title);
                release.Parameters.AddWithValue("$region", ReadString(entry, "region"));
                release.Parameters.AddWithValue("$publisher", ReadJoined(entry, "publisher"));
                release.Parameters.AddWithValue("$year", year.HasValue ? year.Value : DBNull.Value);
                release.Parameters.AddWithValue("$now", now);
                await release.ExecuteNonQueryAsync();
            }
            var serial = ReadString(entry, "serial");
            if (serial.Length > 0)
            {
                await using var disc = db.CreateCommand();
                disc.Transaction = transaction;
                disc.CommandText = "INSERT INTO Disc(DiscId,ReleaseId,DiscNumber,SerialRaw,SerialNormalized) VALUES($id,$release,1,$serial,$normalized)";
                disc.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
                disc.Parameters.AddWithValue("$release", releaseId);
                disc.Parameters.AddWithValue("$serial", serial);
                disc.Parameters.AddWithValue("$normalized", NormalizeSerial(serial));
                await disc.ExecuteNonQueryAsync();
            }
            foreach (var language in ReadValues(entry, "language", "languages").Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await using var lang = db.CreateCommand();
                lang.Transaction = transaction;
                lang.CommandText = "INSERT OR IGNORE INTO ReleaseLanguage(ReleaseId,LanguageCode,EvidenceSourceRecordId,Confidence) VALUES($id,$lang,NULL,0.5)";
                lang.Parameters.AddWithValue("$id", releaseId);
                lang.Parameters.AddWithValue("$lang", language);
                await lang.ExecuteNonQueryAsync();
            }
            added++;
        }
        transaction.Commit();
        return added;
    }

    private static string ReadString(JsonElement record, string name)
    {
        if (!record.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return string.Empty;
        return value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? string.Empty : string.Empty;
    }

    private static string ReadJoined(JsonElement record, string name)
    {
        if (!record.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return string.Empty;
        if (value.ValueKind == JsonValueKind.String) return value.GetString()?.Trim() ?? string.Empty;
        if (value.ValueKind == JsonValueKind.Array)
            return string.Join(", ", value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()?.Trim() ?? string.Empty).Where(x => x.Length > 0));
        return string.Empty;
    }

    private static IReadOnlyList<string> ReadValues(JsonElement record, params string[] names)
    {
        JsonElement value = default;
        var found = false;
        foreach (var name in names)
            if (record.TryGetProperty(name, out value)) { found = true; break; }
        if (!found || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return Array.Empty<string>();
        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()?.Trim() ?? string.Empty).Where(x => x.Length > 0).ToArray();
        if (value.ValueKind == JsonValueKind.String)
            return (value.GetString() ?? string.Empty).Split(new[] { ',', ';', '/', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Array.Empty<string>();
    }

    private static int? ReadYear(JsonElement record)
    {
        foreach (var key in new[] { "release_date", "releaseDate", "year" })
        {
            if (!record.TryGetProperty(key, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numeric) && numeric is >= 1970 and <= 2100) return numeric;
            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString()?.Trim() ?? string.Empty;
                if (text.Length >= 4 && int.TryParse(text[..4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed is >= 1970 and <= 2100) return parsed;
            }
        }
        return null;
    }

    private static string ConceptualTitle(string value)
    {
        var cleaned = DiscSuffixPattern.Replace(value.Trim(), string.Empty).Trim();
        cleaned = cleaned.TrimEnd('-', ':', '–').Trim();
        return cleaned.Length == 0 ? value.Trim() : cleaned;
    }

    private static string NormalizeTitle(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();
        normalized = Regex.Replace(normalized, @"\s+", " ");
        return normalized;
    }

    private static string NormalizeSerial(string value) => new(value.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static bool IsClearNonGame(string title) => NonGameTitlePatterns.Any(pattern => pattern.IsMatch(title));

    private static int PrimaryRank(SourceRelease release)
    {
        var region = release.Region.ToLowerInvariant();
        if (region.Contains("ntsc-u") || region.Contains("north") || region.Contains("usa")) return 0;
        if (region.Contains("pal") || region.Contains("europe") || region.Contains("australia")) return 1;
        if (region.Contains("ntsc-j") || region.Contains("japan")) return 2;
        if (region.Contains("korea")) return 3;
        return 4;
    }

    private static string InferReleaseType(string title)
    {
        if (title.Contains("Platinum", StringComparison.OrdinalIgnoreCase)) return "Platinum";
        if (title.Contains("Greatest Hits", StringComparison.OrdinalIgnoreCase)) return "Greatest Hits";
        return "Retail";
    }

    private sealed record SourceRelease(
        string Title,
        string ConceptTitle,
        string Serial,
        string Region,
        string Developer,
        string Publisher,
        int? Year,
        IReadOnlyList<string> Languages,
        string ReleaseType);
}
