using System;
using System.IO;
using Microsoft.Data.Sqlite;
using PlayStation2GameLibrary;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
if (!File.Exists(Path.Combine(root, "database", "schema.sql")))
    root = Directory.GetCurrentDirectory();
var schema = Path.Combine(root, "database", "schema.sql");
var fullSeed = Path.Combine(root, "data", "PS2.data.full.json");
var legacySeed = Path.Combine(root, "data", "GameDB-PS2.seed.json");
var seed = File.Exists(fullSeed) ? fullSeed : legacySeed;
if (!File.Exists(schema) || !File.Exists(seed))
    throw new Exception("Missing bundled catalogue or schema.");

var temp = Path.Combine(Path.GetTempPath(), "PS2-Smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
var dbPath = Path.Combine(temp, "catalogue.db");
try
{
    await using (var db = new SqliteConnection($"Data Source={dbPath}"))
    {
        await db.OpenAsync();
        await using var command = db.CreateCommand();
        command.CommandText = await File.ReadAllTextAsync(schema);
        await command.ExecuteNonQueryAsync();
    }

    var seeded = await StarterCatalogue.EnsureSeededAsync(dbPath, seed);
    if (seeded < 3000) throw new Exception($"Too few conceptual games after seed: {seeded}");

    string gameId;
    long initialGames;
    long initialReleases;
    await using (var db = new SqliteConnection($"Data Source={dbPath}"))
    {
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();

        cmd.CommandText = "SELECT COUNT(*) FROM Game";
        initialGames = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        if (initialGames < 3000) throw new Exception($"Catalog not populated: {initialGames}");

        cmd.CommandText = "SELECT COUNT(*) FROM Release";
        initialReleases = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        if (File.Exists(fullSeed) && initialReleases <= initialGames)
            throw new Exception($"Full source lost regional releases: {initialReleases} releases for {initialGames} games.");

        if (File.Exists(fullSeed))
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Game WHERE CanonicalTitle='Grand Theft Auto: San Andreas' COLLATE NOCASE";
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync()) != 1)
                throw new Exception("San Andreas was not grouped into exactly one conceptual game.");

            cmd.CommandText = "SELECT GameId FROM Game WHERE CanonicalTitle='Grand Theft Auto: San Andreas' COLLATE NOCASE LIMIT 1";
            var sanId = (string)(await cmd.ExecuteScalarAsync() ?? throw new Exception("San Andreas missing"));

            cmd.CommandText = @"SELECT COUNT(*) FROM Release WHERE GameId=$san";
            cmd.Parameters.Clear(); cmd.Parameters.AddWithValue("$san", sanId);
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync()) < 3)
                throw new Exception("San Andreas does not contain the expected multiple regional releases.");

            cmd.CommandText = @"SELECT COUNT(*) FROM Disc d JOIN Release r ON r.ReleaseId=d.ReleaseId
WHERE r.GameId=$san AND d.SerialRaw IN ('SLES-52541','SLES-52927','SLUS-20946')";
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync()) != 3)
                throw new Exception("San Andreas expected serials were not preserved.");

            cmd.CommandText = @"SELECT d.SerialRaw FROM Release r JOIN Disc d ON d.ReleaseId=r.ReleaseId
WHERE r.GameId=$san AND r.IsPrimary=1 ORDER BY d.DiscNumber LIMIT 1";
            if (!string.Equals((string?)await cmd.ExecuteScalarAsync(), "SLUS-20946", StringComparison.OrdinalIgnoreCase))
                throw new Exception("San Andreas primary release is not the preferred NTSC-U/C serial SLUS-20946.");

            cmd.CommandText = @"SELECT COUNT(*) FROM ReleaseLanguage rl JOIN Release r ON r.ReleaseId=rl.ReleaseId
WHERE r.GameId=$san AND LOWER(rl.LanguageCode)='english'";
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync()) < 1)
                throw new Exception("San Andreas English language evidence is missing.");

            cmd.CommandText = "SELECT COUNT(*) FROM ReleaseLanguage WHERE LanguageCode LIKE '[%' OR LanguageCode LIKE '%]%'";
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync()) != 0)
                throw new Exception("Language arrays were incorrectly stored as one JSON-like language string.");

            cmd.CommandText = @"SELECT COUNT(*) FROM Game
WHERE CanonicalTitle LIKE '%Official PlayStation 2 Magazine Demo%'
   OR CanonicalTitle LIKE 'DVD Player Version %'
   OR CanonicalTitle LIKE '%Utility Disc%'";
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync()) != 0)
                throw new Exception("Clear demo/system/utility records leaked into the game catalog.");

            cmd.CommandText = @"SELECT COUNT(*) FROM Game g
