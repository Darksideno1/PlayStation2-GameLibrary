using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;

namespace PlayStation2GameLibrary;

/// <summary>Manage owned physical, digital and OPL copies without editing catalogue records.</summary>
public sealed class CollectionWindow : Window
{
    private readonly string _dbPath;
    private readonly string? _initialGameId;
    private readonly ObservableCollection<CopyRow> _rows = new();
    private readonly DataGrid _grid = new() { IsReadOnly = true, AutoGenerateColumns = false, Margin = new Thickness(0, 12, 0, 12) };
    private readonly ComboBox _game = new() { DisplayMemberPath = "Title", SelectedValuePath = "Id", MinWidth = 270 };
    private readonly ComboBox _type = new() { ItemsSource = new[] { "Physical", "Digital", "OPL" }, SelectedIndex = 0, Width = 110 };
    private readonly TextBox _size = new() { Width = 85, ToolTip = "Digital size in GB (optional)" };
    private readonly TextBox _notes = new() { Width = 190, ToolTip = "Optional notes" };
    private readonly TextBlock _summary = new() { Margin = new Thickness(0, 8, 0, 0) };
    private string? _editingCopyId;

    public CollectionWindow(string databasePath, string? initialGameId = null)
    {
        _dbPath = databasePath;
        _initialGameId = initialGameId;
        Title = "My Collection — PLAYSTATION 2 GAME LIBRARY";
        Width = 1100;
        Height = 680;
        MinWidth = 820;
        MinHeight = 490;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.White;
        var root = new DockPanel { Margin = new Thickness(22) };
        Content = root;
        var header = new StackPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "My Collection", FontSize = 25, FontWeight = FontWeights.Bold });
        header.Children.Add(new TextBlock { Text = "Manage Physical, Digital and OPL copies. Select a row to edit it.", Margin = new Thickness(0, 4, 0, 12) });
        var editor = new WrapPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(editor);
        editor.Children.Add(Label("Game"));
        editor.Children.Add(_game);
        editor.Children.Add(Label("Type"));
        editor.Children.Add(_type);
        editor.Children.Add(Label("Size (GB)"));
        editor.Children.Add(_size);
        editor.Children.Add(Label("Notes"));
        editor.Children.Add(_notes);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        header.Children.Add(actions);
        AddButton(actions, "Add / Save", async (_, _) => await SaveAsync());
        AddButton(actions, "New entry", (_, _) => ClearEditor());
        AddButton(actions, "Delete selected", async (_, _) => await DeleteAsync());
        header.Children.Add(_summary);
        _grid.Columns.Add(new DataGridTextColumn { Header = "Game", Binding = new System.Windows.Data.Binding("Title"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Type", Binding = new System.Windows.Data.Binding("CopyType"), Width = 105 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Size (GB)", Binding = new System.Windows.Data.Binding("SizeDisplay"), Width = 110 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Notes", Binding = new System.Windows.Data.Binding("Notes"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.ItemsSource = _rows;
        _grid.SelectionChanged += (_, _) => SelectCopy();
        root.Children.Add(_grid);
        Loaded += async (_, _) => await LoadAsync();
    }

    private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(12, 6, 6, 0), VerticalAlignment = VerticalAlignment.Center };
    private static void AddButton(Panel parent, string label, RoutedEventHandler click)
    {
        var button = new Button { Content = label, Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0) };
        button.Click += click;
        parent.Children.Add(button);
    }

    private async Task LoadAsync()
    {
        try
        {
            var games = new List<GameChoice>();
            await using var db = new SqliteConnection($"Data Source={_dbPath}");
            await db.OpenAsync();
            await using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "SELECT GameId,CanonicalTitle FROM Game ORDER BY SortTitle COLLATE NOCASE";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) games.Add(new GameChoice(reader.GetString(0), reader.GetString(1)));
            }
            _game.ItemsSource = games;
            if (_initialGameId is not null) _game.SelectedValue = _initialGameId;
            if (_game.SelectedIndex < 0 && games.Count > 0) _game.SelectedIndex = 0;
            await RefreshAsync(db);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Collection error", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async Task RefreshAsync(SqliteConnection db)
    {
        var rows = new List<CopyRow>();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = @"SELECT c.CopyId,c.GameId,g.CanonicalTitle,c.CopyType,c.DigitalSizeBytes,COALESCE(c.Notes,'')
FROM CollectionCopy c JOIN Game g ON g.GameId=c.GameId ORDER BY g.SortTitle,c.CopyType";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new CopyRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4), reader.GetString(5)));
        _rows.Clear();
        foreach (var row in rows) _rows.Add(row);
        var gb = rows.Sum(r => (decimal)(r.SizeBytes ?? 0)) / 1_000_000_000m;
        _summary.Text = $"{rows.Count} copies  •  Physical: {rows.Count(r => r.CopyType == "Physical")}  •  Digital: {rows.Count(r => r.CopyType == "Digital")}  •  OPL: {rows.Count(r => r.CopyType == "OPL")}  •  Digital storage: {gb:0.##} GB";
    }

    private void SelectCopy()
    {
        if (_grid.SelectedItem is not CopyRow row) return;
        _editingCopyId = row.CopyId;
        _game.SelectedValue = row.GameId;
        _type.SelectedItem = row.CopyType;
        _size.Text = row.SizeBytes is long bytes ? (bytes / 1_000_000_000m).ToString("0.###", CultureInfo.CurrentCulture) : "";
        _notes.Text = row.Notes;
    }

    private void ClearEditor()
    {
        _editingCopyId = null;
        _grid.SelectedItem = null;
        _type.SelectedIndex = 0;
        _size.Clear();
        _notes.Clear();
    }

    private async Task SaveAsync()
    {
        if (_game.SelectedValue is not string gameId || _type.SelectedItem is not string type)
        {
            MessageBox.Show(this, "Select a game and copy type first."); return;
        }
        long? bytes = null;
        if (!string.IsNullOrWhiteSpace(_size.Text))
        {
            if (!decimal.TryParse(_size.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var gb) || gb < 0 || gb > 1000000)
            {
                MessageBox.Show(this, "Enter a valid non-negative size in GB."); return;
            }
            bytes = (long)(gb * 1_000_000_000m);
        }
        try
        {
            await using var db = new SqliteConnection($"Data Source={_dbPath}");
            await db.OpenAsync();
            await using var cmd = db.CreateCommand();
            var now = DateTimeOffset.UtcNow.ToString("O");
            if (_editingCopyId is null)
                cmd.CommandText = @"INSERT INTO CollectionCopy(CopyId,GameId,CopyType,DigitalSizeBytes,Notes,CreatedUtc,UpdatedUtc)
VALUES($id,$game,$type,$size,$notes,$now,$now)";
            else
                cmd.CommandText = @"UPDATE CollectionCopy SET GameId=$game,ReleaseId=NULL,CopyType=$type,DigitalSizeBytes=$size,Notes=$notes,UpdatedUtc=$now WHERE CopyId=$id";
            cmd.Parameters.AddWithValue("$id", _editingCopyId ?? Guid.NewGuid().ToString("D"));
            cmd.Parameters.AddWithValue("$game", gameId);
            cmd.Parameters.AddWithValue("$type", type);
            cmd.Parameters.AddWithValue("$size", (object?)bytes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$notes", _notes.Text);
            cmd.Parameters.AddWithValue("$now", now);
            await cmd.ExecuteNonQueryAsync();
            ClearEditor();
            await RefreshAsync(db);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async Task DeleteAsync()
    {
        if (_grid.SelectedItem is not CopyRow row) return;
        if (MessageBox.Show(this, $"Delete this {row.CopyType} copy of {row.Title}?", "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await using var db = new SqliteConnection($"Data Source={_dbPath}");
            await db.OpenAsync();
            await using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM CollectionCopy WHERE CopyId=$id";
            cmd.Parameters.AddWithValue("$id", row.CopyId);
            await cmd.ExecuteNonQueryAsync();
            ClearEditor();
            await RefreshAsync(db);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Delete failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private sealed record GameChoice(string Id, string Title);
    private sealed record CopyRow(string CopyId, string GameId, string Title, string CopyType, long? SizeBytes, string Notes)
    {
        public string SizeDisplay => SizeBytes is long bytes ? $"{bytes / 1_000_000_000m:0.###}" : "—";
    }
}
