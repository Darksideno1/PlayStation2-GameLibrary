using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Threading;

namespace PlayStation2GameLibrary;

internal static class AddCollectionEntryEnhancementsBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(AddCollectionEntryWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded), true);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not AddCollectionEntryWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => Apply(window)));
    }

    private static void Apply(AddCollectionEntryWindow window)
    {
        var purchaseBox = PrivateField<TextBox>(window, "_purchaseDate");
        var formatBox = PrivateField<TextBox>(window, "_digitalFormat");
        var release = PrivateField<ComboBox>(window, "_release");
        if (purchaseBox is null || formatBox is null) return;

        ReplacePurchaseDateWithPicker(purchaseBox);
        ReplaceDigitalFormatWithSelector(formatBox);
        if (release is not null) release.ItemTemplate = ReleaseTemplateWithoutPrimary();
    }

    private static void ReplacePurchaseDateWithPicker(TextBox source)
    {
        if (source.Tag as string == "calendar-enhanced" || source.Parent is not Panel parent) return;
        source.Tag = "calendar-enhanced";
        var index = parent.Children.IndexOf(source);
        var picker = new DatePicker
        {
            Height = 36,
            SelectedDateFormat = DatePickerFormat.Long,
            Language = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag),
            ToolTip = "Choose the purchase date from the calendar. This field is optional."
        };
        var syncing = false;

        void SyncPickerFromText()
        {
            if (syncing) return;
            syncing = true;
            try
            {
                if (DateTime.TryParse(source.Text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                    || DateTime.TryParse(source.Text, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed))
                    picker.SelectedDate = parsed;
                else if (string.IsNullOrWhiteSpace(source.Text))
                    picker.SelectedDate = null;
            }
            finally { syncing = false; }
        }

        SyncPickerFromText();
        source.TextChanged += (_, _) => SyncPickerFromText();
        picker.SelectedDateChanged += (_, _) =>
        {
            if (syncing) return;
            syncing = true;
            try { source.Text = picker.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty; }
            finally { syncing = false; }
        };

        source.Visibility = Visibility.Collapsed;
        parent.Children.Insert(index + 1, picker);
    }

    private static void ReplaceDigitalFormatWithSelector(TextBox source)
    {
        if (source.Tag as string == "format-enhanced" || source.Parent is not Panel parent) return;
        source.Tag = "format-enhanced";
        var index = parent.Children.IndexOf(source);
        var selector = new ComboBox
        {
            Height = 36,
            IsEditable = true,
            IsTextSearchEnabled = true,
            ItemsSource = new[] { "ISO", "ZSO", "BIN/CUE", "CHD", "MDF/MDS", "NRG", "UL", "Other" },
            ToolTip = "Choose a common digital format or type another value."
        };
        var syncing = false;
        selector.Text = string.IsNullOrWhiteSpace(source.Text) ? "ISO" : source.Text;
        source.Text = selector.Text;

        void SyncSourceFromSelector()
        {
            if (syncing) return;
            syncing = true;
            try { source.Text = selector.Text.Trim(); }
            finally { syncing = false; }
        }

        source.TextChanged += (_, _) =>
        {
            if (syncing) return;
            syncing = true;
            try { selector.Text = string.IsNullOrWhiteSpace(source.Text) ? "ISO" : source.Text; }
            finally { syncing = false; }
        };
        selector.SelectionChanged += (_, _) => SyncSourceFromSelector();
        selector.LostKeyboardFocus += (_, _) => SyncSourceFromSelector();
        selector.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => SyncSourceFromSelector()));

        source.Visibility = Visibility.Collapsed;
        parent.Children.Insert(index + 1, selector);
    }

    private static DataTemplate ReleaseTemplateWithoutPrimary()
    {
        var template = new DataTemplate();
        var row = new FrameworkElementFactory(typeof(StackPanel));
        row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        row.SetValue(FrameworkElement.MarginProperty, new Thickness(3));

        var flag = new FrameworkElementFactory(typeof(Image));
        flag.SetBinding(Image.SourceProperty, new Binding("RegionFlag"));
        flag.SetValue(FrameworkElement.WidthProperty, 25d);
        flag.SetValue(FrameworkElement.HeightProperty, 16d);
        flag.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
        row.AppendChild(flag);

        var text = new FrameworkElementFactory(typeof(TextBlock));
        var multi = new MultiBinding { StringFormat = "{0}  ·  {1}  ·  {2}" };
        multi.Bindings.Add(new Binding("Region"));
        multi.Bindings.Add(new Binding("Serial"));
        multi.Bindings.Add(new Binding("ReleaseType"));
        text.SetBinding(TextBlock.TextProperty, multi);
        text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        row.AppendChild(text);

        template.VisualTree = row;
        return template;
    }

    private static T? PrivateField<T>(object owner, string name) where T : class
        => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner) as T;
}
