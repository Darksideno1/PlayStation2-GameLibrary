using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace PlayStation2GameLibrary;

internal sealed class ImportExportWindow : Window
{
    private readonly string _databasePath;
    private readonly TextBlock _backupStatus = StatusText();
    private readonly TextBlock _restoreStatus = StatusText();
    private readonly TextBlock _exportStatus = StatusText();
    private readonly TextBlock _importStatus = StatusText();
    private readonly TextBlock _importPreview = new() { Text = "Select a JSON or CSV file to preview collection entries.", Foreground = Brush("#60758D"), TextWrapping = TextWrapping.Wrap };
    private readonly ListBox _activity = new() { MinHeight = 120 };
    private string? _importPath;
    private ImportPreview? _preview;

    public ImportExportWindow(string databasePath)
    {
        _databasePath = databasePath;
        Title = "Import / Export — PLAYSTATION 2 GAME LIBRARY";
        Width = 1120;
        Height = 760;
        MinWidth = 880;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#EEF5FB");
        FontFamily = new FontFamily("Segoe UI");

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Content = scroll;
        var root = new StackPanel { Margin = new Thickness(22) };
        scroll.Content = root;
        root.Children.Add(new TextBlock { Text = "Import / Export", FontSize = 30, FontWeight = FontWeights.Bold, Foreground = Brush("#10243F") });
        root.Children.Add(new TextBlock { Text = "Back up, restore and move your personal PlayStation 2 collection data.", Foreground = Brush("#60758D"), Margin = new Thickness(0, 5, 0, 18) });

        var cards = new Grid();
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(cards);

        var backup = MakeCard("⤓", "Create Backup", "Create a complete SQLite backup of the catalogue, personal collection and settings stored in the database.", "#E8F7EF", "#1E9F61");
        Grid.SetRow(backup, 0); Grid.SetColumn(backup, 0); cards.Children.Add(backup);
        var backupPanel = (StackPanel)backup.Child;
        backupPanel.Children.Add(InfoRow("Collection data", "Included"));
        backupPanel.Children.Add(InfoRow("Catalogue data", "Included"));
        backupPanel.Children.Add(InfoRow("Cover image files", "Not copied"));
        var backupButton = ActionButton("Create Backup", true);
        backupButton.Click += async (_, _) => await CreateBackupAsync();
        backupPanel.Children.Add(ActionRow(backupButton));
        backupPanel.Children.Add(_backupStatus);

        var restore = MakeCard("↶", "Restore Backup", "Restore a compatible SQLite backup. A safety copy of the current database is created first.", "#FDEBED", "#D13A46");
        restore.Margin = new Thickness(12, 0, 0, 12);
        Grid.SetRow(restore, 0); Grid.SetColumn(restore, 1); cards.Children.Add(restore);
        var restorePanel = (StackPanel)restore.Child;
        var restoreChoose = ActionButton("Select Backup…", false);
        restoreChoose.Click += async (_, _) => await RestoreBackupAsync();
        restorePanel.Children.Add(ActionRow(restoreChoose));
        restorePanel.Children.Add(new Border { Background = Brush("#FFF6E7"), BorderBrush = Brush("#EED3A2"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(11), Margin = new Thickness(0, 10, 0, 0), Child = new TextBlock { Text = "Restore can overwrite the active catalogue and personal collection. Compatibility and SQLite integrity are checked before restore.", Foreground = Brush("#805817"), FontSize = 11, TextWrapping = TextWrapping.Wrap } });
        restorePanel.Children.Add(_restoreStatus);

        var export = MakeCard("⇱", "Export My Collection", "Export only your Physical and Digital collection entries for spreadsheets or archiving.", "#E8F3FF", "#287BD3");
        Grid.SetRow(export, 1); Grid.SetColumn(export, 0); cards.Children.Add(export);
        var exportPanel = (StackPanel)export.Child;
        exportPanel.Children.Add(InfoRow("JSON", "Full structured data"));
        exportPanel.Children.Add(InfoRow("CSV", "Spreadsheet-friendly"));
        var json = ActionButton("Export JSON", true);
        json.Click += async (_, _) => await ExportCollectionAsync(true);
        var csv = ActionButton("Export CSV", false);
        csv.Click += async (_, _) => await ExportCollectionAsync(false);
        exportPanel.Children.Add(ActionRow(json, csv));
        exportPanel.Children.Add(_exportStatus);

        var import = MakeCard("⇲", "Import Collection", "Preview a JSON or CSV collection export before adding entries. Exact duplicates are skipped.", "#FFF4DF", "#B97816");
        import.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetRow(import, 1); Grid.SetColumn(import, 1); cards.Children.Add(import);
        var importPanel = (StackPanel)import.Child;
        var chooseImport = ActionButton("Select JSON / CSV…", false);
        chooseImport.Click += async (_, _) => await SelectImportAsync();
        var applyImport = ActionButton("Import Entries", true);
        applyImport.IsEnabled = false;
        applyImport.Name = "ApplyImportButton";
        applyImport.Click += async (_, _) => await ApplyImportAsync(applyImport);
        importPanel.Children.Add(ActionRow(chooseImport, applyImport));
        var previewBorder = new Border { Background = Brush("#F9FCFF"), BorderBrush = Brush("#D3E1EE"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(11), Margin = new Thickness(0, 10, 0, 0), Child = _importPreview };
        importPanel.Children.Add(previewBorder);
        importPanel.Children.Add(_importStatus);

        var history = Card();
        history.Margin = new Thickness(0, 14, 0, 0);
        root.Children.Add(history);
        var historyPanel = new StackPanel();
        history.Child = historyPanel;
        historyPanel.Children.Add(new TextBlock { Text = "Recent Activity", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brush("#17314D") });
        historyPanel.Children.Add(new TextBlock { Text = "Actions performed during this application session.", Foreground = Brush("#70849A"), FontSize = 12, Margin = new Thickness(0, 3, 0, 10) });
        historyPanel.Children.Add(_activity);
        AddActivity("Ready — no import/export action has been performed yet.");
    }

    private async Task CreateBackupAsync()
    {
        var dialog = new SaveFileDialog { Title = "Save PS2 Library backup", Filter = "SQLite database (*.db)|*.db", FileName = $"PS2-Library-Backup-{DateTime.Now:yyyyMMdd-HHmmss}.db" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (SamePath(dialog.FileName, _databasePath)) throw new InvalidOperationException("Choose a destination different from the active database.");
            await BackupDatabaseAsync(_databasePath, dialog.FileName);
            _backupStatus.Text = $"Backup created: {dialog.FileName}";
            _backupStatus.Foreground = Brush("#168454");
            AddActivity($"Backup created — {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex)
        {
            _backupStatus.Text = ex.Message;
            _backupStatus.Foreground = Brush("#C03943");
        }
    }

    private async Task RestoreBackupAsync()
    {
        var dialog = new OpenFileDialog { Title = "Select PS2 Library backup", Filter = "SQLite database (*.db)|*.db", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (SamePath(dialog.FileName, _databasePath)) throw new InvalidOperationException("The selected file is already the active database.");
            await ValidateBackupAsync(dialog.FileName);
            if (MessageBox.Show(this, "Restore will replace the current database. A safety backup will be created first. Continue?", "Confirm restore", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var safety = _databasePath + ".before-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".db";
            await BackupDatabaseAsync(_databasePath, safety);
            await RestoreDatabaseAsync(dialog.FileName, _databasePath);
            _restoreStatus.Text = $"Restore completed. Safety backup: {safety}";
            _restoreStatus.Foreground = Brush("#168454");
            AddActivity($"Database restored — {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex)
        {
            _restoreStatus.Text = ex.Message;
            _restoreStatus.Foreground = Brush("#C03943");
        }
    }

    private async Task ExportCollectionAsync(bool json)
    {
        try
        {
            var rows = await ReadCollectionAsync();
            var dialog = new SaveFileDialog
            {
                Title = json ? "Export collection as JSON" : "Export collection as CSV",
                Filter = json ? "JSON file (*.json)|*.json" : "CSV file (*.csv)|*.csv",
                FileName = json ? "My_PS2_Collection.json" : "My_PS2_Collection.csv"
            };
            if (dialog.ShowDialog(this) != true) return;
            if (json)
            {
                var envelope = new CollectionExport("PLAYSTATION 2 GAME LIBRARY", "0.1 BETA", DateTimeOffset.UtcNow, rows);
                await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                var builder = new StringBuilder();
                builder.AppendLine("GameId,Title,CopyType,DigitalSizeBytes,Notes");
                foreach (var row in rows)
                    builder.AppendLine(string.Join(',', Csv(row.GameId), Csv(row.Title), Csv(row.CopyType), row.DigitalSizeBytes?.ToString(CultureInfo.InvariantCulture) ?? "", Csv(row.Notes)));
                await File.WriteAllTextAsync(dialog.FileName, builder.ToString(), new UTF8Encoding(true));
            }
            _exportStatus.Text = $"Exported {rows.Count:N0} entries to {dialog.FileName}";
            _exportStatus.Foreground = Brush("#168454");
            AddActivity($"Collection exported — {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex)
        {
            _exportStatus.Text = ex.Message;
            _exportStatus.Foreground = Brush("#C03943");
        }
    }

    private async Task SelectImportAsync()
    {
        var dialog = new OpenFileDialog { Title = "Select collection export", Filter = "Collection files (*.json;*.csv)|*.json;*.csv|JSON (*.json)|*.json|CSV (*.csv)|*.csv", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _importPath = dialog.FileName;
            var rows = Path.GetExtension(dialog.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase)
                ? await ReadCsvAsync(dialog.FileName)
                : await ReadJsonAsync(dialog.FileName);
            _preview = await BuildPreviewAsync(rows);
            _importPreview.Text = $"File: {Path.GetFileName(dialog.FileName)}\nEntries: {_preview.Rows.Count:N0}\nPhysical: {_preview.Rows.Count(r => r.CopyType == "Physical"):N0}\nDigital: {_preview.Rows.Count(r => r.CopyType == "Digital"):N0}\nDuplicates skipped: {_preview.Duplicates:N0}\nUnknown games rejected: {_preview.Rejected:N0}\nReady to add: {_preview.ToAdd:N0}";
            var apply = FindVisualChildren<Button>(this).FirstOrDefault(b => string.Equals(b.Name, "ApplyImportButton", StringComparison.Ordinal));
            if (apply is not null) apply.IsEnabled = _preview.ToAdd > 0;
            _importStatus.Text = "Preview complete; database unchanged.";
            _importStatus.Foreground = Brush("#60758D");
        }
        catch (Exception ex)
        {
            _preview = null;
            _importPreview.Text = ex.Message;
            _importStatus.Text = "Import preview failed.";
            _importStatus.Foreground = Brush("#C03943");
        }
    }

    private async Task ApplyImportAsync(Button applyButton)
    {
        if (_preview is null || _preview.ToAdd == 0) return;
        if (MessageBox.Show(this, $"Add {_preview.ToAdd:N0} collection entries? Exact duplicates will remain unchanged.", "Confirm collection import", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            var added = 0;
            await using var db = new SqliteConnection($"Data Source={_databasePath}");
            await db.OpenAsync();
            await using var tx = await db.BeginTransactionAsync();
            foreach (var row in _preview.Rows.Where(r => r.Action == "Add"))
            {
                await using var cmd = db.CreateCommand();
                cmd.Transaction = (SqliteTransaction)tx;
                cmd.CommandText = @"INSERT INTO CollectionCopy(CopyId,GameId,CopyType,DigitalSizeBytes,Notes,CreatedUtc,UpdatedUtc)
VALUES($id,$game,$type,$size,$notes,$now,$now)";
                cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
                cmd.Parameters.AddWithValue("$game", row.GameId);
                cmd.Parameters.AddWithValue("$type", row.CopyType);
                cmd.Parameters.AddWithValue("$size", (object?)row.DigitalSizeBytes ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$notes", row.Notes ?? string.Empty);
                cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                added += await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
            _importStatus.Text = $"Import completed: {added:N0} new entries added; {_preview.Duplicates:N0} duplicates skipped; {_preview.Rejected:N0} rejected.";
            _importStatus.Foreground = Brush("#168454");
            applyButton.IsEnabled = false;
            AddActivity($"Collection imported — {added:N0} new entries");
        }
        catch (Exception ex)
        {
            _importStatus.Text = ex.Message;
            _importStatus.Foreground = Brush("#C03943");
        }
    }

    private async Task<List<CollectionRow>> ReadCollectionAsync()
    {
        var rows = new List<CollectionRow>();
        await using var db = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly");
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = @"SELECT c.GameId,g.CanonicalTitle,c.CopyType,c.DigitalSizeBytes,COALESCE(c.Notes,'')
FROM CollectionCopy c JOIN Game g ON g.GameId=c.GameId WHERE c.CopyType IN ('Physical','Digital') ORDER BY g.SortTitle,c.CopyType";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new CollectionRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetInt64(3), reader.GetString(4)));
        return rows;
    }

    private static async Task<List<CollectionRow>> ReadJsonAsync(string path)
    {
        var text = await File.ReadAllTextAsync(path);
        using var document = JsonDocument.Parse(text);
        JsonElement array;
        if (document.RootElement.ValueKind == JsonValueKind.Array) array = document.RootElement;
        else if (document.RootElement.TryGetProperty("Entries", out var entries)) array = entries;
        else if (document.RootElement.TryGetProperty("entries", out entries)) array = entries;
        else if (document.RootElement.TryGetProperty("Collection", out entries)) array = entries;
        else if (document.RootElement.TryGetProperty("collection", out entries)) array = entries;
        else throw new InvalidDataException("JSON does not contain a collection entry array.");

        var rows = new List<CollectionRow>();
        foreach (var item in array.EnumerateArray())
        {
            string S(string a, string b = "") => item.TryGetProperty(a, out var v) ? v.GetString() ?? "" : b.Length > 0 && item.TryGetProperty(b, out v) ? v.GetString() ?? "" : "";
            long? size = null;
            if (item.TryGetProperty("DigitalSizeBytes", out var sizeEl) || item.TryGetProperty("digitalSizeBytes", out sizeEl))
                if (sizeEl.ValueKind == JsonValueKind.Number && sizeEl.TryGetInt64(out var value)) size = value;
            rows.Add(new CollectionRow(S("GameId", "gameId"), S("Title", "title"), S("CopyType", "copyType"), size, S("Notes", "notes")));
        }
        return rows;
    }

    private static async Task<List<CollectionRow>> ReadCsvAsync(string path)
    {
        var lines = await File.ReadAllLinesAsync(path);
        if (lines.Length == 0) return new List<CollectionRow>();
        var header = ParseCsv(lines[0]);
        var indexes = header.Select((name, index) => (name, index)).ToDictionary(x => x.name.Trim(), x => x.index, StringComparer.OrdinalIgnoreCase);
        string Get(IReadOnlyList<string> values, string name) => indexes.TryGetValue(name, out var index) && index < values.Count ? values[index] : string.Empty;
        var rows = new List<CollectionRow>();
        foreach (var line in lines.Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var values = ParseCsv(line);
            long? size = long.TryParse(Get(values, "DigitalSizeBytes"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
            rows.Add(new CollectionRow(Get(values, "GameId"), Get(values, "Title"), Get(values, "CopyType"), size, Get(values, "Notes")));
        }
        return rows;
    }

    private async Task<ImportPreview> BuildPreviewAsync(IEnumerable<CollectionRow> sourceRows)
    {
        var previewRows = new List<PreviewRow>();
        var duplicates = 0;
        var rejected = 0;
        await using var db = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly");
        await db.OpenAsync();
        foreach (var source in sourceRows)
        {
            var type = source.CopyType.Equals("Digital", StringComparison.OrdinalIgnoreCase) ? "Digital" : source.CopyType.Equals("Physical", StringComparison.OrdinalIgnoreCase) ? "Physical" : string.Empty;
            if (type.Length == 0) { rejected++; previewRows.Add(new PreviewRow(source.GameId, source.Title, source.CopyType, source.DigitalSizeBytes, source.Notes, "Reject")); continue; }
            var gameId = source.GameId;
            await using (var resolve = db.CreateCommand())
            {
                resolve.CommandText = string.IsNullOrWhiteSpace(gameId)
                    ? "SELECT GameId FROM Game WHERE CanonicalTitle=$title COLLATE NOCASE LIMIT 1"
                    : "SELECT GameId FROM Game WHERE GameId=$game LIMIT 1";
                if (string.IsNullOrWhiteSpace(gameId)) resolve.Parameters.AddWithValue("$title", source.Title);
                else resolve.Parameters.AddWithValue("$game", gameId);
                gameId = (await resolve.ExecuteScalarAsync())?.ToString() ?? string.Empty;
            }
            if (gameId.Length == 0) { rejected++; previewRows.Add(new PreviewRow(source.GameId, source.Title, type, source.DigitalSizeBytes, source.Notes, "Reject")); continue; }
            await using var duplicate = db.CreateCommand();
            duplicate.CommandText = @"SELECT COUNT(*) FROM CollectionCopy WHERE GameId=$game AND CopyType=$type AND COALESCE(DigitalSizeBytes,-1)=COALESCE($size,-1) AND COALESCE(Notes,'')=$notes";
            duplicate.Parameters.AddWithValue("$game", gameId);
            duplicate.Parameters.AddWithValue("$type", type);
            duplicate.Parameters.AddWithValue("$size", (object?)source.DigitalSizeBytes ?? DBNull.Value);
            duplicate.Parameters.AddWithValue("$notes", source.Notes ?? string.Empty);
            var exists = Convert.ToInt64(await duplicate.ExecuteScalarAsync()) > 0;
            if (exists) duplicates++;
            previewRows.Add(new PreviewRow(gameId, source.Title, type, source.DigitalSizeBytes, source.Notes, exists ? "Duplicate" : "Add"));
        }
        return new ImportPreview(previewRows, duplicates, rejected);
    }

    private static async Task ValidateBackupAsync(string path)
    {
        await using var db = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('Game','Release','CollectionCopy')";
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) != 3) throw new InvalidDataException("This is not a compatible PS2 Library backup.");
        cmd.CommandText = "PRAGMA quick_check";
        if (!string.Equals((await cmd.ExecuteScalarAsync())?.ToString(), "ok", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SQLite integrity check failed.");
    }

    private static async Task BackupDatabaseAsync(string sourcePath, string destinationPath)
    {
        if (File.Exists(destinationPath)) File.Delete(destinationPath);
        await using var source = new SqliteConnection($"Data Source={sourcePath};Mode=ReadOnly");
        await using var destination = new SqliteConnection($"Data Source={destinationPath}");
        await source.OpenAsync();
        await destination.OpenAsync();
        source.BackupDatabase(destination);
    }

    private static async Task RestoreDatabaseAsync(string sourcePath, string livePath)
    {
        await using var source = new SqliteConnection($"Data Source={sourcePath};Mode=ReadOnly");
        await using var live = new SqliteConnection($"Data Source={livePath}");
        await source.OpenAsync();
        await live.OpenAsync();
        source.BackupDatabase(live);
    }

    private static bool SamePath(string a, string b) => Path.GetFullPath(a).Equals(Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";

    private static IReadOnlyList<string> ParseCsv(string line)
    {
        var result = new List<string>();
        var builder = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { builder.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (ch == ',' && !quoted) { result.Add(builder.ToString()); builder.Clear(); }
            else builder.Append(ch);
        }
        result.Add(builder.ToString());
        return result;
    }

    private void AddActivity(string text) => _activity.Items.Insert(0, $"{DateTime.Now:dd MMM yyyy · HH:mm}    {text}");

    private static Border MakeCard(string glyph, string title, string description, string iconBackground, string iconForeground)
    {
        var card = Card();
        card.Margin = new Thickness(0, 0, 0, 12);
        var panel = new StackPanel();
        card.Child = panel;
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.Children.Add(header);
        header.Children.Add(new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(11), Background = Brush(iconBackground), Child = new TextBlock { Text = glyph, FontSize = 22, Foreground = Brush(iconForeground), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        var text = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(text, 1); header.Children.Add(text);
        text.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Brush("#17314D") });
        text.Children.Add(new TextBlock { Text = description, Foreground = Brush("#60758D"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        return card;
    }

    private static Border Card() => new() { Background = Brushes.White, BorderBrush = Brush("#D3E1EE"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(17) };
    private static Brush Brush(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;
    private static TextBlock StatusText() => new() { Foreground = Brush("#60758D"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };

    private static Grid InfoRow(string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 7, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = label, Foreground = Brush("#506980"), FontSize = 12 });
        var right = new TextBlock { Text = value, FontWeight = FontWeights.SemiBold, Foreground = Brush("#17314D"), FontSize = 12 };
        Grid.SetColumn(right, 1); grid.Children.Add(right);
        return grid;
    }

    private static Button ActionButton(string text, bool primary) => new() { Content = text, Padding = new Thickness(14, 7, 14, 7), Background = primary ? Brush("#2F82E5") : Brushes.White, Foreground = primary ? Brushes.White : Brush("#17314D"), BorderBrush = primary ? Brush("#2F82E5") : Brush("#C3D5E6"), FontWeight = FontWeights.SemiBold };
    private static StackPanel ActionRow(params Button[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var button in buttons) { button.Margin = new Thickness(0, 0, 8, 0); panel.Children.Add(button); }
        return panel;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    private sealed record CollectionExport(string Product, string Version, DateTimeOffset CreatedUtc, IReadOnlyList<CollectionRow> Entries);
    private sealed record CollectionRow(string GameId, string Title, string CopyType, long? DigitalSizeBytes, string Notes);
    private sealed record PreviewRow(string GameId, string Title, string CopyType, long? DigitalSizeBytes, string Notes, string Action);
    private sealed record ImportPreview(IReadOnlyList<PreviewRow> Rows, int Duplicates, int Rejected)
    {
        public int ToAdd => Rows.Count(r => r.Action == "Add");
    }
}