WHERE EXISTS (SELECT 1 FROM Release r WHERE r.GameId=g.GameId)
AND NOT EXISTS (SELECT 1 FROM Release r WHERE r.GameId=g.GameId AND r.RegionCode NOT LIKE '%NTSC-J%' AND LOWER(r.RegionCode) NOT LIKE '%japan%' AND LOWER(r.RegionCode) NOT LIKE '%korea%')
AND NOT EXISTS (SELECT 1 FROM Release r JOIN ReleaseLanguage rl ON rl.ReleaseId=r.ReleaseId WHERE r.GameId=g.GameId AND LOWER(rl.LanguageCode) IN ('english','en','eng'))";
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync()) == 0)
                throw new Exception("All-language catalog policy failed: no JP/KR-only games without English were retained.");

            cmd.CommandText = "SELECT COUNT(*) FROM Game WHERE CanonicalTitle='0 Story' COLLATE NOCASE";
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync()) != 1)
                throw new Exception("0 Story is missing or its Disc 1 / Disc 2 titles were not grouped as one conceptual game.");

            cmd.CommandText = @"SELECT COUNT(DISTINCT d.SerialRaw) FROM Game g
JOIN Release r ON r.GameId=g.GameId
JOIN Disc d ON d.ReleaseId=r.ReleaseId
WHERE g.CanonicalTitle='0 Story' COLLATE NOCASE AND d.SerialRaw IN ('SLPM-65002','SLPM-65003')";
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync()) != 2)
                throw new Exception("0 Story did not retain both full-game disc serials SLPM-65002 / SLPM-65003.");

            cmd.CommandText = @"SELECT COUNT(*) FROM Game g
JOIN Release r ON r.GameId=g.GameId
JOIN Disc d ON d.ReleaseId=r.ReleaseId
WHERE g.CanonicalTitle='Taxi 3' COLLATE NOCASE AND d.SerialRaw='SLES-51377'";
            cmd.Parameters.Clear();
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync()) != 1)
                throw new Exception("PAL-only special title Taxi 3 / SLES-51377 was lost by the full-catalog policy.");
        }

        cmd.CommandText = "SELECT GameId FROM Game ORDER BY SortTitle LIMIT 1";
        cmd.Parameters.Clear();
        gameId = (string)(await cmd.ExecuteScalarAsync() ?? throw new Exception("No game found"));
        cmd.CommandText = @"INSERT INTO CollectionCopy(CopyId,GameId,CopyType,DigitalSizeBytes,Notes,CreatedUtc,UpdatedUtc)
VALUES($id,$game,'Physical',NULL,'Smoke test',$now,$now)";
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        cmd.Parameters.AddWithValue("$game", gameId);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    await using (var detailDb = new SqliteConnection($"Data Source={dbPath}"))
    {
        await detailDb.OpenAsync();
        await using var cmd = detailDb.CreateCommand();
        cmd.CommandText = @"INSERT INTO CollectionCopy(CopyId,GameId,CopyType,Notes,CreatedUtc,UpdatedUtc)
VALUES($copy,$game,'Physical','Second physical copy',$now,$now)";
        var secondId = Guid.NewGuid().ToString("D");
        cmd.Parameters.AddWithValue("$copy", secondId);
        cmd.Parameters.AddWithValue("$game", gameId);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
        cmd.CommandText = @"INSERT INTO CollectionCopyDetails(CopyId,Edition,CoverPresent,CasePresent,DiscPresent,ManualPresent,DiscCondition,UpdatedUtc)
VALUES($copy,'Collector Edition','Yes','Yes','Yes','No','Very Good',$now)";
        await cmd.ExecuteNonQueryAsync();
    }

    await using (var reopened = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly"))
    {
        await reopened.OpenAsync();
        await using var cmd = reopened.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM CollectionCopy WHERE GameId=$id AND CopyType='Physical'";
        cmd.Parameters.AddWithValue("$id", gameId);
        if (Convert.ToInt64(await cmd.ExecuteScalarAsync()) != 2)
            throw new Exception("Two independent collection copies did not persist after database reopen.");
        cmd.CommandText = "SELECT Edition || '|' || ManualPresent || '|' || DiscCondition FROM CollectionCopyDetails WHERE CopyId IN (SELECT CopyId FROM CollectionCopy WHERE Notes='Second physical copy')";
        if ((string?)cmd.ExecuteScalar() != "Collector Edition|No|Very Good") throw new Exception("Per-copy inventory metadata did not persist after reopen.");
        cmd.CommandText = "SELECT COUNT(*) FROM Game";
        if (Convert.ToInt64(await cmd.ExecuteScalarAsync()) != initialGames)
            throw new Exception("Catalogue changed after reopening database.");
    }

    Console.WriteLine($"PASS: {initialGames:N0} conceptual games / {initialReleases:N0} releases -> all-language catalog + 0 Story + multi-release checks -> collection persistence.");
}
finally
{
    try { Directory.Delete(temp, true); } catch (IOException) { }
}
