using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;
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
    private readonly TextBox _search = new() { MinWidth = 200, Height = 34 };
    private readonly ComboBox _filter = new() { Width = 150, Height = 34 };
    private readonly TextBlock _detailTitle = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _detailBody = new() { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(79, 101, 130)) };
    private readonly TextBlock _total = new() { FontSize = 22, FontWeight = FontWeights.Bold };
    private readonly TextBlock _physical = new() { FontSize = 22, FontWeight = FontWeights.Bold };
    private readonly TextBlock _digital = new() { FontSize = 22, FontWeight = FontWeights.Bold };
    private readonly TextBlock _opl = new() { FontSize = 22, FontWeight = FontWeights.Bold };
    private readonly TextBlock _storage = new() { FontSize = 22, FontWeight = FontWeights.Bold };
    private ICollectionView? _view;

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
        Background = Brush("#F3F6FB");
        FontFamily = new FontFamily("Segoe UI");
        var root = new DockPanel { Margin = new Thickness(24) };
        Content = root;
        var header = new StackPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "MY LIBRARY  /  COLLECTION", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brush("#1261E8") });
        header.Children.Add(new TextBlock { Text = "My Collection", FontSize = 29, FontWeight = FontWeights.Bold, Foreground = Brush("#17233A"), Margin = new Thickness(0, 6, 0, 3) });
        header.Children.Add(new TextBlock { Text = "Your physical games, digital copies and OPL library in one place.", Foreground = Brush("#70809A"), Margin = new Thickness(0, 0, 0, 18) });
        var metrics = new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, 0, 17) };
        header.Children.Add(metrics);
        Metric(metrics, "TOTAL COPIES", _total);
        Metric(metrics, "PHYSICAL", _physical);
        Metric(metrics, "DIGITAL", _digital);
        Metric(metrics, "OPL", _opl);
        Metric(metrics, "DIGITAL STORAGE (GB)", _storage);
        var filters = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(filters);
        var newButton = new Button { Content = "+ New copy", Background = Brush("#1261E8"), Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, Padding = new Thickness(16, 8, 16, 8) };
        newButton.Click += (_, _) => ClearEditor();
        DockPanel.SetDock(newButton, Dock.Right);
        filters.Children.Add(newButton);
        _filter.ItemsSource = new[] { "All copies", "Physical", "Digital", "OPL" };
        _filter.SelectedIndex = 0;
        _filter.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(_filter, Dock.Right);
        filters.Children.Add(_filter);
        _search.ToolTip = "Search your collection by title or notes";
        _search.Margin = new Thickness(0, 0, 12, 0);
        filters.Children.Add(_search);
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        root.Children.Add(layout);
        var listCard = Card();
        Grid.SetColumn(listCard, 0);
        layout.Children.Add(listCard);
        var listPanel = new DockPanel();
        listCard.Child = listPanel;
        listPanel.Children.Add(_grid);
        _grid.Margin = new Thickness(0);
        _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _grid.RowHeight = 39;
        _grid.BorderThickness = new Thickness(0);
        _grid.Columns.Add(new DataGridTextColumn { Header = "GAME TITLE", Binding = new Binding("Title"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "FORMAT", Binding = new Binding("CopyType"), Width = 90 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "SIZE (GB)", Binding = new Binding("SizeDisplay"), Width = 105 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "NOTES", Binding = new Binding("Notes"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.ItemsSource = _rows;
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = MatchesFilter;
        _search.TextChanged += (_, _) => _view?.Refresh();
        _filter.SelectionChanged += (_, _) => _view?.Refresh();
        _grid.SelectionChanged += (_, _) => SelectCopy();
        var side = new Border { Margin = new Thickness(16, 0, 0, 0), Background = Brushes.White, BorderBrush = Brush("#DBE4EF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(18) };
        Grid.SetColumn(side, 1);
        layout.Children.Add(side);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        side.Child = scroll;
        var edit = new StackPanel();
        scroll.Content = edit;
        edit.Children.Add(new TextBlock { Text = "COPY DETAILS", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brush("#1261E8") });
        edit.Children.Add(_detailTitle);
        _detailTitle.Margin = new Thickness(0, 10, 0, 7);
        edit.Children.Add(_detailBody);
        _detailBody.Margin = new Thickness(0, 0, 0, 14);
        edit.Children.Add(FieldLabel("Game"));
        edit.Children.Add(_game);
        _game.MaxWidth = 310;
        _game.HorizontalAlignment = HorizontalAlignment.Stretch;
        edit.Children.Add(FieldLabel("Collection type"));
        edit.Children.Add(_type);
        _type.HorizontalAlignment = HorizontalAlignment.Stretch;
        edit.Children.Add(FieldLabel("Digital size (GB)"));
        edit.Children.Add(_size);
        _size.HorizontalAlignment = HorizontalAlignment.Stretch;
        edit.Children.Add(FieldLabel("Notes"));
        edit.Children.Add(_notes);
        _notes.HorizontalAlignment = HorizontalAlignment.Stretch;
        _notes.TextWrapping = TextWrapping.Wrap;
        _notes.AcceptsReturn = true;
        _notes.MinHeight = 68;
        var actions = new StackPanel { Margin = new Thickness(0, 15, 0, 0) };
        edit.Children.Add(actions);
        AddButton(actions, "Save copy", async (_, _) => await SaveAsync());
        AddButton(actions, "New copy", (_, _) => ClearEditor());
        AddButton(actions, "Delete selected copy", async (_, _) => await DeleteAsync());
        edit.Children.Add(_summary);
        _summary.TextWrapping = TextWrapping.Wrap;
        _summary.Foreground = Brush("#70809A");
        ClearEditor();
        Loaded += async (_, _) => await LoadAsync();
    }

    private static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
    private static Border Card() => new() { Background = Brushes.White, BorderBrush = Brush("#DBE4EF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(15) };
    private static TextBlock FieldLabel(string name) => new() { Text = name, Foreground = Brush("#52657D"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 5) };
    private static void Metric(Panel parent, string title, TextBlock number)
    {
        var card = Card();
        card.Margin = new Thickness(0, 0, 10, 0);
        var stack = new StackPanel();
        card.Child = stack;
        stack.Children.Add(new TextBlock { Text = title, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brush("#70809A") });
        number.Foreground = Brush("#17233A");
        number.Margin = new Thickness(0, 7, 0, 0);
        stack.Children.Add(number);
        parent.Children.Add(card);
    }
    private bool MatchesFilter(object item)
    {
        if (item is not CopyRow row) return false;
        var selected = _filter.SelectedItem as string;
        if (selected is not null && selected != "All copies" && selected != row.CopyType) return false;
        var term = _search.Text.Trim();
        return term.Length == 0 || row.Title.Contains(term, StringComparison.OrdinalIgnoreCase) || row.Notes.Contains(term, StringComparison.OrdinalIgnoreCase);
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
        _view?.Refresh();
        _total.Text = rows.Count.ToString("N0");
        _physical.Text = rows.Count(r => r.CopyType == "Physical").ToString("N0");
        _digital.Text = rows.Count(r => r.CopyType == "Digital").ToString("N0");
        _opl.Text = rows.Count(r => r.CopyType == "OPL").ToString("N0");
        var gb = rows.Sum(r => (decimal)(r.SizeBytes ?? 0)) / 1_000_000_000m;
        _storage.Text = gb.ToString("0.##");
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
        _detailTitle.Text = row.Title;
        _detailBody.Text = $"{row.CopyType} copy  •  {(row.SizeBytes is long bytes ? $"{bytes / 1_000_000_000m:0.###} GB" : "Size not recorded")}";
    }

    private void ClearEditor()
    {
        _editingCopyId = null;
        _grid.SelectedItem = null;
        _type.SelectedIndex = 0;
        _size.Clear();
        _notes.Clear();
        _detailTitle.Text = "Add a collection copy";
        _detailBody.Text = "Choose a game and format, then save. Select an existing row to edit its details.";
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
