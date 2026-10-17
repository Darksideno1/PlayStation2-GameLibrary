using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using PlayStation2GameLibrary;

Exception? threadError = null;
var staThread = new System.Threading.Thread(() =>
{
    try
    {
        var temp = Path.Combine(Path.GetTempPath(), "PS2-GUI-Smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        Environment.SetEnvironmentVariable("PS2_LIBRARY_DATA_DIR", temp);
        Exception? error = null;
        var completed = false;
        var app = new PlayStation2GameLibrary.App { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var window = new MainWindow();
        var deadline = DateTime.UtcNow.AddSeconds(90);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) =>
        {
            if (completed) return;
            try
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Catalogue UI did not load the full catalogue within 90 seconds.");
                var grid = (DataGrid)window.FindName("GamesGrid");
                if (grid.Items.Count < 3000) return;
                timer.Stop();
                window.UpdateLayout();
                CaptureWindow(window, "catalogue-screenshot.png");

                grid.SelectedIndex = 0;
                var selected = grid.SelectedItem;
                if (selected is null) throw new Exception("Cannot select catalogue game.");

                var detailsButton = FindVisualChildren<Button>(window).First(x => string.Equals((string?)x.Content, "Open Game Details", StringComparison.OrdinalIgnoreCase));
                detailsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                var pageTitle = (TextBlock)window.FindName("PageTitle");
                if (!string.Equals(pageTitle.Text, "Game Details", StringComparison.Ordinal))
                    throw new Exception($"Open Game Details did not navigate inside the main window. Current page: {pageTitle.Text}");

                var allButtons = FindVisualChildren<Button>(window).ToList();
                var allText = FindVisualChildren<TextBlock>(window).ToList();
                var backButton = allButtons.FirstOrDefault(x => string.Equals((string?)x.Content, "← Back to Catalog", StringComparison.Ordinal));
                if (backButton is null) throw new Exception("Inline Game Details did not create Back to Catalog navigation.");
                if (!allButtons.Any(x => string.Equals((string?)x.Content, "Overview", StringComparison.Ordinal)))
                    throw new Exception("Approved Game Details Overview tab is missing.");
                if (!allButtons.Any(x => string.Equals((string?)x.Content, "Regions & Releases", StringComparison.Ordinal)))
                    throw new Exception("Approved Game Details Regions & Releases tab is missing.");
                if (!allText.Any(x => string.Equals(x.Text, "Quick Facts", StringComparison.Ordinal)))
                    throw new Exception("Approved Game Details Quick Facts card is missing.");
                if (!allText.Any(x => string.Equals(x.Text, "Physical copies", StringComparison.Ordinal)))
                    throw new Exception("Approved Game Details collection summary is missing.");

                CaptureWindow(window, "game-details-screenshot.png");

                backButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                if (!string.Equals(pageTitle.Text, "Catalog", StringComparison.Ordinal))
                    throw new Exception("Back to Catalog did not restore the catalogue view.");

                var saved = false;
                var editorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                editorTimer.Tick += (_, _) =>
                {
                    try
                    {
                        if (DateTime.UtcNow > deadline) throw new TimeoutException("Complete Add to Collection dialog timed out.");
                        var addDialog = Application.Current.Windows.OfType<AddCollectionEntryWindow>().FirstOrDefault();
                        if (addDialog is null) return;
                        var releaseBox = FindVisualChildren<ComboBox>(addDialog).FirstOrDefault(x => x.Items.Count > 0);
                        if (releaseBox?.SelectedItem is null) return;
                        if (!saved)
                        {
                            var saveButton = FindVisualChildren<Button>(addDialog).First(x => string.Equals((string?)x.Content, "Add to Collection", StringComparison.OrdinalIgnoreCase));
                            saveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            saved = true;
                            return;
                        }
                        if (addDialog.IsVisible) return;
                        editorTimer.Stop();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                        editorTimer.Stop();
                        Application.Current.Windows.OfType<AddCollectionEntryWindow>().FirstOrDefault()?.Close();
                    }
                };
                editorTimer.Start();
                var addButton = FindVisualChildren<Button>(window).First(x => string.Equals(((string?)x.Content)?.Replace("  ", " ").Trim(), "+ Add to Collection", StringComparison.OrdinalIgnoreCase));
                addButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (error is null)
                {
                    using var db = new SqliteConnection($"Data Source={Path.Combine(temp, "catalogue.db")};Mode=ReadOnly");
                    db.Open();
                    using var cmd = db.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM CollectionCopy";
                    if (Convert.ToInt64(cmd.ExecuteScalar()) != 1)
                        throw new Exception("One-step Add to Collection did not persist through UI.");
                    cmd.CommandText = "SELECT COUNT(*) FROM CollectionCopyDetails";
                    if (Convert.ToInt64(cmd.ExecuteScalar()) != 1)
                        throw new Exception("One-step Add to Collection did not save copy details.");
                    completed = true;
                    Console.WriteLine($"PASS: Catalog displays {grid.Items.Count:N0} games, approved Game Details layout opens inline, and one-step Add to Collection persists copy + details.");
                }
                window.Close();
            }
            catch (Exception ex) { error = ex; timer.Stop(); window.Close(); }
        };
        timer.Start();
        app.Run(window);
        if (error is not null) throw error;
        if (!completed) throw new Exception("Windows GUI smoke test did not complete.");
        try { Directory.Delete(temp, true); } catch (IOException) { }
    }
    catch (Exception ex) { threadError = ex; }
});
staThread.SetApartmentState(System.Threading.ApartmentState.STA);
staThread.Start();
staThread.Join();
if (threadError is not null) throw threadError;

static void CaptureWindow(Window window, string fileName)
{
    window.UpdateLayout();
    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
        Math.Max(1, (int)window.ActualWidth), Math.Max(1, (int)window.ActualHeight),
        96, 96, System.Windows.Media.PixelFormats.Pbgra32);
    bitmap.Render(window);
    var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
    png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
    var screenshotPath = Path.Combine(Directory.GetCurrentDirectory(), "tests", "WindowsGuiSmoke", fileName);
    using var stream = File.Create(screenshotPath);
    png.Save(stream);
}

static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
{
    for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
    {
        var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
        if (child is T item) yield return item;
        foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
    }
}
