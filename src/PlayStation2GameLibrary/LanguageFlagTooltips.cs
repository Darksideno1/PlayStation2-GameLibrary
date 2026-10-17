using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PlayStation2GameLibrary;

internal static class LanguageFlagTooltipBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(Image), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnImageLoaded), true);
    }

    private static void OnImageLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Image image || image.Source is null || image.ToolTip is not null) return;
        // Region pills deliberately use 23x15 or 24x16. Do not label those as languages.
        if ((Math.Abs(image.Width - 23) < .1 && Math.Abs(image.Height - 15) < .1) ||
            (Math.Abs(image.Width - 24) < .1 && Math.Abs(image.Height - 16) < .1)) return;

        foreach (var item in Languages)
        {
            var expected = FlagIconFactory.ForLanguage(item.Code);
            if (expected is not null && ReferenceEquals(image.Source, expected))
            {
                image.ToolTip = item.Name;
                ToolTipService.SetShowDuration(image, 10000);
                ToolTipService.SetInitialShowDelay(image, 250);
                return;
            }
        }
    }

    private static readonly (string Code, string Name)[] Languages =
    {
        ("en", "English"), ("fr", "French"), ("de", "German"), ("it", "Italian"),
        ("es", "Spanish"), ("ja", "Japanese"), ("nl", "Dutch"), ("pt", "Portuguese"),
        ("sv", "Swedish"), ("fi", "Finnish"), ("da", "Danish"), ("no", "Norwegian"),
        ("pl", "Polish"), ("ru", "Russian"), ("el", "Greek"), ("ko", "Korean"),
        ("zh", "Chinese"), ("cs", "Czech")
    };
}
