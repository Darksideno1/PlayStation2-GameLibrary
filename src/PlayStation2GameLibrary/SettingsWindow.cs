using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace PlayStation2GameLibrary;

internal sealed class SettingsWindow : Window
{
    private readonly TextBox _mediaPath = new() { MinWidth = 440 };
    private readonly CheckBox _thumbnailCache = new() { Content = "Enable thumbnail cache (recommended)" };
    private readonly TextBlock _status = new() { Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap };
    private AppSettings _settings;

    public SettingsWindow()
    {
        _settings = AppSettingsService.Load();
        Title = "Settings — PLAYSTATION 2 GAME LIBRARY";
        Width = 780;
        Height = 500;
        MinWidth = 650;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#F3F7FC");
        FontFamily = new FontFamily("Segoe UI");

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        heading.Children.Add(new TextBlock { Text = "Settings", FontSize = 29, FontWeight = FontWeights.Bold, Foreground = Brush("#17233A") });
        heading.Children.Add(new TextBlock { Text = "Configure where local front cover images are stored.", Foreground = Brush("#70809A"), Margin = new Thickness(0, 5, 0, 0) });
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var card = Card();
        Grid.SetRow(card, 1);
        root.Children.Add(card);
        var panel = new StackPanel();
        card.Child = panel;
        panel.Children.Add(new TextBlock { Text = "MEDIA LIBRARY LOCATION", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brush("#1261E8") });
        panel.Children.Add(new TextBlock { Text = "Choose the root Media folder. The program reads front covers from its Covers subfolder.", Foreground = Brush("#60758E"), Margin = new Thickness(0, 5, 0, 12), TextWrapping = TextWrapping.Wrap });

        var pathRow = new Grid();
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _mediaPath.Text = _settings.MediaLibraryLocation;
        _mediaPath.Height = 34;
        _mediaPath.Padding = new Thickness(9, 5, 9, 5);
        pathRow.Children.Add(_mediaPath);
        var browse = new Button { Content = "Browse…", Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += Browse_Click;
        Grid.SetColumn(browse, 1);
        pathRow.Children.Add(browse);
        panel.Children.Add(pathRow);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var test = new Button { Content = "Test Path" };
        test.Click += Test_Click;
        actions.Children.Add(test);
        var open = new Button { Content = "Open Covers Folder", Margin = new Thickness(8, 3, 0, 3) };
        open.Click += Open_Click;
        actions.Children.Add(open);
        panel.Children.Add(actions);

        panel.Children.Add(new TextBlock { Text = @"Examples: D:\PS2 Media\     \\SYNOLOGY\PS2-Media\     .\Media\", Foreground = Brush("#70809A"), FontSize = 11, Margin = new Thickness(0, 8, 0, 0) });
        _thumbnailCache.IsChecked = _settings.ThumbnailCacheEnabled;
        _thumbnailCache.Margin = new Thickness(0, 16, 0, 0);
        panel.Children.Add(_thumbnailCache);
        panel.Children.Add(new TextBlock { Text = "Supported cover formats: JPG, JPEG, PNG and WebP. Original image files are never modified.", Foreground = Brush("#60758E"), FontSize = 11, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
        _status.Margin = new Thickness(0, 12, 0, 0);
        panel.Children.Add(_status);

        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        Grid.SetRow(bottom, 3);
        root.Children.Add(bottom);
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => Close();
        bottom.Children.Add(cancel);
        var save = new Button { Content = "Save Settings", Background = Brush("#1261E8"), Foreground = Brushes.White, FontWeight = FontWeights.SemiBold };
        save.Click += Save_Click;
        bottom.Children.Add(save);
    }

    private static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
    private static Border Card() => new() { Background = Brushes.White, BorderBrush = Brush("#D8E6F6"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(18) };

    private void Browse_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select Media Library root folder" };
        var current = ResolveEnteredRoot();
        if (Directory.Exists(current)) dialog.InitialDirectory = current;
        if (dialog.ShowDialog(this) == true) _mediaPath.Text = dialog.FolderName;
    }

    private string ResolveEnteredRoot()
    {
        var temp = new AppSettings { MediaLibraryLocation = _mediaPath.Text.Trim(), ThumbnailCacheEnabled = _thumbnailCache.IsChecked == true };
        return AppSettingsService.ResolveMediaRoot(temp);
    }

    private void Test_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var root = ResolveEnteredRoot();
            var covers = Path.Combine(root, "Covers");
            _status.Foreground = Directory.Exists(covers) ? Brush("#168454") : Brush("#B66D13");
            _status.Text = Directory.Exists(covers)
                ? $"Covers folder is available: {covers}"
                : $"Media root resolves to {root}. The Covers subfolder does not exist yet.";
        }
        catch (Exception ex)
        {
            _status.Foreground = Brush("#C03943");
            _status.Text = ex.Message;
        }
    }

    private void Open_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var covers = Path.Combine(ResolveEnteredRoot(), "Covers");
            if (!Directory.Exists(covers)) Directory.CreateDirectory(covers);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = covers, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Open Covers folder failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            _settings = new AppSettings
            {
                MediaLibraryLocation = string.IsNullOrWhiteSpace(_mediaPath.Text) ? @".\Media" : _mediaPath.Text.Trim(),
                ThumbnailCacheEnabled = _thumbnailCache.IsChecked == true
            };
            AppSettingsService.Save(_settings);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save settings failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
