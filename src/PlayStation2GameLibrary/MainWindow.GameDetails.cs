using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Data.Sqlite;

namespace PlayStation2GameLibrary;

public partial class MainWindow
{
    private Grid? _inlineGameDetailsPage;
    private readonly ObservableCollection<InlineReleaseItem> _inlineReleases = new();
    private bool _catalogReturnHooked;

    internal Task OpenGameDetailsInlineAsync(string gameId) => ShowInlineGameDetailsAsync(gameId);

    private async Task ShowInlineGameDetailsAsync(string gameId)
    {
        if (CatalogPage is null) return;
        if (!_catalogReturnHooked)
        {
            CatalogNavButton.Click += (_, _) => ReturnToCatalogView();
            _catalogReturnHooked = true;
        }

        foreach (UIElement child in CatalogPage.Children) child.Visibility = Visibility.Collapsed;
        if (_inlineGameDetailsPage is not null) CatalogPage.Children.Remove(_inlineGameDetailsPage);

        var page = BuildInlineGameDetailsPage();
        _inlineGameDetailsPage = page;
        CatalogPage.Children.Add(page);
        Grid.SetColumnSpan(page, 2);
        PageTitle.Text = "Game Details";
        PageSubtitle.Text = "Game information, front cover and linked regional releases.";

        try
        {
            await LoadInlineGameDetailsAsync(gameId, page);
        }
        catch (Exception ex)
        {
            var status = FindNamed<TextBlock>(page, "InlineDetailsStatus");
            if (status is not null) status.Text = "Could not load game details.";
            MessageBox.Show(this, ex.Message, "Game details error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private Grid BuildInlineGameDetailsPage()
    {
        var root = new Grid { Background = Brush("#EEF5FB") };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(toolbar);
        var back = new Button { Content = "← Back to Catalog", Padding = new Thickness(12, 8, 12, 8) };
        back.Click += (_, _) => ReturnToCatalogView();
        toolbar.Children.Add(back);
        var status = new TextBlock { Name = "InlineDetailsStatus", Foreground = Brush("#60758D"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(status, 1); toolbar.Children.Add(status);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(390) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(content, 1); root.Children.Add(content);

        var overview = InlineCard();
        content.Children.Add(overview);
        var overviewScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        overview.Child = overviewScroll;
        var overviewStack = new StackPanel(); overviewScroll.Content = overviewStack;
        var coverFrame = new Border { Height = 340, Background = Brush("#F4F7FA"), BorderBrush = Brush("#D3E1EE"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(8) };
        overviewStack.Children.Add(coverFrame);
        var coverGrid = new Grid(); coverFrame.Child = coverGrid;
        var cover = new Image { Name = "InlineCoverImage", Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed }; coverGrid.Children.Add(cover);
        var placeholder = new StackPanel { Name = "InlineNoCover", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        placeholder.Children.Add(new TextBlock { Text = "▧", FontSize = 42, Foreground = Brush("#9FB0C4"), HorizontalAlignment = HorizontalAlignment.Center });
        placeholder.Children.Add(new TextBlock { Text = "No cover available", FontWeight = FontWeights.SemiBold, Foreground = Brush("#60758D"), Margin = new Thickness(0, 8, 0, 0) });
        coverGrid.Children.Add(placeholder);
        overviewStack.Children.Add(new TextBlock { Name = "InlineGameTitle", FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Brush("#10243F"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 4) });
        overviewStack.Children.Add(new TextBlock { Name = "InlineMetadata", Foreground = Brush("#60758D"), FontSize = 12, Margin = new Thickness(0, 0, 0, 10) });
        AddInlineDetail(overviewStack, "Primary serial", "InlinePrimarySerial");
        AddInlineDetail(overviewStack, "Region", "InlinePrimaryRegion");
        AddInlineDetail(overviewStack, "Publisher", "InlinePublisher");
        overviewStack.Children.Add(new TextBlock { Text = "Languages", Foreground = Brush("#5C7189"), FontSize = 11, Margin = new Thickness(0, 8, 0, 3) });
        overviewStack.Children.Add(new StackPanel { Name = "InlineLanguageFlags", Orientation = Orientation.Horizontal });
        var add = new Button { Name = "InlineAddButton", Content = "+ Add to Collection", Background = Brush("#2F82E5"), Foreground = Brushes.White, BorderBrush = Brush("#2F82E5"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 15, 0, 0), Padding = new Thickness(14, 8, 14, 8) };
        overviewStack.Children.Add(add);

        var details = InlineCard(); details.Margin = new Thickness(16, 0, 0, 0); Grid.SetColumn(details, 1); content.Children.Add(details);
        var detailsGrid = new Grid(); detailsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); detailsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); details.Child = detailsGrid;
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 12) }; detailsGrid.Children.Add(head);
        head.Children.Add(new TextBlock { Text = "Regions & Releases", FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = Brush("#17314D") });
        head.Children.Add(new TextBlock { Name = "InlineDescription", Text = "No description available.", Foreground = Brush("#60758D"), TextWrapping = TextWrapping.Wrap, MaxHeight = 92, Margin = new Thickness(0, 5, 0, 0) });
        var grid = new DataGrid { Name = "InlineReleasesGrid", ItemsSource = _inlineReleases, AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, BorderThickness = new Thickness(0), RowHeight = 43 };
        grid.Columns.Add(new DataGridTemplateColumn { Header = "REGION", Width = 120, CellTemplate = RegionTemplate() });
        grid.Columns.Add(new DataGridTextColumn { Header = "SERIAL", Binding = new System.Windows.Data.Binding("Serial"), Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "YEAR", Binding = new System.Windows.Data.Binding("Year"), Width = 70 });
        grid.Columns.Add(new DataGridTextColumn { Header = "TYPE", Binding = new System.Windows.Data.Binding("ReleaseType"), Width = 95 });
        grid.Columns.Add(new DataGridTextColumn { Header = "PUBLISHER", Binding = new System.Windows.Data.Binding("Publisher"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTemplateColumn { Header = "LANGUAGES", Width = 170, CellTemplate = LanguageTemplate() });
        Grid.SetRow(grid, 1); detailsGrid.Children.Add(grid);
        return root;
    }

    private async Task LoadInlineGameDetailsAsync(string gameId, Grid page)
    {
        var title = FindNamed<TextBlock>(page, "InlineGameTitle") ?? throw new InvalidOperationException("Game Details title control was not created.");
        var meta = FindNamed<TextBlock>(page, "InlineMetadata") ?? throw new InvalidOperationException("Game Details metadata control was not created.");
        var description = FindNamed<TextBlock>(page, "InlineDescription") ?? throw new InvalidOperationException("Game Details description control was not created.");
        var serialText = FindNamed<TextBlock>(page, "InlinePrimarySerial") ?? throw new InvalidOperationException("Game Details serial control was not created.");
        var regionText = FindNamed<TextBlock>(page, "InlinePrimaryRegion") ?? throw new InvalidOperationException("Game Details region control was not created.");
        var publisherText = FindNamed<TextBlock>(page, "InlinePublisher") ?? throw new InvalidOperationException("Game Details publisher control was not created.");
        var flags = FindNamed<StackPanel>(page, "InlineLanguageFlags") ?? throw new InvalidOperationException("Game Details language controls were not created.");
        var cover = FindNamed<Image>(page, "InlineCoverImage") ?? throw new InvalidOperationException("Game Details cover control was not created.");
        var placeholder = FindNamed<StackPanel>(page, "InlineNoCover") ?? throw new InvalidOperationException("Game Details cover placeholder was not created.");
        var status = FindNamed<TextBlock>(page, "InlineDetailsStatus") ?? throw new InvalidOperationException("Game Details status control was not created.");
        var add = FindNamed<Button>(page, "InlineAddButton") ?? throw new InvalidOperationException("Game Details collection button was not created.");
        add.Click += (_, _) => { new CollectionWindow(_databasePath, gameId) { Owner = this }.ShowDialog(); _ = RefreshGamesAsync(); _ = RefreshFooterAsync(); _ = RefreshCollectionPageAsync(); };

        await using var db = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly"); await db.OpenAsync();
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT CanonicalTitle,FirstReleaseYear,COALESCE(Developer,''),COALESCE(Description,'') FROM Game WHERE GameId=$id"; cmd.Parameters.AddWithValue("$id", gameId);
            await using var reader = await cmd.ExecuteReaderAsync(); if (!await reader.ReadAsync()) { title.Text = "Game not found"; status.Text = "No game record found."; return; }
            title.Text = reader.GetString(0); meta.Text = $"{(reader.IsDBNull(1) ? "Year unknown" : reader.GetInt32(1).ToString())}  •  {(string.IsNullOrWhiteSpace(reader.GetString(2)) ? "Developer unknown" : reader.GetString(2))}"; description.Text = string.IsNullOrWhiteSpace(reader.GetString(3)) ? "No description available." : reader.GetString(3);
        }

        _inlineReleases.Clear();
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT COALESCE(r.RegionCode,'Unknown'),r.ReleaseYear,COALESCE(r.Publisher,'—'),COALESCE(r.ReleaseType,'Retail'),r.IsPrimary,
COALESCE((SELECT group_concat(d.SerialRaw, ', ') FROM Disc d WHERE d.ReleaseId=r.ReleaseId),'—'),
COALESCE((SELECT group_concat(DISTINCT rl.LanguageCode) FROM ReleaseLanguage rl WHERE rl.ReleaseId=r.ReleaseId),'')
FROM Release r WHERE r.GameId=$id ORDER BY r.IsPrimary DESC,
CASE WHEN r.RegionCode IN ('NorthAmerica','USA','NTSC-U','NTSC-U/C') THEN 0 WHEN r.RegionCode IN ('Europe','PAL','Europe/PAL','Australia') THEN 1 ELSE 2 END,r.ReleaseYear";
            cmd.Parameters.AddWithValue("$id", gameId); await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var rawRegion = reader.GetString(0); var codes = reader.GetString(6).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                _inlineReleases.Add(new InlineReleaseItem(FriendlyRegion(rawRegion), FlagIconFactory.ForRegion(rawRegion), reader.IsDBNull(1) ? "—" : reader.GetInt32(1).ToString(), reader.GetString(5), reader.GetString(2), reader.GetString(3), reader.GetInt32(4) == 1, codes.Select(FlagIconFactory.ForLanguage).Where(x => x is not null).Cast<ImageSource>().ToArray()));
            }
        }

        var primary = _inlineReleases.FirstOrDefault(x => x.IsPrimary) ?? _inlineReleases.FirstOrDefault();
        if (primary is null) { serialText.Text = regionText.Text = publisherText.Text = "—"; status.Text = "No regional releases are linked to this game."; return; }
        serialText.Text = primary.Serial; regionText.Text = primary.Region; publisherText.Text = primary.Publisher;
        flags.Children.Clear(); foreach (var flag in primary.LanguageFlags) flags.Children.Add(new Image { Source = flag, Width = 27, Height = 18, Stretch = Stretch.Fill, Margin = new Thickness(0, 0, 6, 0) }); if (flags.Children.Count == 0) flags.Children.Add(new TextBlock { Text = "—", Foreground = Brushes.SlateGray });
        var firstSerial = primary.Serial.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
        var coverPath = _mediaLibrary.FindCoverForSerial(firstSerial); var image = _mediaLibrary.LoadImage(coverPath); cover.Source = image; cover.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible; placeholder.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
        status.Text = image is null ? $"{_inlineReleases.Count:N0} regional release(s) • no local front cover" : $"{_inlineReleases.Count:N0} regional release(s) • {Path.GetFileName(coverPath)}";
    }

    private void ReturnToCatalogView()
    {
        if (_inlineGameDetailsPage is not null) { CatalogPage.Children.Remove(_inlineGameDetailsPage); _inlineGameDetailsPage = null; }
        foreach (UIElement child in CatalogPage.Children) child.Visibility = Visibility.Visible;
        PageTitle.Text = "Catalog"; PageSubtitle.Text = "Browse and explore PlayStation 2 games.";
    }

    private static Border InlineCard() => new() { Background = Brushes.White, BorderBrush = Brush("#D3E1EE"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(16) };
    private static void AddInlineDetail(Panel panel, string label, string name) { panel.Children.Add(new TextBlock { Text = label, Foreground = Brush("#5C7189"), FontSize = 11, Margin = new Thickness(0, 8, 0, 2) }); panel.Children.Add(new TextBlock { Name = name, Text = "—", Foreground = Brush("#17314D"), FontSize = 12, TextWrapping = TextWrapping.Wrap }); }

    private static T? FindNamed<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root is T own && own.Name == name) return own;

        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject dependencyChild) continue;
            var found = FindNamed<T>(dependencyChild, name);
            if (found is not null) return found;
        }

