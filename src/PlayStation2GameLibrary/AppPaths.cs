using System.IO;

namespace PlayStation2GameLibrary;

internal static class AppPaths
{
    public static string PortableRoot
    {
        get
        {
            var baseDir = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var directory = new DirectoryInfo(baseDir);
            if (directory.Name.Equals("USER", StringComparison.OrdinalIgnoreCase) ||
                directory.Name.Equals("ADMIN", StringComparison.OrdinalIgnoreCase))
                return directory.Parent?.FullName ?? baseDir;
            return baseDir;
        }
    }

    public static string DataDirectory => Environment.GetEnvironmentVariable("PS2_LIBRARY_DATA_DIR")
        ?? Path.Combine(PortableRoot, "Data");

    public static string ConfigDirectory => Environment.GetEnvironmentVariable("PS2_LIBRARY_CONFIG_DIR")
        ?? Path.Combine(PortableRoot, "Config");

    public static string DatabasePath => Path.Combine(DataDirectory, "catalogue.db");

    public static string DefaultMediaRoot => Path.Combine(PortableRoot, "Media");

    public static void EnsurePortableFolders()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(DefaultMediaRoot);
        Directory.CreateDirectory(Path.Combine(DefaultMediaRoot, "Covers"));
    }
}
