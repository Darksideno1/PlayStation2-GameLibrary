using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Data.Sqlite;

namespace PlayStation2GameLibrary;

/// <summary>
/// Complete one-step Add to Collection dialog. All metadata required for a new
/// Physical or Digital copy is entered before the copy is created.
/// </summary>
public sealed class AddCollectionEntryWindow : Window
{
    private readonly string _dbPath;
    private readonly string _gameId;
    private readonly ComboBox _release = new() { MinWidth = 430, Height = 38 };
    private readonly RadioButton _physical = new() { Content = "Physical", IsChecked = true, GroupName = "CopyType", Margin = new Thickness(0, 0, 16, 0) };
    private readonly RadioButton _digital = new() { Content = "Digital", GroupName = "CopyType" };
    private readonly ComboBox _condition = new() { ItemsSource = new[] { "Mint", "Very Good", "Good", "Acceptable", "Poor" }, SelectedIndex = 1, Height = 36 };
    private readonly CheckBox _case = new() { IsChecked = true };
    private readonly CheckBox _cover = new() { IsChecked = true };
    private readonly CheckBox _disc = new() { IsChecked = true };
    private readonly CheckBox _manual = new() { IsChecked = true };
    private readonly TextBox _digitalSize = new() { Height = 36 };
    private readonly TextBox _storage = new() { Height = 36 };
    private readonly TextBox _digitalFormat = new() { Height = 36, Text = "ISO" };
    private readonly TextBox _purchaseDate = new() { Height = 36, ToolTip = "Optional. Example: 2026-10-17" };
    private readonly TextBox _notes = new() { MinHeight = 78, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly StackPanel _physicalPanel = new();
    private readonly StackPanel _digitalPanel = new();
    private readonly TextBlock _status = new() { Foreground = Brush("#60758D"), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _title = new() { FontSize = 25, FontWeight = FontWeights.Bold, Foreground = Brush("#10243F"), TextWrapping = TextWrapping.Wrap };

    public AddCollectionEntryWindow(string dbPath, string gameId)
    {
        _dbPath = dbPath;
        _gameId = gameId;
        Title = "Add to Collection — PLAYSTATION 2 GAME LIBRARY";
        Width = 760;
        Height = 790;
        MinWidth = 650;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#EEF5FB");
        FontFamily = new FontFamily("Segoe UI");

        Content = BuildUi();
        _physical.Checked += (_, _) => UpdateTypePanels();
        _digital.Checked += (_, _) => UpdateTypePanels();
        Loaded += async (_, _) => await LoadAsync();
        UpdateTypePanels();
    }

    private UIElement BuildUi()
    {
        var root = new DockPanel { Margin = new Thickness(22) };

        var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        footer.Children.Add(_status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        var cancel = Button("Cancel", false);
        cancel.Click += (_, _) => Close();
        actions.Children.Add(cancel);
        var save = Button("Add to Collection", true);
        save.Margin = new Thickness(8, 0, 0, 0);
        save.Click += async (_, _) => await SaveAsync();
        actions.Children.Add(save);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(scroll);
        var content = new StackPanel();
        scroll.Content = content;

        content.Children.Add(new TextBlock { Text = "ADD TO COLLECTION", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brush("#1264BD") });
        _title.Margin = new Thickness(0, 6, 0, 4);
        content.Children.Add(_title);
        content.Children.Add(new TextBlock
        {
            Text = "Create the complete collection entry now. You will only need Edit later if something changes.",
            Foreground = Brush("#60758D"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16)
        });

        content.Children.Add(Card(Section("Release", "Choose the exact regional release / serial for this copy.", _release)));

        var typeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        typeRow.Children.Add(_physical);
        typeRow.Children.Add(_digital);
        content.Children.Add(Card(Section("Ownership type", "Physical and Digital entries store different metadata.", typeRow), new Thickness(0, 12, 0, 0)));

        BuildPhysicalPanel();
        content.Children.Add(_physicalPanel);
        BuildDigitalPanel();
        content.Children.Add(_digitalPanel);

        var common = new StackPanel();
        common.Children.Add(FieldLabel("Purchase date"));
        common.Children.Add(_purchaseDate);
        common.Children.Add(FieldLabel("Notes"));
        common.Children.Add(_notes);
        content.Children.Add(Card(Section("Purchase & notes", "Optional information about this individual copy.", common), new Thickness(0, 12, 0, 0)));
        return root;
    }

    private void BuildPhysicalPanel()
    {
        _physicalPanel.Children.Clear();
        var body = new StackPanel();
        body.Children.Add(FieldLabel("Condition"));
        body.Children.Add(_condition);
        body.Children.Add(new TextBlock { Text = "Completeness", FontWeight = FontWeights.SemiBold, Foreground = Brush("#304B68"), Margin = new Thickness(0, 14, 0, 7) });
        var grid = new UniformGrid { Columns = 2 };
        grid.Children.Add(CompletenessCard(_case, "▣", "Original case", "Original PS2 case"));
        grid.Children.Add(CompletenessCard(_cover, "▤", "Cover", "Printed front cover included"));
        grid.Children.Add(CompletenessCard(_disc, "◉", "Disc", "Game disc included"));
        grid.Children.Add(CompletenessCard(_manual, "▥", "Manual", "Instruction booklet included"));
        body.Children.Add(grid);
        _physicalPanel.Children.Add(Card(Section("Physical copy", "Record what is actually included with this copy.", body), new Thickness(0, 12, 0, 0)));
    }

    private void BuildDigitalPanel()
    {
        _digitalPanel.Children.Clear();
        var body = new StackPanel();
        body.Children.Add(FieldLabel("Digital size (GB)"));
        _digitalSize.ToolTip = "Optional. Example: 4.37";
        body.Children.Add(_digitalSize);
        body.Children.Add(FieldLabel("Storage location"));
        _storage.ToolTip = "Optional. Example: D:\\PS2 Games\\";
        body.Children.Add(_storage);
        body.Children.Add(FieldLabel("File format"));
        body.Children.Add(_digitalFormat);
        _digitalPanel.Children.Add(Card(Section("Digital copy", "Save the size and where this copy is stored.", body), new Thickness(0, 12, 0, 0)));
    }

    private async Task LoadAsync()
    {
        try
        {
            await using var db = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
            await db.OpenAsync();
            await using (var titleCmd = db.CreateCommand())
            {
                titleCmd.CommandText = "SELECT CanonicalTitle FROM Game WHERE GameId=$id";
                titleCmd.Parameters.AddWithValue("$id", _gameId);
                _title.Text = Convert.ToString(await titleCmd.ExecuteScalar()) ?? "Game";
            }

            var choices = new List<ReleaseChoice>();
            await using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"SELECT r.ReleaseId,COALESCE(r.RegionCode,'Unknown'),COALESCE(r.ReleaseType,'Retail'),r.IsPrimary,
COALESCE((SELECT group_concat(d.SerialRaw, ', ') FROM Disc d WHERE d.ReleaseId=r.ReleaseId),'—')
FROM Release r WHERE r.GameId=$id
ORDER BY r.IsPrimary DESC,
CASE WHEN r.RegionCode IN ('NorthAmerica','USA','NTSC-U','NTSC-U/C') THEN 0 WHEN r.RegionCode IN ('Europe','PAL','Europe/PAL','Australia') THEN 1 ELSE 2 END,
r.ReleaseYear";
                cmd.Parameters.AddWithValue("$id", _gameId);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var rawRegion = reader.GetString(1);
                    choices.Add(new ReleaseChoice(
                        reader.GetString(0), FriendlyRegion(rawRegion), FlagIconFactory.ForRegion(rawRegion), reader.GetString(4), reader.GetString(2), reader.GetInt32(3) == 1));
                }
            }
            _release.ItemsSource = choices;
            _release.ItemTemplate = ReleaseTemplate();
            _release.SelectedIndex = choices.Count > 0 ? 0 : -1;
            _status.Text = choices.Count > 0 ? $"{choices.Count} release(s) available." : "No release is available for this game.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Add to Collection", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task SaveAsync()
    {
        if (_release.SelectedItem is not ReleaseChoice release)
        {
            MessageBox.Show(this, "Choose a release before adding the copy.", "Release required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var copyType = _digital.IsChecked == true ? "Digital" : "Physical";
        long? sizeBytes = null;
        if (copyType == "Digital" && !string.IsNullOrWhiteSpace(_digitalSize.Text))
        {
            if (!TryDecimal(_digitalSize.Text, out var gb) || gb < 0)
            {
                MessageBox.Show(this, "Enter a valid digital size in GB.", "Invalid size", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            sizeBytes = (long)Math.Round(gb * 1_000_000_000m);
        }

        var purchaseDate = _purchaseDate.Text.Trim();
        if (purchaseDate.Length > 0)
        {
            if (!DateTime.TryParse(purchaseDate, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed)
                && !DateTime.TryParseExact(purchaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                MessageBox.Show(this, "Enter a valid purchase date (for example 2026-10-17).", "Invalid date", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            purchaseDate = parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        var copyId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow.ToString("O");
        var completeness = copyType == "Physical"
            ? string.Join(", ", new[] { (_case.IsChecked == true, "Case"), (_cover.IsChecked == true, "Cover"), (_disc.IsChecked == true, "Disc"), (_manual.IsChecked == true, "Manual") }.Where(x => x.Item1).Select(x => x.Item2))
            : string.Empty;

        try
        {
            await using var db = new SqliteConnection($"Data Source={_dbPath}");
            await db.OpenAsync();
            await using var tx = await db.BeginTransactionAsync();

            await using (var cmd = db.CreateCommand())
            {
                cmd.Transaction = (SqliteTransaction)tx;
                cmd.CommandText = @"INSERT INTO CollectionCopy
(CopyId,ReleaseId,GameId,CopyType,OwnershipStatus,Condition,Completeness,StorageLocation,DigitalSizeBytes,Notes,CreatedUtc,UpdatedUtc)
VALUES($copy,$release,$game,$type,'Owned',$condition,$completeness,$storage,$size,$notes,$now,$now)";
                cmd.Parameters.AddWithValue("$copy", copyId);
                cmd.Parameters.AddWithValue("$release", release.ReleaseId);
                cmd.Parameters.AddWithValue("$game", _gameId);
                cmd.Parameters.AddWithValue("$type", copyType);
                cmd.Parameters.AddWithValue("$condition", copyType == "Physical" ? _condition.SelectedItem?.ToString() ?? "Very Good" : "");
                cmd.Parameters.AddWithValue("$completeness", completeness);
                cmd.Parameters.AddWithValue("$storage", copyType == "Digital" ? _storage.Text.Trim() : "");
                cmd.Parameters.AddWithValue("$size", sizeBytes is long bytes ? bytes : DBNull.Value);
                cmd.Parameters.AddWithValue("$notes", _notes.Text.Trim());
                cmd.Parameters.AddWithValue("$now", now);
                await cmd.ExecuteNonQueryAsync();
            }

            await using (var cmd = db.CreateCommand())
            {
                cmd.Transaction = (SqliteTransaction)tx;
                cmd.CommandText = @"INSERT INTO CollectionCopyDetails
(CopyId,Edition,CoverPresent,CasePresent,DiscPresent,ManualPresent,CoverCondition,CaseCondition,DiscCondition,ManualCondition,DigitalFormat,OplVersion,OplCompatibility,VmcStatus,PurchaseDate,PurchasePrice,Seller,UpdatedUtc)
VALUES($copy,'Standard',$cover,$case,$disc,$manual,'Not rated','Not rated','Not rated','Not rated',$format,'','Not tested','Not tested',$date,'','',$now)";
                cmd.Parameters.AddWithValue("$copy", copyId);
                cmd.Parameters.AddWithValue("$cover", copyType == "Physical" ? YesNo(_cover) : "Not applicable");
                cmd.Parameters.AddWithValue("$case", copyType == "Physical" ? YesNo(_case) : "Not applicable");
                cmd.Parameters.AddWithValue("$disc", copyType == "Physical" ? YesNo(_disc) : "Not applicable");
                cmd.Parameters.AddWithValue("$manual", copyType == "Physical" ? YesNo(_manual) : "Not applicable");
                cmd.Parameters.AddWithValue("$format", copyType == "Digital" ? _digitalFormat.Text.Trim() : "");
                cmd.Parameters.AddWithValue("$date", purchaseDate);
                cmd.Parameters.AddWithValue("$now", now);
                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Add to Collection failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateTypePanels()
    {
        _physicalPanel.Visibility = _physical.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        _digitalPanel.Visibility = _digital.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string YesNo(CheckBox box) => box.IsChecked == true ? "Yes" : "No";

    private static bool TryDecimal(string text, out decimal value)
        => decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value)
           || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    private static Border CompletenessCard(CheckBox check, string icon, string title, string subtitle)
    {
        var card = new Border { Background = Brush("#F8FBFE"), BorderBrush = Brush("#CFE0EF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(10), Margin = new Thickness(0, 0, 8, 8) };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        card.Child = row;
        check.VerticalAlignment = VerticalAlignment.Top;
        check.Margin = new Thickness(0, 4, 9, 0);
        row.Children.Add(check);
        var iconText = new TextBlock { Text = icon, FontSize = 19, Foreground = Brush("#2F82E5"), VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(iconText, 1);
        row.Children.Add(iconText);
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, Foreground = Brush("#17314D") });
        text.Children.Add(new TextBlock { Text = subtitle, Foreground = Brush("#70849A"), FontSize = 11, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(text, 2);
        row.Children.Add(text);
        return card;
    }

    private static Border Card(UIElement child, Thickness? margin = null)
        => new() { Child = child, Background = Brushes.White, BorderBrush = Brush("#D3E1EE"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(15), Margin = margin ?? new Thickness(0) };

    private static StackPanel Section(string title, string subtitle, UIElement content)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brush("#17314D") });
        panel.Children.Add(new TextBlock { Text = subtitle, Foreground = Brush("#70849A"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 10) });
        panel.Children.Add(content);
        return panel;
    }

    private static TextBlock FieldLabel(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Foreground = Brush("#304B68"), Margin = new Thickness(0, 10, 0, 5) };

    private static Button Button(string text, bool primary)
        => new() { Content = text, Padding = new Thickness(15, 8, 15, 8), Background = primary ? Brush("#1FAF69") : Brushes.White, Foreground = primary ? Brushes.White : Brush("#17314D"), BorderBrush = primary ? Brush("#1FAF69") : Brush("#C8D8E7"), FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal };

    private static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    private static string FriendlyRegion(string? region)
    {
        var value = (region ?? string.Empty).ToLowerInvariant();
        if (value.Contains("north") || value.Contains("usa") || value.Contains("ntsc-u")) return "NTSC-U/C";
        if (value.Contains("japan") || value.Contains("ntsc-j")) return "NTSC-J";
        if (value.Contains("europe") || value.Contains("pal") || value.Contains("australia")) return "PAL";
        return string.IsNullOrWhiteSpace(region) ? "Unknown" : region;
    }

    private static DataTemplate ReleaseTemplate()
    {
        var template = new DataTemplate();
        var row = new FrameworkElementFactory(typeof(StackPanel));
        row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        row.SetValue(FrameworkElement.MarginProperty, new Thickness(3));
        var flag = new FrameworkElementFactory(typeof(Image));
        flag.SetBinding(Image.SourceProperty, new Binding(nameof(ReleaseChoice.RegionFlag)));
        flag.SetValue(FrameworkElement.WidthProperty, 25d);
        flag.SetValue(FrameworkElement.HeightProperty, 16d);
        flag.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
        row.AppendChild(flag);
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(ReleaseChoice.Display)));
        text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        row.AppendChild(text);
        template.VisualTree = row;
        return template;
    }

    private sealed record ReleaseChoice(string ReleaseId, string Region, ImageSource? RegionFlag, string Serial, string ReleaseType, bool IsPrimary)
    {
        public string Display => $"{Region}  ·  {Serial}  ·  {ReleaseType}{(IsPrimary ? "  ·  Primary" : "")}";
    }
}
