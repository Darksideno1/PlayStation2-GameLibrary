using System.IO;
using System.Text.Json;

namespace PlayStation2GameLibrary;

internal sealed class AppSettings
{
    public string MediaLibraryLocation { get; set; } = @".\Media";
    public bool ThumbnailCacheEnabled { get; set; } = true;
}

internal static class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PlayStation2GameLibrary");

    private static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public static string ResolveMediaRoot(AppSettings settings)
    {
        var configured = string.IsNullOrWhiteSpace(settings.MediaLibraryLocation) ? @".\Media" : settings.MediaLibraryLocation.Trim();
        if (Path.IsPathRooted(configured)) return Path.GetFullPath(configured);
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured));
    }
}
