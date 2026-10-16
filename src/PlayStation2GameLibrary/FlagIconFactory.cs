using System.Windows;
using System.Windows.Media;

namespace PlayStation2GameLibrary;

internal static class FlagIconFactory
{
    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? ForLanguage(string? languageCode)
    {
        var key = (languageCode ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "en" or "eng" or "english" => "gb",
            "fr" or "fra" or "fre" or "french" => "fr",
            "de" or "deu" or "ger" or "german" => "de",
            "it" or "ita" or "italian" => "it",
            "es" or "spa" or "spanish" => "es",
            "ja" or "jpn" or "japanese" => "jp",
            _ => string.Empty
        };
        return key.Length == 0 ? null : Get(key);
    }

    public static ImageSource ForRegion(string? region)
    {
        var value = (region ?? string.Empty).Trim().ToLowerInvariant();
        var key = value.Contains("japan") || value.Contains("ntsc-j") ? "jp"
            : value.Contains("north") || value.Contains("usa") || value.Contains("ntsc-u") ? "us"
            : "eu";
        return Get(key);
    }

    private static ImageSource Get(string key)
    {
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var group = new DrawingGroup();
        AddRect(group, 0, 0, 36, 24, Colors.White);
        switch (key)
        {
            case "fr":
                AddRect(group, 0, 0, 12, 24, Color.FromRgb(22, 58, 140));
                AddRect(group, 24, 0, 12, 24, Color.FromRgb(237, 41, 57));
                break;
            case "it":
                AddRect(group, 0, 0, 12, 24, Color.FromRgb(22, 155, 98));
                AddRect(group, 24, 0, 12, 24, Color.FromRgb(206, 43, 55));
                break;
            case "de":
                AddRect(group, 0, 0, 36, 8, Colors.Black);
                AddRect(group, 0, 8, 36, 8, Color.FromRgb(221, 0, 0));
                AddRect(group, 0, 16, 36, 8, Color.FromRgb(255, 206, 0));
                break;
            case "es":
                AddRect(group, 0, 0, 36, 6, Color.FromRgb(170, 21, 27));
                AddRect(group, 0, 6, 36, 12, Color.FromRgb(241, 191, 0));
                AddRect(group, 0, 18, 36, 6, Color.FromRgb(170, 21, 27));
                break;
            case "jp":
                AddEllipse(group, 12, 6, 12, 12, Color.FromRgb(216, 31, 58));
                break;
            case "us":
                for (var i = 0; i < 13; i++)
                    if (i % 2 == 0) AddRect(group, 0, i * (24d / 13d), 36, 24d / 13d, Color.FromRgb(201, 31, 55));
                AddRect(group, 0, 0, 15, 13, Color.FromRgb(33, 70, 139));
                break;
            case "gb":
                AddRect(group, 0, 0, 36, 24, Color.FromRgb(33, 70, 139));
                AddRect(group, 15, 0, 6, 24, Colors.White);
                AddRect(group, 0, 9, 36, 6, Colors.White);
                AddRect(group, 16.5, 0, 3, 24, Color.FromRgb(207, 20, 43));
                AddRect(group, 0, 10.5, 36, 3, Color.FromRgb(207, 20, 43));
                break;
            default: // EU/PAL visual
                AddRect(group, 0, 0, 36, 24, Color.FromRgb(23, 71, 166));
                for (var i = 0; i < 12; i++)
                {
                    var angle = (Math.PI * 2 * i / 12) - Math.PI / 2;
                    var x = 18 + Math.Cos(angle) * 7;
                    var y = 12 + Math.Sin(angle) * 7;
                    AddEllipse(group, x - .7, y - .7, 1.4, 1.4, Color.FromRgb(255, 213, 46));
                }
                break;
        }
        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        Cache[key] = image;
        return image;
    }

    private static void AddRect(DrawingGroup group, double x, double y, double width, double height, Color color) =>
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(color), null, new RectangleGeometry(new Rect(x, y, width, height))));

    private static void AddEllipse(DrawingGroup group, double x, double y, double width, double height, Color color) =>
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(color), null, new EllipseGeometry(new Rect(x, y, width, height))));
}
