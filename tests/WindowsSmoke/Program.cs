using System;
using System.IO;
using Microsoft.Data.Sqlite;
using PlayStation2GameLibrary;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
if (!File.Exists(Path.Combine(root, "database", "schema.sql")))
    root = Directory.GetCurrentDirectory();
var schema = Path.Combine(root, "database", "schema.sql");
var seed = Path.Combine(root, "data", "GameDB-PS2.seed.json");
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
    var importer = new GameDbJsonImportService();
    var preview = await importer.PreviewAsync(seed, dbPath);
    if (preview.AddCount < 3000) throw new Exception($"Too few bundled games: {preview.AddCount}");
    await importer.ApplyAsync(preview, dbPath);
    string gameId;
    long initial;
    await using (var db = new SqliteConnection($"Data Source={dbPath}"))
    {
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Game";
        initial = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        if (initial < 3000) throw new Exception($"Catalog not populated: {initial}");
        cmd.CommandText = "SELECT GameId FROM Game ORDER BY SortTitle LIMIT 1";
        gameId = (string)(await cmd.ExecuteScalarAsync() ?? throw new Exception("No game found"));
        cmd.CommandText = @"INSERT INTO CollectionCopy(CopyId,GameId,CopyType,DigitalSizeBytes,Notes,CreatedUtc,UpdatedUtc)
VALUES($id,$game,'Physical',NULL,'Smoke test', $now,$now)";
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        cmd.Parameters.AddWithValue("$game", gameId);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }
    // Two physical copies of the same game retain independent metadata.
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
        if (Convert.ToInt64(await cmd.ExecuteScalarAsync()) != 1)
            throw new Exception("Collection entry did not persist after database reopen.");
        cmd.CommandText = "SELECT COUNT(*) FROM Game";
        if (Convert.ToInt64(await cmd.ExecuteScalarAsync()) != initial)
            throw new Exception("Catalogue changed after reopening database.");
    }
    Console.WriteLine($"PASS: seed -> {initial} games -> collection insert -> persisted after database reopen.");
}
finally
{
    try { Directory.Delete(temp, true); } catch (IOException) { }
}
