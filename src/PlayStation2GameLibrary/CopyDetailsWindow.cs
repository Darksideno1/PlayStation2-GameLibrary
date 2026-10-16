using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Data.Sqlite;

namespace PlayStation2GameLibrary;

/// <summary>Detailed per-copy inventory for Physical and Digital collection entries.</summary>
public sealed class CopyDetailsWindow : Window
{
    private readonly string _dbPath;
    private readonly string _copyId;
    private readonly string _copyType;
    private readonly Dictionary<string, Control> _fields = new();
    private readonly TextBlock _status = new() { Foreground = Brushes.SlateGray };
    private readonly TextBlock _subtitle = new() { Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap };

    private static readonly string[] Presence = { "Unknown", "Yes", "No", "Not applicable" };
    private static readonly string[] Grades = { "Not rated", "Poor", "Fair", "Good", "Very Good", "Excellent", "Sealed" };

    public CopyDetailsWindow(string dbPath, string copyId, string gameTitle, string copyType)
    {
        _dbPath = dbPath;
        _copyId = copyId;
        _copyType = copyType;
        Title = "Copy details — PLAYSTATION 2 GAME LIBRARY";
        Width = 790;
        Height = 720;
        MinWidth = 630;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#EEF5FB");
        FontFamily = new FontFamily("Segoe UI");

        var root = new DockPanel { Margin = new Thickness(22) };
        Content = root;
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        head.Children.Add(new TextBlock { Text = "MY COLLECTION  /  COPY DETAILS", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brush("#1261E8") });
        head.Children.Add(new TextBlock { Text = gameTitle, FontSize = 25, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#17233A"), Margin = new Thickness(0, 6, 0, 5) });
        _subtitle.Text = $"{copyType} copy • Each copy can have its own condition, contents and purchase information.";
        head.Children.Add(_subtitle);

        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        var cancel = new Button { Content = "Close", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(16, 8, 16, 8) };
        cancel.Click += (_, _) => Close();
        bottom.Children.Add(cancel);
        var save = new Button { Content = "Save copy details", Background = Brush("#2F82E5"), Foreground = Brushes.White, FontWeight = FontWeights.Bold, Padding = new Thickness(16, 8, 16, 8) };
        save.Click += async (_, _) => await SaveAsync();
        bottom.Children.Add(save);

        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(scroll);
        var content = new StackPanel();
        scroll.Content = content;

        if (copyType == "Physical")
        {
            Section(content, "COMPLETENESS", "Record what is actually included with this individual physical copy.");
            Text(content, "Edition", "Edition");
            Choice(content, "Cover included", "CoverPresent", Presence);
            Choice(content, "Original case included", "CasePresent", Presence);
            Choice(content, "Disc included", "DiscPresent", Presence);
            Choice(content, "Manual included", "ManualPresent", Presence);
            Section(content, "CONDITION", "Rate each component independently.");
            Choice(content, "Cover condition", "CoverCondition", Grades);
            Choice(content, "Case condition", "CaseCondition", Grades);
            Choice(content, "Disc condition", "DiscCondition", Grades);
            Choice(content, "Manual condition", "ManualCondition", Grades);
        }
        else if (copyType == "Digital")
        {
            Section(content, "DIGITAL COPY", "Digital size is edited in the main collection panel.");
            Text(content, "File format (ISO, ZSO, CHD…)", "DigitalFormat");
        }

        Section(content, "PURCHASE & SOURCE", "Optional collecting information.");
        Text(content, "Purchase date (YYYY-MM-DD)", "PurchaseDate");
        Text(content, "Purchase price (include currency)", "PurchasePrice");
        Text(content, "Seller / source", "Seller");
        Loaded += async (_, _) => await LoadAsync();
    }

    private static Brush Brush(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;

    private static void Section(Panel root, string title, string description)
    {
        var box = new StackPanel { Margin = new Thickness(0, 12, 0, 8) };
        root.Children.Add(box);
        box.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brush("#1261E8") });
        box.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#70809A"), Margin = new Thickness(0, 3, 0, 0) });
    }

    private void Text(Panel root, string label, string key)
    {
        root.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 9, 0, 4) });
        var control = new TextBox { Height = 31, Padding = new Thickness(7, 4, 7, 4) };
        root.Children.Add(control);
        _fields[key] = control;
    }

    private void Choice(Panel root, string label, string key, string[] values)
    {
        root.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 9, 0, 4) });
        var control = new ComboBox { ItemsSource = values, SelectedIndex = 0, Height = 31 };
        root.Children.Add(control);
        _fields[key] = control;
    }

    private string Value(string key) => _fields[key] is ComboBox box ? box.SelectedItem?.ToString() ?? "" : ((TextBox)_fields[key]).Text.Trim();

    private void SetValue(string key, string value)
    {
        if (!_fields.TryGetValue(key, out var control)) return;
        if (control is TextBox text) text.Text = value;
        else if (control is ComboBox combo && combo.Items.Contains(value)) combo.SelectedItem = value;
    }

    private async System.Threading.Tasks.Task LoadAsync()
    {
        try
        {
            await using var db = new SqliteConnection($"Data Source={_dbPath}");
            await db.OpenAsync();
            await using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT * FROM CollectionCopyDetails WHERE CopyId=$id";
            cmd.Parameters.AddWithValue("$id", _copyId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return;
            foreach (var key in _fields.Keys)
            {
                var index = reader.GetOrdinal(key);
                SetValue(key, reader.IsDBNull(index) ? "" : reader.GetString(index));
            }
            _status.Text = "Saved copy details loaded.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Load copy details failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async System.Threading.Tasks.Task SaveAsync()
    {
        var date = _fields.ContainsKey("PurchaseDate") ? Value("PurchaseDate") : "";
        if (date.Length > 0 && !DateTime.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
        {
            MessageBox.Show(this, "Use YYYY-MM-DD for the purchase date.", "Invalid date");
            return;
        }

        try
        {
            await using var db = new SqliteConnection($"Data Source={_dbPath}");
            await db.OpenAsync();
            await using var cmd = db.CreateCommand();
            var columns = new[] { "Edition", "CoverPresent", "CasePresent", "DiscPresent", "ManualPresent",
                "CoverCondition", "CaseCondition", "DiscCondition", "ManualCondition", "DigitalFormat",
                "OplVersion", "OplCompatibility", "VmcStatus", "PurchaseDate", "PurchasePrice", "Seller" };
            var defaults = new Dictionary<string, string> { ["Edition"] = "Standard" };
            foreach (var key in columns)
            {
                var value = _fields.ContainsKey(key) ? Value(key) :
                    key.EndsWith("Present") ? "Unknown" :
                    key.EndsWith("Condition") ? "Not rated" :
                    key is "OplCompatibility" or "VmcStatus" ? "Not tested" :
                    defaults.GetValueOrDefault(key, "");
                cmd.Parameters.AddWithValue("$" + key, value);
            }
            cmd.Parameters.AddWithValue("$id", _copyId);
            cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            cmd.CommandText = @"INSERT INTO CollectionCopyDetails
(CopyId,Edition,CoverPresent,CasePresent,DiscPresent,ManualPresent,CoverCondition,CaseCondition,DiscCondition,ManualCondition,DigitalFormat,OplVersion,OplCompatibility,VmcStatus,PurchaseDate,PurchasePrice,Seller,UpdatedUtc)
VALUES($id,$Edition,$CoverPresent,$CasePresent,$DiscPresent,$ManualPresent,$CoverCondition,$CaseCondition,$DiscCondition,$ManualCondition,$DigitalFormat,$OplVersion,$OplCompatibility,$VmcStatus,$PurchaseDate,$PurchasePrice,$Seller,$now)
ON CONFLICT(CopyId) DO UPDATE SET
Edition=excluded.Edition,CoverPresent=excluded.CoverPresent,CasePresent=excluded.CasePresent,
DiscPresent=excluded.DiscPresent,ManualPresent=excluded.ManualPresent,CoverCondition=excluded.CoverCondition,
CaseCondition=excluded.CaseCondition,DiscCondition=excluded.DiscCondition,ManualCondition=excluded.ManualCondition,
DigitalFormat=excluded.DigitalFormat,OplVersion=excluded.OplVersion,OplCompatibility=excluded.OplCompatibility,
VmcStatus=excluded.VmcStatus,PurchaseDate=excluded.PurchaseDate,PurchasePrice=excluded.PurchasePrice,
Seller=excluded.Seller,UpdatedUtc=excluded.UpdatedUtc";
            if (await cmd.ExecuteNonQueryAsync() < 1) throw new InvalidOperationException("Copy details were not saved.");
            _status.Text = "Copy details saved successfully.";
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save copy details failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
