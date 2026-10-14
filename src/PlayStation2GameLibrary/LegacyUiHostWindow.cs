using System;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;

namespace PlayStation2GameLibrary;

/// <summary>
/// Host for the original archived WebView2 UI. Deliberately isolated from the
/// existing MainWindow until the original frontend and data bridge are installed
/// and the catalogue-to-collection regression flow passes on Windows.
/// </summary>
internal sealed class LegacyUiHostWindow : Window
{
    private readonly WebView2 _browser = new();

    internal LegacyUiHostWindow(string originalFrontendDirectory)
    {
        Title = "PlayStation 2 Game Library";
        Width = 1600;
        Height = 900;
        MinWidth = 1180;
        MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#F3F6FB")!;
        Content = _browser;
        Loaded += async (_, _) =>
        {
            var index = Path.GetFullPath(Path.Combine(originalFrontendDirectory, "index.html"));
            if (!File.Exists(index))
            {
                MessageBox.Show(this, "Original WebView2 frontend is not yet bundled. The current catalogue remains available in the main window.",
                    "Original UI not installed", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
                return;
            }
            try
            {
                await _browser.EnsureCoreWebView2Async();
                _browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _browser.CoreWebView2.Settings.IsWebMessageEnabled = false; // Enable only with validated RPC bridge.
                _browser.CoreWebView2.Navigate(new Uri(index).AbsoluteUri);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "WebView2 initialization failed", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        };
    }
}
