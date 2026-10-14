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
        // The smoke harness supplies MainWindow explicitly; no StartupUri is needed.
        var window = new MainWindow();
        var deadline = DateTime.UtcNow.AddSeconds(90);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) =>
        {
            if (completed) return;
            try
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Catalogue UI did not load within 90 seconds.");
                var grid = (DataGrid)window.FindName("GamesGrid");
                if (grid.Items.Count < 100) return;
                timer.Stop();
                window.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    Math.Max(1, (int)window.ActualWidth), Math.Max(1, (int)window.ActualHeight),
                    96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(window);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                var screenshotPath = Path.Combine(Directory.GetCurrentDirectory(), "tests", "WindowsGuiSmoke", "catalogue-screenshot.png");
                using (var stream = File.Create(screenshotPath)) png.Save(stream);
                grid.SelectedIndex = 0;
                var selected = grid.SelectedItem;
                if (selected is null) throw new Exception("Cannot select catalogue game.");
                // Use the real catalogue action, not a directly constructed editor.
                var saved = false;
                var editorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                editorTimer.Tick += (_, _) =>
                {
                    try
                    {
                        if (DateTime.UtcNow > deadline) throw new TimeoutException("Collection editor timed out.");
                        var copyDialog = Application.Current.Windows.OfType<CollectionWindow>().FirstOrDefault();
                        if (copyDialog is null) return;
                        var gameBox = FindVisualChildren<ComboBox>(copyDialog).FirstOrDefault(x => x.SelectedValuePath == "Id");
                        if (gameBox?.SelectedValue is not string) return;
                        if (!saved)
                        {
                            var saveButton = FindVisualChildren<Button>(copyDialog).First(x => (string?)x.Content == "Add / Save");
                            saveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            saved = true;
                            return;
                        }
                        var copyGrid = FindVisualChildren<DataGrid>(copyDialog).FirstOrDefault();
                        if (copyGrid?.Items.Count < 1) return;
                        editorTimer.Stop();
                        copyDialog.Close();
                    }
                    catch (Exception ex) { error = ex; editorTimer.Stop(); Application.Current.Windows.OfType<CollectionWindow>().FirstOrDefault()?.Close(); }
                };
                editorTimer.Start();
                var addButton = FindVisualChildren<Button>(window).First(x => (string?)x.Content == "+ ADD TO COLLECTION");
                addButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (error is null)
                {
                    using var db = new SqliteConnection($"Data Source={Path.Combine(temp, "catalogue.db")};Mode=ReadOnly");
                    db.Open();
                    using var cmd = db.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM CollectionCopy";
                    if (Convert.ToInt64(cmd.ExecuteScalar()) != 1)
                        throw new Exception("Add to Collection did not persist through UI.");
                    completed = true;
                    Console.WriteLine("PASS: WPF catalogue button opens editor, adds game, My Collection grid shows entry, SQLite record persists.");
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

static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
{
    for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
    {
        var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
        if (child is T item) yield return item;
        foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
    }
}
