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
            "nl" or "nld" or "dut" or "dutch" => "nl",
            "pt" or "por" or "portuguese" => "pt",
            "sv" or "swe" or "swedish" => "se",
            "fi" or "fin" or "finnish" => "fi",
            "da" or "dan" or "danish" or "danks" => "dk",
            "no" or "nor" or "norwegian" or "norweigan" => "no",
            "pl" or "pol" or "polish" => "pl",
            "ru" or "rus" or "russian" => "ru",
            "el" or "ell" or "gre" or "greek" => "gr",
            "ko" or "kor" or "korean" => "kr",
            "zh" or "zho" or "chi" or "chinese" => "cn",
            "cs" or "ces" or "cze" or "czech" => "cz",
            "hu" or "hun" or "hungarian" => "hu",
            "tr" or "tur" or "turkish" => "tr",
            _ => string.Empty
        };
        return key.Length == 0 ? null : Get(key);
    }

    public static ImageSource ForRegion(string? region)
    {
        var value = (region ?? string.Empty).Trim().ToLowerInvariant();
        var key = value.Contains("korea") || value is "kr" or "k" ? "kr"
            : value.Contains("japan") || value.Contains("ntsc-j") ? "jp"
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
                AddVerticalTricolor(group, Color.FromRgb(22, 58, 140), Colors.White, Color.FromRgb(237, 41, 57));
                break;
            case "it":
                AddVerticalTricolor(group, Color.FromRgb(22, 155, 98), Colors.White, Color.FromRgb(206, 43, 55));
                break;
            case "de":
                AddHorizontalTricolor(group, Colors.Black, Color.FromRgb(221, 0, 0), Color.FromRgb(255, 206, 0));
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
                AddUnionJack(group);
                break;
            case "nl":
                AddHorizontalTricolor(group, Color.FromRgb(174, 28, 40), Colors.White, Color.FromRgb(33, 70, 139));
                break;
            case "pt":
                AddRect(group, 0, 0, 14, 24, Color.FromRgb(4, 106, 56));
                AddRect(group, 14, 0, 22, 24, Color.FromRgb(218, 41, 28));
                AddEllipse(group, 11, 8, 6, 8, Color.FromRgb(255, 205, 0));
                break;
            case "se":
                AddNordicCross(group, Color.FromRgb(35, 95, 155), Color.FromRgb(255, 205, 0));
                break;
            case "fi":
                AddNordicCross(group, Colors.White, Color.FromRgb(0, 53, 128));
                break;
            case "dk":
                AddNordicCross(group, Color.FromRgb(198, 12, 48), Colors.White);
                break;
            case "no":
                AddNordicLikeCross(group, Color.FromRgb(186, 12, 47), Colors.White, Color.FromRgb(0, 32, 91), centered: false);
                break;
            case "pl":
                AddRect(group, 0, 0, 36, 12, Colors.White);
                AddRect(group, 0, 12, 36, 12, Color.FromRgb(220, 20, 60));
                break;
            case "ru":
                AddHorizontalTricolor(group, Colors.White, Color.FromRgb(0, 57, 166), Color.FromRgb(213, 43, 30));
                break;
            case "gr":
                for (var i = 0; i < 9; i++)
                    AddRect(group, 0, i * (24d / 9d), 36, 24d / 9d, i % 2 == 0 ? Color.FromRgb(13, 94, 175) : Colors.White);
                AddRect(group, 0, 0, 14, 13.4, Color.FromRgb(13, 94, 175));
                AddRect(group, 5, 0, 4, 13.4, Colors.White);
                AddRect(group, 0, 4.7, 14, 4, Colors.White);
                break;
            case "kr":
                AddEllipse(group, 13, 6, 10, 12, Color.FromRgb(205, 46, 58));
                AddEllipse(group, 13, 12, 10, 6, Color.FromRgb(0, 71, 160));
                break;
            case "cn":
                AddRect(group, 0, 0, 36, 24, Color.FromRgb(222, 41, 16));
                AddEllipse(group, 5, 4, 5, 5, Color.FromRgb(255, 222, 0));
                break;
            case "cz":
                AddRect(group, 0, 0, 36, 12, Colors.White);
                AddRect(group, 0, 12, 36, 12, Color.FromRgb(215, 20, 26));
                AddPolygon(group, Color.FromRgb(17, 69, 126), new Point(0, 0), new Point(15, 12), new Point(0, 24));
                break;
            case "hu":
                AddHorizontalTricolor(group, Color.FromRgb(206, 42, 55), Colors.White, Color.FromRgb(67, 111, 77));
                break;
            case "tr":
                AddRect(group, 0, 0, 36, 24, Color.FromRgb(227, 10, 23));
                AddEllipse(group, 9, 6, 12, 12, Colors.White);
                AddEllipse(group, 12, 7.5, 10, 9, Color.FromRgb(227, 10, 23));
                AddEllipse(group, 21, 9, 3, 3, Colors.White);
                break;
            default:
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

    private static void AddUnionJack(DrawingGroup group)
    {
        var blue = Color.FromRgb(1, 33, 105);
        var red = Color.FromRgb(200, 16, 46);
        AddRect(group, 0, 0, 36, 24, blue);

        AddPolygon(group, Colors.White, new Point(0, 0), new Point(5, 0), new Point(36, 19), new Point(36, 24), new Point(31, 24), new Point(0, 5));
        AddPolygon(group, Colors.White, new Point(31, 0), new Point(36, 0), new Point(36, 5), new Point(5, 24), new Point(0, 24), new Point(0, 19));
        AddPolygon(group, red, new Point(0, 0), new Point(2.8, 0), new Point(36, 21.2), new Point(36, 24), new Point(33.2, 24), new Point(0, 2.8));
        AddPolygon(group, red, new Point(33.2, 0), new Point(36, 0), new Point(36, 2.8), new Point(2.8, 24), new Point(0, 24), new Point(0, 21.2));

        AddRect(group, 14, 0, 8, 24, Colors.White);
        AddRect(group, 0, 8, 36, 8, Colors.White);
        AddRect(group, 16, 0, 4, 24, red);
        AddRect(group, 0, 10, 36, 4, red);
    }

    private static void AddHorizontalTricolor(DrawingGroup group, Color a, Color b, Color c)
    {
        AddRect(group, 0, 0, 36, 8, a);
        AddRect(group, 0, 8, 36, 8, b);
        AddRect(group, 0, 16, 36, 8, c);
    }

    private static void AddVerticalTricolor(DrawingGroup group, Color a, Color b, Color c)
    {
        AddRect(group, 0, 0, 12, 24, a);
        AddRect(group, 12, 0, 12, 24, b);
        AddRect(group, 24, 0, 12, 24, c);
    }

    private static void AddNordicCross(DrawingGroup group, Color background, Color cross)
    {
        AddRect(group, 0, 0, 36, 24, background);
        AddRect(group, 11, 0, 5, 24, cross);
        AddRect(group, 0, 9.5, 36, 5, cross);
    }

    private static void AddNordicLikeCross(DrawingGroup group, Color background, Color outer, Color inner, bool centered)
    {
        AddRect(group, 0, 0, 36, 24, background);
        var x = centered ? 15d : 10d;
        AddRect(group, x - 2, 0, 7, 24, outer);
        AddRect(group, 0, 8.5, 36, 7, outer);
        AddRect(group, x, 0, 3, 24, inner);
        AddRect(group, 0, 10.5, 36, 3, inner);
    }

    private static void AddPolygon(DrawingGroup group, Color color, params Point[] points)
    {
        if (points.Length < 3) return;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], isFilled: true, isClosed: true);
            context.PolyLineTo(points.Skip(1).ToArray(), isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(color), null, geometry));
    }

    private static void AddRect(DrawingGroup group, double x, double y, double width, double height, Color color) =>
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(color), null, new RectangleGeometry(new Rect(x, y, width, height))));

    private static void AddEllipse(DrawingGroup group, double x, double y, double width, double height, Color color) =>
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(color), null, new EllipseGeometry(new Rect(x, y, width, height))));
}
