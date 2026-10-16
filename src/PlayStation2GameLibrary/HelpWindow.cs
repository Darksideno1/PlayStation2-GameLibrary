using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PlayStation2GameLibrary;

internal sealed class HelpWindow : Window
{
    private readonly Dictionary<string, FrameworkElement> _sections = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> _navButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBox _search = new() { Height = 36, Padding = new Thickness(9, 6, 9, 6) };
    private readonly StackPanel _contentHost = new();

    public HelpWindow()
    {
        Title = "Help — PLAYSTATION 2 GAME LIBRARY";
        Width = 1120;
        Height = 760;
        MinWidth = 860;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#EEF5FB");
        FontFamily = new FontFamily("Segoe UI");

        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Content = root;

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        root.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text = "Help", FontSize = 30, FontWeight = FontWeights.Bold, Foreground = Brush("#10243F") });
        heading.Children.Add(new TextBlock { Text = "Quick guidance for using PLAYSTATION 2 GAME LIBRARY.", Foreground = Brush("#60758D"), Margin = new Thickness(0, 5, 0, 0) });

        var searchBorder = new Border { GridRow = 1, Background = Brushes.White, BorderBrush = Brush("#C3D5E6"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 14) };
        Grid.SetRow(searchBorder, 1);
        root.Children.Add(searchBorder);
        _search.BorderThickness = new Thickness(0);
        _search.ToolTip = "Search help topics";
        _search.TextChanged += (_, _) => SearchHelp();
        searchBorder.Child = _search;

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(body, 2);
        root.Children.Add(body);

        var navCard = Card();
        navCard.Padding = new Thickness(10);
        body.Children.Add(navCard);
        var nav = new StackPanel();
        navCard.Child = nav;
        AddNav(nav, "start", "Quick Start");
        AddNav(nav, "catalog", "Catalog");
        AddNav(nav, "collection", "My Collection");
        AddNav(nav, "covers", "Front Covers");
        AddNav(nav, "backup", "Backup & Restore");
        AddNav(nav, "troubleshooting", "Troubleshooting");
        AddNav(nav, "about", "About");

        var contentCard = Card();
        contentCard.Margin = new Thickness(16, 0, 0, 0);
        Grid.SetColumn(contentCard, 1);
        body.Children.Add(contentCard);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        contentCard.Child = scroll;
        scroll.Content = _contentHost;

        AddSection("start", BuildQuickStart());
        AddSection("catalog", BuildCatalog());
        AddSection("collection", BuildCollection());
        AddSection("covers", BuildCovers());
        AddSection("backup", BuildBackup());
        AddSection("troubleshooting", BuildTroubleshooting());
        AddSection("about", BuildAbout());
        SelectSection("start");
    }

    private FrameworkElement BuildQuickStart()
    {
        var panel = Section("Quick Start", "The catalog contains global game information; My Collection stores only the copies you own.");
        AddStep(panel, 1, "Choose the Media Library folder.", "Open Settings and select the root Media folder that contains the Covers subfolder.");
        AddStep(panel, 2, "Browse the Catalog.", "Use Region, Language and Release Type filters to find a game.");
        AddStep(panel, 3, "Add a game to My Collection.", "Choose Physical or Digital and save the copy-specific information.");
        AddStep(panel, 4, "Create a backup.", "Use Import / Export before moving data or restoring a previous database.");
        panel.Children.Add(Tip("The application uses one front cover per conceptual game, even when several regional releases are linked to that game."));
        return panel;
    }

    private FrameworkElement BuildCatalog()
    {
        var panel = Section("Catalog", "The Catalog is the global list of PlayStation 2 games. Selecting a game shows its cover, primary serial and collection status.");
        AddHeading(panel, "Filters");
        AddBullet(panel, "Region — PAL, NTSC-U/C or NTSC-J.");
        AddBullet(panel, "Language — shown with graphical flag images.");
        AddBullet(panel, "Release Type — for example Retail or Platinum.");
        AddHeading(panel, "Regions & Releases");
        AddParagraph(panel, "Game Details lists each regional release on its own row so the region-to-serial mapping stays explicit, for example PAL — SLES-52541 and NTSC-U/C — SLUS-20946.");
        return panel;
    }

    private FrameworkElement BuildCollection()
    {
        var panel = Section("My Collection", "My Collection contains your own Physical and Digital copies. Multiple physical copies of the same game are supported.");
        AddHeading(panel, "Physical copies");
        AddParagraph(panel, "Each physical copy can store condition, purchase date, notes and completeness: Original case, Cover, Disc and Manual.");
        AddHeading(panel, "Digital copies");
        AddParagraph(panel, "Digital entries store their size and optional file format. Dashboard totals are calculated from the saved Digital sizes.");
        panel.Children.Add(Tip("Old OPL rows from earlier test versions are kept in the database for compatibility but are not shown in the current USER workflow."));
        return panel;
    }

    private FrameworkElement BuildCovers()
    {
        var panel = Section("Front Covers", "The media model is intentionally simple: ONE GAME = ONE FRONT COVER.");
        AddHeading(panel, "File lookup");
        AddParagraph(panel, "The program looks in the configured Media Library's Covers folder and uses the primary serial as the filename key.");
        AddBullet(panel, "Supported: JPG, JPEG, PNG and WebP.");
        AddBullet(panel, "Original image files are never resized or modified.");
        AddBullet(panel, "If a cover is missing, a neutral No cover available placeholder is displayed instead of an error.");
        return panel;
    }

    private FrameworkElement BuildBackup()
    {
        var panel = Section("Backup & Restore", "Use Import / Export to protect and move your personal collection data.");
        AddHeading(panel, "Backup");
        AddParagraph(panel, "Create Backup produces a complete SQLite backup of the active library database. Original cover image files remain in the Media Library and are not duplicated into the database backup.");
        AddHeading(panel, "Restore");
        AddParagraph(panel, "Restore checks the selected SQLite file, creates a safety backup of the current database, asks for confirmation and only then replaces the active database.");
        AddHeading(panel, "Collection transfer");
        AddParagraph(panel, "JSON and CSV exports contain Physical and Digital collection entries. Import previews entries first and skips exact duplicates.");
        return panel;
    }

    private FrameworkElement BuildTroubleshooting()
    {
        var panel = Section("Troubleshooting", "Common problems and the first checks to make.");
        AddHeading(panel, "Cover does not appear");
        AddBullet(panel, "Check Media Library Location in Settings.");
        AddBullet(panel, "Verify that the filename matches the game's primary serial.");
        AddBullet(panel, "Make sure the Covers subfolder is reachable, especially when using a NAS path.");
        AddHeading(panel, "NAS is unavailable");
        AddParagraph(panel, "The catalog and personal collection remain usable without cover images. Covers will appear again after the configured network path becomes available.");
        AddHeading(panel, "Wrong regional release");
        AddParagraph(panel, "Open Game Details to identify the correct region and serial, then edit or recreate the personal collection entry if needed.");
        return panel;
    }

    private FrameworkElement BuildAbout()
    {
        var panel = Section("About", "PLAYSTATION 2 GAME LIBRARY v0.1 BETA");
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 12, 0, 0) };
        panel.Children.Add(grid);
        AddAboutCell(grid, "Application", "PLAYSTATION 2 GAME LIBRARY");
        AddAboutCell(grid, "Version", "v0.1 BETA");
        AddAboutCell(grid, "Platform", "Windows / WPF");
        AddAboutCell(grid, "Runtime", ".NET 8");
        panel.Children.Add(Tip("USER and ADMIN are separate builds. ADMIN contains catalogue-maintenance tools that are not intended for normal USER distribution."));
        return panel;
    }

    private void AddNav(Panel nav, string id, string label)
    {
        var button = new Button { Content = label, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 2, 0, 2), BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Brush("#314C68"), FontWeight = FontWeights.SemiBold };
        button.Click += (_, _) => SelectSection(id);
        nav.Children.Add(button);
        _navButtons[id] = button;
    }

    private void AddSection(string id, FrameworkElement content)
    {
        content.Visibility = Visibility.Collapsed;
        _contentHost.Children.Add(content);
        _sections[id] = content;
    }

    private void SelectSection(string id)
    {
        foreach (var pair in _sections) pair.Value.Visibility = pair.Key.Equals(id, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        foreach (var pair in _navButtons)
        {
            var selected = pair.Key.Equals(id, StringComparison.OrdinalIgnoreCase);
            pair.Value.Background = selected ? Brush("#DFEFFF") : Brushes.Transparent;
            pair.Value.Foreground = selected ? Brush("#1264BD") : Brush("#314C68");
        }
    }

    private void SearchHelp()
    {
        var query = _search.Text.Trim();
        if (query.Length == 0) return;
        foreach (var pair in _sections)
        {
            if (ExtractText(pair.Value).Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                SelectSection(pair.Key);
                return;
            }
        }
    }

    private static string ExtractText(DependencyObject root)
    {
        var parts = new List<string>();
        void Walk(DependencyObject node)
        {
            if (node is TextBlock text) parts.Add(text.Text);
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Walk(VisualTreeHelper.GetChild(node, i));
        }
        Walk(root);
        return string.Join(' ', parts);
    }

    private static StackPanel Section(string title, string intro)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brush("#17314D") });
        panel.Children.Add(new TextBlock { Text = intro, Foreground = Brush("#4E657D"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8), LineHeight = 20 });
        return panel;
    }

    private static void AddHeading(Panel panel, string text) => panel.Children.Add(new TextBlock { Text = text, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("#17314D"), Margin = new Thickness(0, 17, 0, 6) });
    private static void AddParagraph(Panel panel, string text) => panel.Children.Add(new TextBlock { Text = text, Foreground = Brush("#4E657D"), TextWrapping = TextWrapping.Wrap, LineHeight = 20, Margin = new Thickness(0, 0, 0, 5) });
    private static void AddBullet(Panel panel, string text) => panel.Children.Add(new TextBlock { Text = "•  " + text, Foreground = Brush("#4E657D"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 3, 0, 3) });

    private static void AddStep(Panel panel, int number, string title, string text)
    {
        var border = new Border { BorderBrush = Brush("#D3E1EE"), BorderThickness = new Thickness(1), Background = Brush("#F9FCFF"), CornerRadius = new CornerRadius(10), Padding = new Thickness(11), Margin = new Thickness(0, 5, 0, 5) };
        panel.Children.Add(border);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        border.Child = grid;
        grid.Children.Add(new Border { Width = 28, Height = 28, Background = Brush("#2E83E5"), CornerRadius = new CornerRadius(14), Child = new TextBlock { Text = number.ToString(), Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        var stack = new StackPanel();
        Grid.SetColumn(stack, 1); grid.Children.Add(stack);
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = Brush("#17314D") });
        stack.Children.Add(new TextBlock { Text = text, Foreground = Brush("#60758D"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 2, 0, 0) });
    }

    private static Border Tip(string text) => new() { Margin = new Thickness(0, 14, 0, 0), Background = Brush("#EAF5FF"), BorderBrush = Brush("#D2E9FB"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Child = new TextBlock { Text = text, Foreground = Brush("#376184"), FontSize = 12, TextWrapping = TextWrapping.Wrap } };

    private static void AddAboutCell(Panel panel, string label, string value)
    {
        var border = new Border { Margin = new Thickness(0, 0, 9, 9), BorderBrush = Brush("#D3E1EE"), BorderThickness = new Thickness(1), Background = Brush("#F9FCFF"), CornerRadius = new CornerRadius(9), Padding = new Thickness(10) };
        panel.Children.Add(border);
        var stack = new StackPanel();
        border.Child = stack;
        stack.Children.Add(new TextBlock { Text = label, Foreground = Brush("#74889C"), FontSize = 10 });
        stack.Children.Add(new TextBlock { Text = value, FontWeight = FontWeights.SemiBold, Foreground = Brush("#17314D"), Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
    }

    private static Border Card() => new() { Background = Brushes.White, BorderBrush = Brush("#D3E1EE"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(18) };
    private static Brush Brush(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;
}
