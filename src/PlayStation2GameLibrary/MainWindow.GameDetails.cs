using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
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
        PageSubtitle.Text = "One conceptual game, all linked regional releases.";

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
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.Children.Add(toolbar);

        var status = new TextBlock
        {
            Name = "InlineDetailsStatus",
            Foreground = Brush("#70839A"),
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        toolbar.Children.Add(status);

        var back = new Button
        {
            Content = "← Back to Catalog",
            Background = Brushes.White,
            Foreground = Brush("#17314D"),
            BorderBrush = Brush("#BCD2E7"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 8, 14, 8),
            Cursor = Cursors.Hand
        };
        back.Click += (_, _) => ReturnToCatalogView();
        Grid.SetColumn(back, 1);
        toolbar.Children.Add(back);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
        Grid.SetRow(content, 1);
        root.Children.Add(content);

        var mainCard = InlineCard();
        mainCard.Padding = new Thickness(18);
        content.Children.Add(mainCard);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        mainCard.Child = scroll;
        var left = new StackPanel();
        scroll.Content = left;

        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        left.Children.Add(titleRow);

        var title = new TextBlock
        {
            Name = "InlineGameTitle",
            Text = "Game title",
            FontSize = 27,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#10243F"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0)
        };
        titleRow.Children.Add(title);

        var add = new Button
        {
            Name = "InlineAddButton",
            Content = "+  Add to Collection",
            Background = Brush("#20B06B"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#20B06B"),
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(17, 9, 17, 9),
            MinWidth = 170,
            Cursor = Cursors.Hand
        };
        Grid.SetColumn(add, 1);
        titleRow.Children.Add(add);

        var releaseBadge = new Border
        {
            Background = Brush("#EDF6FF"),
            BorderBrush = Brush("#C8DDF2"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        releaseBadge.Child = new TextBlock { Name = "InlineReleaseType", Text = "Retail", FontSize = 10.5, Foreground = Brush("#254E75") };
        left.Children.Add(releaseBadge);

        var collection = new Border
        {
            Background = Brush("#F7FBFF"),
            BorderBrush = Brush("#CFE0EF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 13, 0, 14)
        };
        left.Children.Add(collection);
        var collectionStack = new StackPanel();
        collection.Child = collectionStack;
        collectionStack.Children.Add(new TextBlock
        {
            Name = "InlineCollectionState",
            Text = "Not in Collection",
            Foreground = Brush("#60758D"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var metrics = new Grid();
        metrics.ColumnDefinitions.Add(new ColumnDefinition());
        metrics.ColumnDefinitions.Add(new ColumnDefinition());
        metrics.ColumnDefinitions.Add(new ColumnDefinition());
        collectionStack.Children.Add(metrics);
        var physical = MetricCard("Physical copies", "InlinePhysicalCount");
        var digital = MetricCard("Digital entries", "InlineDigitalCount");
        var primaryMetric = MetricCard("Primary release", "InlineCollectionPrimary");
        physical.Margin = new Thickness(0, 0, 7, 0);
        digital.Margin = new Thickness(0, 0, 7, 0);
        Grid.SetColumn(digital, 1);
        Grid.SetColumn(primaryMetric, 2);
        metrics.Children.Add(physical);
        metrics.Children.Add(digital);
        metrics.Children.Add(primaryMetric);

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        left.Children.Add(tabs);
        var overviewTab = TabButton("Overview", "InlineOverviewTab", true);
        var releasesTab = TabButton("Regions & Releases", "InlineReleasesTab", false);
        releasesTab.Margin = new Thickness(7, 0, 0, 0);
        tabs.Children.Add(overviewTab);
        tabs.Children.Add(releasesTab);

        var tabHost = new Grid();
        left.Children.Add(tabHost);

        var overviewPanel = new StackPanel { Name = "InlineOverviewPanel" };
        tabHost.Children.Add(overviewPanel);

        var primaryValue = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        primaryValue.Children.Add(new TextBlock { Name = "InlinePrimarySerial", Text = "—", FontWeight = FontWeights.SemiBold, Foreground = Brush("#10243F"), VerticalAlignment = VerticalAlignment.Center });
        var regionPill = new Border
        {
            Name = "InlinePrimaryRegionPill",
            Background = Brush("#EDF6FF"),
            BorderBrush = Brush("#C8DDF2"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 3, 7, 3),
            Margin = new Thickness(10, 0, 0, 0)
        };
        var regionPillContent = new StackPanel { Orientation = Orientation.Horizontal };
        regionPillContent.Children.Add(new Image { Name = "InlinePrimaryRegionFlag", Width = 24, Height = 16, Stretch = Stretch.Fill, Margin = new Thickness(0, 0, 6, 0) });
        regionPillContent.Children.Add(new TextBlock { Name = "InlinePrimaryRegion", Text = "—", Foreground = Brush("#254E75"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        regionPill.Child = regionPillContent;
        primaryValue.Children.Add(regionPill);
        AddOverviewRow(overviewPanel, "Primary serial", primaryValue);

        var languageFlags = new WrapPanel { Name = "InlineLanguageFlags", Orientation = Orientation.Horizontal };
        AddOverviewRow(overviewPanel, "Languages", languageFlags);
        AddOverviewRow(overviewPanel, "Release date", new TextBlock { Name = "InlineReleaseDate", Text = "—", Foreground = Brush("#17314D"), TextWrapping = TextWrapping.Wrap });
        AddOverviewRow(overviewPanel, "Developer", new TextBlock { Name = "InlineDeveloper", Text = "—", Foreground = Brush("#17314D"), TextWrapping = TextWrapping.Wrap });
        AddOverviewRow(overviewPanel, "Publisher", new TextBlock { Name = "InlinePublisher", Text = "—", Foreground = Brush("#17314D"), TextWrapping = TextWrapping.Wrap });
        AddOverviewRow(overviewPanel, "Genres", new TextBlock { Name = "InlineGenres", Text = "—", Foreground = Brush("#17314D"), TextWrapping = TextWrapping.Wrap });
        AddOverviewRow(overviewPanel, "Description", new TextBlock { Name = "InlineDescription", Text = "No description available.", Foreground = Brush("#405972"), TextWrapping = TextWrapping.Wrap, MaxWidth = 900 });

        var releasesPanel = new Grid { Name = "InlineReleasesPanel", Visibility = Visibility.Collapsed };
        releasesPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        releasesPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        tabHost.Children.Add(releasesPanel);
        releasesPanel.Children.Add(new TextBlock
        {
            Text = "Each row is a linked regional release of this conceptual game.",
            Foreground = Brush("#60758D"),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 9)
        });
        var releasesGrid = new DataGrid
        {
            Name = "InlineReleasesGrid",
            ItemsSource = _inlineReleases,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            BorderThickness = new Thickness(0),
            RowHeight = 42,
            MinHeight = 260,
            Background = Brushes.White
        };
        releasesGrid.Columns.Add(new DataGridTemplateColumn { Header = "REGION", Width = 115, CellTemplate = RegionTemplate() });
        releasesGrid.Columns.Add(new DataGridTextColumn { Header = "SERIAL", Binding = new System.Windows.Data.Binding("Serial"), Width = 130 });
        releasesGrid.Columns.Add(new DataGridTextColumn { Header = "RELEASE", Binding = new System.Windows.Data.Binding("ReleaseType"), Width = 105 });
        releasesGrid.Columns.Add(new DataGridTextColumn { Header = "YEAR", Binding = new System.Windows.Data.Binding("Year"), Width = 70 });
        releasesGrid.Columns.Add(new DataGridTextColumn { Header = "PUBLISHER", Binding = new System.Windows.Data.Binding("Publisher"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        releasesGrid.Columns.Add(new DataGridTemplateColumn { Header = "LANGUAGES", Width = 175, CellTemplate = LanguageTemplate() });
        Grid.SetRow(releasesGrid, 1);
        releasesPanel.Children.Add(releasesGrid);

        overviewTab.Click += (_, _) => SetInlineDetailsTab(page: root, showOverview: true);
        releasesTab.Click += (_, _) => SetInlineDetailsTab(page: root, showOverview: false);

        var right = new StackPanel { Margin = new Thickness(18, 0, 0, 0) };
        Grid.SetColumn(right, 1);
        content.Children.Add(right);

        var coverFrame = new Border
        {
            Width = 285,
            Height = 390,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = Brush("#F7F9FC"),
            BorderBrush = Brush("#D3E1EE"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(7),
            Effect = new DropShadowEffect { BlurRadius = 15, ShadowDepth = 3, Opacity = 0.18, Color = Colors.Black }
        };
        right.Children.Add(coverFrame);
        var coverGrid = new Grid();
        coverFrame.Child = coverGrid;
        var cover = new Image { Name = "InlineCoverImage", Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed, Cursor = Cursors.Hand };
        coverGrid.Children.Add(cover);
        var placeholder = new StackPanel { Name = "InlineNoCover", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        placeholder.Children.Add(new TextBlock { Text = "▧", FontSize = 42, Foreground = Brush("#9FB0C4"), HorizontalAlignment = HorizontalAlignment.Center });
        placeholder.Children.Add(new TextBlock { Text = "No cover available", FontWeight = FontWeights.SemiBold, Foreground = Brush("#60758D"), Margin = new Thickness(0, 8, 0, 0) });
        coverGrid.Children.Add(placeholder);

        var quickFacts = InlineCard();
        quickFacts.Margin = new Thickness(0, 16, 0, 0);
        quickFacts.Padding = new Thickness(14);
        right.Children.Add(quickFacts);
        var quick = new StackPanel();
        quickFacts.Child = quick;
        quick.Children.Add(new TextBlock { Text = "Quick Facts", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brush("#10243F"), Margin = new Thickness(0, 0, 0, 10) });
        AddQuickFactRow(quick, "Primary serial", new TextBlock { Name = "InlineQuickSerial", Text = "—", Foreground = Brush("#17314D") });
        AddQuickFactRow(quick, "Regions", new WrapPanel { Name = "InlineQuickRegions", Orientation = Orientation.Horizontal });
        AddQuickFactRow(quick, "Languages", new WrapPanel { Name = "InlineQuickLanguages", Orientation = Orientation.Horizontal });
        AddQuickFactRow(quick, "Cover", new TextBlock { Text = "One shared front cover", Foreground = Brush("#17314D") });

        return root;
    }

    private async Task LoadInlineGameDetailsAsync(string gameId, Grid page)
    {
        var title = Required<TextBlock>(page, "InlineGameTitle");
        var description = Required<TextBlock>(page, "InlineDescription");
        var serialText = Required<TextBlock>(page, "InlinePrimarySerial");
        var regionText = Required<TextBlock>(page, "InlinePrimaryRegion");
        var regionFlag = Required<Image>(page, "InlinePrimaryRegionFlag");
        var publisherText = Required<TextBlock>(page, "InlinePublisher");
        var developerText = Required<TextBlock>(page, "InlineDeveloper");
        var releaseDateText = Required<TextBlock>(page, "InlineReleaseDate");
        var genresText = Required<TextBlock>(page, "InlineGenres");
        var releaseTypeText = Required<TextBlock>(page, "InlineReleaseType");
        var flags = Required<WrapPanel>(page, "InlineLanguageFlags");
        var quickSerial = Required<TextBlock>(page, "InlineQuickSerial");
        var quickRegions = Required<WrapPanel>(page, "InlineQuickRegions");
        var quickLanguages = Required<WrapPanel>(page, "InlineQuickLanguages");
        var physicalCountText = Required<TextBlock>(page, "InlinePhysicalCount");
        var digitalCountText = Required<TextBlock>(page, "InlineDigitalCount");
        var collectionPrimary = Required<TextBlock>(page, "InlineCollectionPrimary");
        var collectionState = Required<TextBlock>(page, "InlineCollectionState");
        var cover = Required<Image>(page, "InlineCoverImage");
        var placeholder = Required<StackPanel>(page, "InlineNoCover");
        var status = Required<TextBlock>(page, "InlineDetailsStatus");
        var add = Required<Button>(page, "InlineAddButton");

        add.Click += async (_, _) =>
        {
            new CollectionWindow(_databasePath, gameId) { Owner = this }.ShowDialog();
            await RefreshGamesAsync();
            await RefreshFooterAsync();
            await RefreshCollectionPageAsync();
            await LoadInlineGameDetailsAsync(gameId, page);
        };

        await using var db = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly");
        await db.OpenAsync();

        string developer;
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT CanonicalTitle,COALESCE(Developer,''),COALESCE(Description,'') FROM Game WHERE GameId=$id";
            cmd.Parameters.AddWithValue("$id", gameId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                title.Text = "Game not found";
                status.Text = "No game record found.";
                return;
            }
            title.Text = reader.GetString(0);
            developer = reader.GetString(1);
            developerText.Text = string.IsNullOrWhiteSpace(developer) ? "—" : developer;
            var descriptionValue = reader.GetString(2);
            description.Text = string.IsNullOrWhiteSpace(descriptionValue) ? "No description available." : descriptionValue;
        }

        _inlineReleases.Clear();
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT COALESCE(r.ReleaseTitle,''),COALESCE(r.RegionCode,'Unknown'),r.ReleaseYear,COALESCE(r.Publisher,'—'),COALESCE(r.ReleaseType,'Retail'),r.IsPrimary,
COALESCE((SELECT group_concat(d.SerialRaw, ', ') FROM Disc d WHERE d.ReleaseId=r.ReleaseId),'—'),
COALESCE((SELECT group_concat(DISTINCT rl.LanguageCode) FROM ReleaseLanguage rl WHERE rl.ReleaseId=r.ReleaseId),'')
FROM Release r WHERE r.GameId=$id ORDER BY r.IsPrimary DESC,
CASE WHEN r.RegionCode IN ('NorthAmerica','USA','NTSC-U','NTSC-U/C') THEN 0 WHEN r.RegionCode IN ('Europe','PAL','Europe/PAL','Australia') THEN 1 ELSE 2 END,r.ReleaseYear,r.ReleaseTitle";
            cmd.Parameters.AddWithValue("$id", gameId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var rawRegion = reader.GetString(1);
                var codes = reader.GetString(7).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var languageItems = codes
                    .Select(code => new InlineLanguageFlagItem(FriendlyLanguageName(code), FlagIconFactory.ForLanguage(code), code))
                    .ToArray();
                _inlineReleases.Add(new InlineReleaseItem(
                    reader.GetString(0), FriendlyRegion(rawRegion), FlagIconFactory.ForRegion(rawRegion), rawRegion,
                    reader.IsDBNull(2) ? "—" : reader.GetInt32(2).ToString(), reader.GetString(6), reader.GetString(3), reader.GetString(4),
                    reader.GetInt32(5) == 1, languageItems));
            }
        }

        var primary = _inlineReleases.FirstOrDefault(x => x.IsPrimary) ?? _inlineReleases.FirstOrDefault();
        if (primary is null)
        {
            serialText.Text = regionText.Text = publisherText.Text = quickSerial.Text = "—";
            status.Text = "No regional releases are linked to this game.";
            return;
        }

        serialText.Text = primary.Serial;
        regionText.Text = primary.Region;
        regionFlag.Source = primary.RegionFlag;
        regionFlag.ToolTip = primary.Region;
        publisherText.Text = primary.Publisher;
        releaseDateText.Text = primary.Year;
        releaseTypeText.Text = primary.ReleaseType;
        genresText.Text = "—";
        quickSerial.Text = primary.Serial;
        collectionPrimary.Text = primary.Serial;

        PopulateLanguagePanel(flags, primary.Languages);
        PopulateQuickRegions(quickRegions, _inlineReleases);
        PopulateLanguagePanel(quickLanguages, _inlineReleases.SelectMany(x => x.Languages).GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(x => x.First()));

        long physicalCount;
        long digitalCount;
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT
COALESCE(SUM(CASE WHEN c.CopyType='Physical' THEN 1 ELSE 0 END),0),
COALESCE(SUM(CASE WHEN c.CopyType='Digital' THEN 1 ELSE 0 END),0)
FROM CollectionCopy c
WHERE c.GameId=$id OR c.ReleaseId IN (SELECT r.ReleaseId FROM Release r WHERE r.GameId=$id)";
            cmd.Parameters.AddWithValue("$id", gameId);
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            physicalCount = reader.GetInt64(0);
            digitalCount = reader.GetInt64(1);
        }
        physicalCountText.Text = physicalCount.ToString();
        digitalCountText.Text = digitalCount.ToString();
        var inCollection = physicalCount + digitalCount > 0;
        collectionState.Text = inCollection ? "✓ In Collection" : "Not in Collection";
        collectionState.Foreground = inCollection ? Brush("#0B8B4B") : Brush("#60758D");

        var firstSerial = primary.Serial.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
        var coverPath = _mediaLibrary.FindCoverForSerial(firstSerial);
        var image = _mediaLibrary.LoadImage(coverPath);
        cover.Source = image;
        cover.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible;
        placeholder.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
        cover.ToolTip = image is null ? null : "Open front cover";
        if (image is not null)
        {
            cover.MouseLeftButtonUp += (_, _) =>
            {
                var preview = new Window
                {
                    Owner = this,
                    Title = $"{title.Text} — Front Cover",
                    Width = 620,
                    Height = 820,
                    MinWidth = 420,
                    MinHeight = 560,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = Brush("#EEF5FB"),
                    Content = new Border
                    {
                        Margin = new Thickness(18),
                        Background = Brushes.White,
                        BorderBrush = Brush("#D3E1EE"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(12),
                        Child = new Image { Source = image, Stretch = Stretch.Uniform }
                    }
                };
                preview.ShowDialog();
            };
        }

        status.Text = image is null
            ? $"{_inlineReleases.Count:N0} linked release(s) • no local front cover"
            : $"{_inlineReleases.Count:N0} linked release(s) • {Path.GetFileName(coverPath)}";
    }

    private void ReturnToCatalogView()
    {
        if (_inlineGameDetailsPage is not null)
        {
            CatalogPage.Children.Remove(_inlineGameDetailsPage);
            _inlineGameDetailsPage = null;
        }
        foreach (UIElement child in CatalogPage.Children) child.Visibility = Visibility.Visible;
        PageTitle.Text = "Catalog";
        PageSubtitle.Text = "Browse and explore PlayStation 2 games.";
    }

    private static Border InlineCard() => new()
    {
        Background = Brushes.White,
        BorderBrush = Brush("#D3E1EE"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(16),
        Padding = new Thickness(16)
    };

    private static Border MetricCard(string label, string valueName)
    {
        var border = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#D3E1EE"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 9, 10, 9)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, Foreground = Brush("#60758D"), FontSize = 10.5 });
        stack.Children.Add(new TextBlock { Name = valueName, Text = "0", Foreground = Brush("#10243F"), FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        border.Child = stack;
        return border;
    }

    private static Button TabButton(string text, string name, bool selected)
    {
        return new Button
        {
            Name = name,
            Content = text,
            Background = selected ? Brush("#E7F2FD") : Brushes.White,
            Foreground = Brush("#17314D"),
            BorderBrush = selected ? Brush("#2F82E5") : Brush("#C7D8E8"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 7, 12, 7),
            FontSize = 12,
            Cursor = Cursors.Hand
        };
    }

    private static void AddOverviewRow(Panel panel, string label, FrameworkElement value)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = label, Foreground = Brush("#496580"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 10, 0) });
        Grid.SetColumn(value, 1);
        row.Children.Add(value);
        panel.Children.Add(row);
    }

    private static void AddQuickFactRow(Panel panel, string label, FrameworkElement value)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = label, Foreground = Brush("#496580"), FontSize = 11, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 8, 0) });
        Grid.SetColumn(value, 1);
        row.Children.Add(value);
        panel.Children.Add(row);
    }

    private static void SetInlineDetailsTab(DependencyObject page, bool showOverview)
    {
        var overview = FindNamed<StackPanel>(page, "InlineOverviewPanel");
        var releases = FindNamed<Grid>(page, "InlineReleasesPanel");
        var overviewTab = FindNamed<Button>(page, "InlineOverviewTab");
        var releasesTab = FindNamed<Button>(page, "InlineReleasesTab");
        if (overview is null || releases is null || overviewTab is null || releasesTab is null) return;
        overview.Visibility = showOverview ? Visibility.Visible : Visibility.Collapsed;
        releases.Visibility = showOverview ? Visibility.Collapsed : Visibility.Visible;
        overviewTab.Background = showOverview ? Brush("#E7F2FD") : Brushes.White;
        overviewTab.BorderBrush = showOverview ? Brush("#2F82E5") : Brush("#C7D8E8");
        releasesTab.Background = showOverview ? Brushes.White : Brush("#E7F2FD");
        releasesTab.BorderBrush = showOverview ? Brush("#C7D8E8") : Brush("#2F82E5");
    }

    private static void PopulateLanguagePanel(Panel panel, IEnumerable<InlineLanguageFlagItem> languages)
    {
        panel.Children.Clear();
        var any = false;
        foreach (var language in languages)
        {
            any = true;
            panel.Children.Add(CreateLanguageVisual(language));
        }
        if (!any) panel.Children.Add(new TextBlock { Text = "—", Foreground = Brush("#60758D") });
    }

    private static FrameworkElement CreateLanguageVisual(InlineLanguageFlagItem language)
    {
        if (language.Icon is not null)
        {
            return new Image
            {
                Source = language.Icon,
                Width = 25,
                Height = 17,
                Stretch = Stretch.Fill,
                Margin = new Thickness(0, 0, 6, 2),
                ToolTip = language.Name
            };
        }
        return new Border
        {
            Background = Brush("#EDF2F7"),
            BorderBrush = Brush("#D4DEE8"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 2, 5, 2),
            Margin = new Thickness(0, 0, 6, 2),
            ToolTip = language.Name,
            Child = new TextBlock { Text = language.Code.ToUpperInvariant(), FontSize = 9, Foreground = Brush("#496580") }
        };
    }

    private static void PopulateQuickRegions(Panel panel, IEnumerable<InlineReleaseItem> releases)
    {
        panel.Children.Clear();
        foreach (var release in releases.GroupBy(x => x.Region, StringComparer.OrdinalIgnoreCase).Select(x => x.First()))
        {
            if (release.RegionFlag is null) continue;
            panel.Children.Add(new Image
            {
                Source = release.RegionFlag,
                Width = 25,
                Height = 17,
                Stretch = Stretch.Fill,
                Margin = new Thickness(0, 0, 7, 2),
                ToolTip = release.Region
            });
        }
        if (panel.Children.Count == 0) panel.Children.Add(new TextBlock { Text = "—", Foreground = Brush("#60758D") });
    }

    private static string FriendlyLanguageName(string code)
    {
        return code.Trim().ToLowerInvariant() switch
        {
            "en" or "eng" or "english" => "English",
            "fr" or "fra" or "fre" or "french" => "French",
            "de" or "deu" or "ger" or "german" => "German",
            "it" or "ita" or "italian" => "Italian",
            "es" or "spa" or "spanish" => "Spanish",
            "ja" or "jpn" or "japanese" => "Japanese",
            "nl" or "nld" or "dut" or "dutch" => "Dutch",
            "pt" or "por" or "portuguese" => "Portuguese",
            "sv" or "swe" or "swedish" => "Swedish",
            "fi" or "fin" or "finnish" => "Finnish",
            "da" or "dan" or "danish" or "danks" => "Danish",
            "no" or "nor" or "norwegian" or "norweigan" => "Norwegian",
            "pl" or "pol" or "polish" => "Polish",
            "ru" or "rus" or "russian" => "Russian",
            "el" or "ell" or "gre" or "greek" => "Greek",
            "ko" or "kor" or "korean" => "Korean",
            "zh" or "zho" or "chi" or "chinese" => "Chinese",
            "cs" or "ces" or "cze" or "czech" => "Czech",
            "hu" or "hun" or "hungarian" => "Hungarian",
            "tr" or "tur" or "turkish" => "Turkish",
            "ar" or "ara" or "arabic" => "Arabic",
            "hi" or "hin" or "hindi" => "Hindi",
            "ga" or "gle" or "gaelic" => "Gaelic",
            _ => string.IsNullOrWhiteSpace(code) ? "Unknown" : code.Trim()
        };
    }

    private static T Required<T>(DependencyObject root, string name) where T : FrameworkElement =>
        FindNamed<T>(root, name) ?? throw new InvalidOperationException($"Game Details control '{name}' was not created.");

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
        }
        return null;
    }

    private static DataTemplate RegionTemplate()
    {
        var template = new DataTemplate();
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        panel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var image = new FrameworkElementFactory(typeof(Image));
        image.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding("RegionFlag"));
        image.SetBinding(ToolTipService.ToolTipProperty, new System.Windows.Data.Binding("Region"));
        image.SetValue(FrameworkElement.WidthProperty, 24d);
        image.SetValue(FrameworkElement.HeightProperty, 16d);
        image.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
        panel.AppendChild(image);
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Region"));
        text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        panel.AppendChild(text);
        template.VisualTree = panel;
        return template;
    }

    private static DataTemplate LanguageTemplate()
    {
        var template = new DataTemplate();
        var items = new FrameworkElementFactory(typeof(ItemsControl));
        items.SetBinding(ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding("Languages"));
        var panelTemplate = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(StackPanel)));
        panelTemplate.VisualTree.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        items.SetValue(ItemsControl.ItemsPanelProperty, panelTemplate);
        var itemTemplate = new DataTemplate();
        var image = new FrameworkElementFactory(typeof(Image));
        image.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding("Icon"));
        image.SetBinding(ToolTipService.ToolTipProperty, new System.Windows.Data.Binding("Name"));
        image.SetValue(FrameworkElement.WidthProperty, 22d);
        image.SetValue(FrameworkElement.HeightProperty, 15d);
        image.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 5, 0));
        itemTemplate.VisualTree = image;
        items.SetValue(ItemsControl.ItemTemplateProperty, itemTemplate);
        template.VisualTree = items;
        return template;
    }

    private sealed record InlineLanguageFlagItem(string Name, ImageSource? Icon, string Code);
    private sealed record InlineReleaseItem(
        string Title,
        string Region,
        ImageSource? RegionFlag,
        string RawRegion,
        string Year,
        string Serial,
        string Publisher,
        string ReleaseType,
        bool IsPrimary,
        IReadOnlyList<InlineLanguageFlagItem> Languages);
}
