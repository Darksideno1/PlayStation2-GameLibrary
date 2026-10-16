using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp.PixelFormats;

namespace PlayStation2GameLibrary;

internal sealed class MediaLibraryService
{
    private static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
    private readonly AppSettings _settings;

    public MediaLibraryService(AppSettings settings) => _settings = settings;

    public string MediaRoot => AppSettingsService.ResolveMediaRoot(_settings);
    public string CoversDirectory => Path.Combine(MediaRoot, "Covers");

    public string? FindCoverForSerial(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial) || !Directory.Exists(CoversDirectory)) return null;
        var key = serial.Trim();
        foreach (var extension in SupportedExtensions)
        {
            var direct = Path.Combine(CoversDirectory, key + extension);
            if (File.Exists(direct)) return direct;
        }

        try
        {
            return Directory.EnumerateFiles(CoversDirectory)
                .FirstOrDefault(path => SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
                    && string.Equals(Path.GetFileNameWithoutExtension(path), key, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    public ImageSource? LoadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            using var image = SixLabors.ImageSharp.Image.Load<Bgra32>(path);
            var pixels = new byte[checked(image.Width * image.Height * 4)];
            image.CopyPixelDataTo(pixels);
            var source = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, pixels, image.Width * 4);
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

    public (int matched, int unmatched) ScanKnownCoverFiles(IReadOnlyCollection<string> primarySerials)
    {
        if (!Directory.Exists(CoversDirectory)) return (0, 0);
        var known = new HashSet<string>(primarySerials.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
        var matched = 0;
        var unmatched = 0;
        try
        {
            foreach (var path in Directory.EnumerateFiles(CoversDirectory))
            {
                if (!SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) continue;
                if (known.Contains(Path.GetFileNameWithoutExtension(path))) matched++;
                else unmatched++;
            }
        }
        catch
        {
            return (matched, unmatched);
        }
        return (matched, unmatched);
    }
}