        try
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var found = FindNamed<T>(child, name);
                if (found is not null) return found;
            }
        }
        catch (InvalidOperationException)
        {
            // Some logical-only objects do not expose visual children. Logical traversal above is sufficient for them.
        }
        return null;
    }

    private static DataTemplate RegionTemplate()
    {
        var template = new DataTemplate(); var panel = new FrameworkElementFactory(typeof(StackPanel)); panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal); panel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var image = new FrameworkElementFactory(typeof(Image)); image.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding("RegionFlag")); image.SetValue(FrameworkElement.WidthProperty, 24d); image.SetValue(FrameworkElement.HeightProperty, 16d); image.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0)); panel.AppendChild(image);
        var text = new FrameworkElementFactory(typeof(TextBlock)); text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Region")); text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); panel.AppendChild(text); template.VisualTree = panel; return template;
    }

    private static DataTemplate LanguageTemplate()
    {
        var template = new DataTemplate(); var items = new FrameworkElementFactory(typeof(ItemsControl)); items.SetBinding(ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding("LanguageFlags"));
        var panelTemplate = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(StackPanel))); panelTemplate.VisualTree.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal); items.SetValue(ItemsControl.ItemsPanelProperty, panelTemplate);
        var itemTemplate = new DataTemplate(); var image = new FrameworkElementFactory(typeof(Image)); image.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(".")); image.SetValue(FrameworkElement.WidthProperty, 22d); image.SetValue(FrameworkElement.HeightProperty, 15d); image.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 5, 0)); itemTemplate.VisualTree = image; items.SetValue(ItemsControl.ItemTemplateProperty, itemTemplate); template.VisualTree = items; return template;
    }

    private sealed record InlineReleaseItem(string Region, ImageSource? RegionFlag, string Year, string Serial, string Publisher, string ReleaseType, bool IsPrimary, IReadOnlyList<ImageSource> LanguageFlags);
}
