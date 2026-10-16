using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace PlayStation2GameLibrary;

internal sealed class AdminMainWindow : Window
{
    private readonly ObservableCollection<CoverRow> _rows = new();
    private readonly DataGrid _grid = new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, BorderThickness = new Thickness(0), RowHeight = 39 };
    private readonly TextBox _search = new() { Height = 36, Padding = new Thickness(9, 6, 9, 6) };
    private readonly ComboBox _statusFilter = new() { Height = 36, Width = 155 };
    private readonly Image _cover = new() { Stretch = Stretch.Uniform };
    private readonly StackPanel _placeholder = new();
    private readonly TextBlock _title = new() { FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Brush("#10243F"), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _file = DetailValue();
    private readonly TextBlock _path = DetailValue();
    private readonly TextBlock _serial = DetailValue();
    private readonly TextBlock _releaseSummary = DetailValue();
    private readonly TextBlock _status = new() { Foreground = Brush("#60758D"), FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private ICollectionView? _view;
    private string _databasePath = string.Empty;
    private AppSettings _settings = AppSettingsService.Load();
    private MediaLibraryService _mediaLibrary;

    public AdminMainWindow()
    {
        _mediaLibrary = new MediaLibraryService(_settings);
        Title = "PLAYSTATION 2 GAME LIBRARY — ADMIN — v0.1 BETA";
        Width = 1540; Height = 880; MinWidth = 1120; MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = Brush("#EEF5FB"); FontFamily = new FontFamily("Segoe UI");

        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); Content = root;
        var top = new Border { Background = Brush("#E6F0F8"), BorderBrush = Brush("#C9D9E8"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(18, 0, 18, 0) }; root.Children.Add(top);
        var topPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; top.Child = topPanel;
        topPanel.Children.Add(new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(7), Background = Brush("#111827"), Child = new TextBlock { Text = "PS", Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        topPanel.Children.Add(new TextBlock { Text = "PLAYSTATION 2 GAME LIBRARY", FontWeight = FontWeights.SemiBold, Foreground = Brush("#213A57"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
        topPanel.Children.Add(new Border { Margin = new Thickness(12, 0, 0, 0), Background = Brush("#FFE8E9"), CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 4, 8, 4), Child = new TextBlock { Text = "ADMIN", Foreground = Brush("#B3323D"), FontSize = 10, FontWeight = FontWeights.Bold } });

        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); Grid.SetRow(body, 1); root.Children.Add(body);
        var nav = new Border { Background = Brush("#F8FBFE"), BorderBrush = Brush("#D5E2EE"), BorderThickness = new Thickness(0, 0, 1, 0) }; body.Children.Add(nav);
        var navPanel = new StackPanel { Margin = new Thickness(12, 22, 12, 0) }; nav.Child = navPanel;
        AddNav(navPanel, "🎮  Catalog Editor", (_, _) => MessageBox.Show(this, "Catalog Editor will be the next ADMIN implementation step. Cover Management is already functional in this build.", "Catalog Editor"));
        AddNav(navPanel, "▧  Cover Management", async (_, _) => await LoadRowsAsync(), true);
        AddNav(navPanel, "⇲  Imports", async (_, _) => await ImportCatalogueAsync());
        AddNav(navPanel, "⤨  Merge / Split", (_, _) => MessageBox.Show(this, "Merge / Split will be implemented after Catalog Editor and import review UI.", "Merge / Split"));
        AddNav(navPanel, "⚙  Settings", (_, _) => OpenSettings());
        AddNav(navPanel, "?  Help", (_, _) => new HelpWindow { Owner = this }.ShowDialog());

        var main = new Grid { Margin = new Thickness(18) }; main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Grid.SetColumn(main, 1); body.Children.Add(main);
        var heading = new Grid { Margin = new Thickness(0, 0, 0, 14) }; heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); main.Children.Add(heading);
        var headingText = new StackPanel(); heading.Children.Add(headingText); headingText.Children.Add(new TextBlock { Text = "Cover Management", FontSize = 30, FontWeight = FontWeights.Bold, Foreground = Brush("#10243F") }); headingText.Children.Add(new TextBlock { Text = "One front cover per conceptual game. Review matches, missing covers and unmatched files.", Foreground = Brush("#60758D"), Margin = new Thickness(0, 5, 0, 0) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(actions, 1); heading.Children.Add(actions);
        var refresh = ActionButton("Refresh Media Library", true); refresh.Click += async (_, _) => await LoadRowsAsync(); actions.Children.Add(refresh);
        var open = ActionButton("Open Covers Folder", false); open.Click += (_, _) => OpenCoversFolder(); actions.Children.Add(open);
        var unmatched = ActionButton("Unmatched Images", false); unmatched.Click += async (_, _) => await ShowUnmatchedAsync(); actions.Children.Add(unmatched);

        var content = new Grid(); content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(390) }); Grid.SetRow(content, 1); main.Children.Add(content);
        var listCard = Card(); content.Children.Add(listCard); var list = new DockPanel(); listCard.Child = list;
        var filters = new Grid { Margin = new Thickness(0, 0, 0, 10) }; filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); filters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); DockPanel.SetDock(filters, Dock.Top); list.Children.Add(filters);
        _search.ToolTip = "Search title, serial or cover file"; _search.TextChanged += (_, _) => _view?.Refresh(); filters.Children.Add(_search);
        _statusFilter.ItemsSource = new[] { "All statuses", "Found", "Missing" }; _statusFilter.SelectedIndex = 0; _statusFilter.Margin = new Thickness(8, 0, 0, 0); _statusFilter.SelectionChanged += (_, _) => _view?.Refresh(); Grid.SetColumn(_statusFilter, 1); filters.Children.Add(_statusFilter);
        _grid.ItemsSource = _rows; _grid.SelectionChanged += async (_, _) => await ShowSelectedAsync();
        _grid.Columns.Add(new DataGridTextColumn { Header = "STATUS", Binding = new Binding("Status"), Width = 75 }); _grid.Columns.Add(new DataGridTextColumn { Header = "TITLE", Binding = new Binding("Title"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) }); _grid.Columns.Add(new DataGridTextColumn { Header = "PRIMARY SERIAL", Binding = new Binding("Serial"), Width = 125 }); _grid.Columns.Add(new DataGridTextColumn { Header = "COVER FILE", Binding = new Binding("CoverFile"), Width = 150 }); list.Children.Add(_grid);
        _view = CollectionViewSource.GetDefaultView(_rows); _view.Filter = MatchesFilter;

        var details = Card(); details.Margin = new Thickness(16, 0, 0, 0); Grid.SetColumn(details, 1); content.Children.Add(details);
        var detailScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; details.Child = detailScroll; var detail = new StackPanel(); detailScroll.Content = detail;
        var coverFrame = new Border { Height = 360, Background = Brush("#F4F7FA"), BorderBrush = Brush("#D3E1EE"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(8) }; detail.Children.Add(coverFrame);
        var coverGrid = new Grid(); coverFrame.Child = coverGrid; coverGrid.Children.Add(_cover); _placeholder.HorizontalAlignment = HorizontalAlignment.Center; _placeholder.VerticalAlignment = VerticalAlignment.Center; _placeholder.Children.Add(new TextBlock { Text = "▧", FontSize = 42, Foreground = Brush("#9FB0C4"), HorizontalAlignment = HorizontalAlignment.Center }); _placeholder.Children.Add(new TextBlock { Text = "No cover available", Foreground = Brush("#60758D"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) }); coverGrid.Children.Add(_placeholder);
        detail.Children.Add(_title); _title.Margin = new Thickness(0, 13, 0, 10); AddDetail(detail, "Primary serial", _serial); AddDetail(detail, "Cover file", _file); AddDetail(detail, "Image path", _path); AddDetail(detail, "Linked releases", _releaseSummary);
        detail.Children.Add(new Border { Margin = new Thickness(0, 13, 0, 0), Padding = new Thickness(11), Background = Brush("#EAF5FF"), CornerRadius = new CornerRadius(10), Child = new TextBlock { Text = "ONE GAME = ONE FRONT COVER\nAll linked regional releases share this same visual cover.", Foreground = Brush("#355D83"), FontSize = 11, TextWrapping = TextWrapping.Wrap } });
        Grid.SetRow(_status, 2); _status.Margin = new Thickness(0, 10, 0, 0); main.Children.Add(_status); Loaded += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            AppPaths.EnsurePortableFolders(); _databasePath = AppPaths.DatabasePath;
            await using var db = new SqliteConnection($"Data Source={_databasePath}"); await db.OpenAsync();
            var schemaPath = Path.Combine(AppContext.BaseDirectory, "database", "schema.sql"); if (File.Exists(schemaPath)) { await using var cmd = db.CreateCommand(); cmd.CommandText = await File.ReadAllTextAsync(schemaPath); await cmd.ExecuteNonQueryAsync(); }
            await StarterCatalogue.EnsureSeededAsync(_databasePath, Path.Combine(AppContext.BaseDirectory, "data", "starter-catalog.json")); await LoadRowsAsync();
            _status.Text = $"Portable database: {_databasePath}";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "ADMIN startup error", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async Task LoadRowsAsync()
    {
        if (string.IsNullOrWhiteSpace(_databasePath)) return; _settings = AppSettingsService.Load(); _mediaLibrary = new MediaLibraryService(_settings); var rows = new List<CoverRow>();
        await using var db = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly"); await db.OpenAsync(); await using var cmd = db.CreateCommand(); cmd.CommandText = @"SELECT g.GameId,g.CanonicalTitle,COALESCE((SELECT d.SerialRaw FROM Release r LEFT JOIN Disc d ON d.ReleaseId=r.ReleaseId WHERE r.GameId=g.GameId ORDER BY r.IsPrimary DESC,d.DiscNumber LIMIT 1),'') FROM Game g ORDER BY g.SortTitle COLLATE NOCASE"; await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) { var serial = reader.GetString(2); var cover = _mediaLibrary.FindCoverForSerial(serial); rows.Add(new CoverRow(reader.GetString(0), reader.GetString(1), serial, cover is null ? "Missing" : "Found", cover is null ? "—" : Path.GetFileName(cover), cover)); }
        _rows.Clear(); foreach (var row in rows) _rows.Add(row); _view?.Refresh(); _status.Text = $"{rows.Count(r => r.Status == "Found"):N0} with cover  •  {rows.Count(r => r.Status == "Missing"):N0} without cover  •  Media Library: {_mediaLibrary.CoversDirectory}  •  Data: {_databasePath}"; if (_rows.Count > 0 && _grid.SelectedItem is null) _grid.SelectedIndex = 0;
    }

    private bool MatchesFilter(object item) { if (item is not CoverRow row) return false; var status = _statusFilter.SelectedItem?.ToString() ?? "All statuses"; if (status != "All statuses" && status != row.Status) return false; var q = _search.Text.Trim(); return q.Length == 0 || row.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || row.Serial.Contains(q, StringComparison.OrdinalIgnoreCase) || row.CoverFile.Contains(q, StringComparison.OrdinalIgnoreCase); }

    private async Task ShowSelectedAsync()
    {
        if (_grid.SelectedItem is not CoverRow row) return; _title.Text = row.Title; _serial.Text = string.IsNullOrWhiteSpace(row.Serial) ? "—" : row.Serial; _file.Text = row.CoverFile; _path.Text = row.CoverPath ?? "—"; var image = _mediaLibrary.LoadImage(row.CoverPath); _cover.Source = image; _cover.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible; _placeholder.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
        var releases = new List<string>(); await using var db = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly"); await db.OpenAsync(); await using var cmd = db.CreateCommand(); cmd.CommandText = @"SELECT r.RegionCode,COALESCE((SELECT group_concat(d.SerialRaw, ', ') FROM Disc d WHERE d.ReleaseId=r.ReleaseId),'—') FROM Release r WHERE r.GameId=$id ORDER BY r.IsPrimary DESC,r.ReleaseYear"; cmd.Parameters.AddWithValue("$id", row.GameId); await using var reader = await cmd.ExecuteReaderAsync(); while (await reader.ReadAsync()) releases.Add($"{FriendlyRegion(reader.GetString(0))} — {reader.GetString(1)}"); _releaseSummary.Text = releases.Count == 0 ? "—" : string.Join(Environment.NewLine, releases.Take(8));
    }

    private async Task ShowUnmatchedAsync()
    {
        if (!Directory.Exists(_mediaLibrary.CoversDirectory)) { MessageBox.Show(this, "Covers folder does not exist yet."); return; } var known = new HashSet<string>(_rows.Select(r => r.Serial).Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase); var unmatched = Directory.EnumerateFiles(_mediaLibrary.CoversDirectory).Where(f => new[] { ".jpg", ".jpeg", ".png", ".webp" }.Contains(Path.GetExtension(f).ToLowerInvariant()) && !known.Contains(Path.GetFileNameWithoutExtension(f))).Select(Path.GetFileName).OrderBy(x => x).ToList(); var window = new Window { Title = "Unmatched Images — ADMIN", Owner = this, Width = 620, Height = 500, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush("#EEF5FB"), Content = new ListBox { ItemsSource = unmatched.Count == 0 ? new[] { "No unmatched images found." } : unmatched, Margin = new Thickness(18) } }; window.ShowDialog(); await Task.CompletedTask;
    }

    private void OpenCoversFolder() { try { Directory.CreateDirectory(_mediaLibrary.CoversDirectory); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = _mediaLibrary.CoversDirectory, UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Open Covers folder failed", MessageBoxButton.OK, MessageBoxImage.Error); } }
    private void OpenSettings() { if (new SettingsWindow { Owner = this }.ShowDialog() == true) _ = LoadRowsAsync(); }

    private async Task ImportCatalogueAsync()
    {
        if (string.IsNullOrWhiteSpace(_databasePath)) return; var dialog = new OpenFileDialog { Title = "Select GameDB-PS2 JSON", Filter = "JSON (*.json)|*.json|All files (*.*)|*.*", CheckFileExists = true }; if (dialog.ShowDialog(this) != true) return;
        try { var service = new GameDbJsonImportService(); var preview = await service.PreviewAsync(dialog.FileName, _databasePath); var message = $"Records: {preview.Rows.Count:N0}\nAdd: {preview.AddCount:N0}\nUpdate: {preview.UpdateCount:N0}\nSkip: {preview.SkipCount:N0}\nReject: {preview.RejectCount:N0}\nWarnings: {preview.WarningCount:N0}\n\nApply import?"; if (MessageBox.Show(this, message, "ADMIN Import Preview", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return; await service.ApplyAsync(preview, _databasePath); await LoadRowsAsync(); MessageBox.Show(this, "Catalogue import completed.", "ADMIN Import", MessageBoxButton.OK, MessageBoxImage.Information); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "ADMIN import failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private static string FriendlyRegion(string region) { var value = region.ToLowerInvariant(); if (value.Contains("north") || value.Contains("usa") || value.Contains("ntsc-u")) return "NTSC-U/C"; if (value.Contains("japan") || value.Contains("ntsc-j")) return "NTSC-J"; if (value.Contains("europe") || value.Contains("pal") || value.Contains("australia")) return "PAL"; return region; }
    private static void AddNav(Panel panel, string text, RoutedEventHandler click, bool selected = false) { var button = new Button { Content = text, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 11, 14, 11), Margin = new Thickness(0, 3, 0, 3), BorderThickness = new Thickness(0), Background = selected ? Brush("#DCEEFF") : Brushes.Transparent, Foreground = selected ? Brush("#1264BD") : Brush("#29466D") }; button.Click += click; panel.Children.Add(button); }
    private static Button ActionButton(string text, bool primary) => new() { Content = text, Margin = new Thickness(0, 0, 7, 0), Padding = new Thickness(12, 7, 12, 7), Background = primary ? Brush("#2F82E5") : Brushes.White, Foreground = primary ? Brushes.White : Brush("#17314D"), BorderBrush = primary ? Brush("#2F82E5") : Brush("#C3D5E6") };
    private static Border Card() => new() { Background = Brushes.White, BorderBrush = Brush("#D3E1EE"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(15), Padding = new Thickness(16) };
    private static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
    private static TextBlock DetailValue() => new() { Foreground = Brush("#17314D"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private static void AddDetail(Panel panel, string label, TextBlock value) { panel.Children.Add(new TextBlock { Text = label, Foreground = Brush("#5C7189"), FontSize = 11, Margin = new Thickness(0, 7, 0, 2) }); panel.Children.Add(value); }
    private sealed record CoverRow(string GameId, string Title, string Serial, string Status, string CoverFile, string? CoverPath);
}
