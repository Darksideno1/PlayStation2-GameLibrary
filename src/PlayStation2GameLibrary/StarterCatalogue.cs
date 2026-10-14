using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PlayStation2GameLibrary;

/// <summary>Initializes a useful offline catalogue from the bundled curated GameDB snapshot.</summary>
public static class StarterCatalogue
{
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
        using var transaction = db.BeginTransaction();
        var now = DateTimeOffset.UtcNow.ToString("O");
        int added = 0;
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            string Get(string key) => entry.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
            var title = Get("title").Trim();
            if (title.Length == 0) continue;
            var id = Guid.NewGuid().ToString("D");
            var releaseId = Guid.NewGuid().ToString("D");
            await using (var game = db.CreateCommand())
            {
                game.Transaction = transaction;
                game.CommandText = @"INSERT INTO Game(GameId,CanonicalTitle,SortTitle,FirstReleaseYear,Developer,CreatedUtc,UpdatedUtc)
VALUES($id,$title,$sort,$year,$dev,$now,$now)";
                game.Parameters.AddWithValue("$id", id);
                game.Parameters.AddWithValue("$title", title);
                game.Parameters.AddWithValue("$sort", title);
                game.Parameters.AddWithValue("$year", entry.TryGetProperty("year", out var year) && year.ValueKind == JsonValueKind.Number && year.TryGetInt32(out int yr) && yr >= 1970 && yr <= 2100 ? yr : DBNull.Value);
                game.Parameters.AddWithValue("$dev", Get("developer"));
                game.Parameters.AddWithValue("$now", now);
                await game.ExecuteNonQueryAsync();
            }
            await using (var release = db.CreateCommand())
            {
                release.Transaction = transaction;
                release.CommandText = @"INSERT INTO Release(ReleaseId,GameId,ReleaseTitle,RegionCode,Publisher,ReleaseYear,CreatedUtc,UpdatedUtc)
VALUES($id,$game,$title,$region,$publisher,$year,$now,$now)";
                release.Parameters.AddWithValue("$id", releaseId);
                release.Parameters.AddWithValue("$game", id);
                release.Parameters.AddWithValue("$title", title);
                release.Parameters.AddWithValue("$region", Get("region"));
                release.Parameters.AddWithValue("$publisher", Get("publisher"));
                release.Parameters.AddWithValue("$year", entry.TryGetProperty("year", out var year) && year.ValueKind == JsonValueKind.Number && year.TryGetInt32(out int yr) && yr >= 1970 && yr <= 2100 ? yr : DBNull.Value);
                release.Parameters.AddWithValue("$now", now);
                await release.ExecuteNonQueryAsync();
            }
            var serial = Get("serial");
            if (serial.Length > 0)
            {
                await using var disc = db.CreateCommand();
                disc.Transaction = transaction;
                disc.CommandText = "INSERT INTO Disc(DiscId,ReleaseId,DiscNumber,SerialRaw,SerialNormalized) VALUES($id,$release,1,$serial,$serial)";
                disc.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
                disc.Parameters.AddWithValue("$release", releaseId);
                disc.Parameters.AddWithValue("$serial", serial);
                await disc.ExecuteNonQueryAsync();
            }
            var languages = Get("language").Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var language in languages.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await using var lang = db.CreateCommand();
                lang.Transaction = transaction;
                lang.CommandText = "INSERT INTO ReleaseLanguage(ReleaseId,LanguageCode,EvidenceSourceRecordId,Confidence) VALUES($id,$lang,NULL,0.5)";
                lang.Parameters.AddWithValue("$id", releaseId);
                lang.Parameters.AddWithValue("$lang", language);
                await lang.ExecuteNonQueryAsync();
            }
            added++;
        }
        transaction.Commit();
        return added;
    }
}
