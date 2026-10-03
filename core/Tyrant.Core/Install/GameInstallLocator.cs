using Tyrant.Core.Errors;

namespace Tyrant.Core.Install;

public sealed class GameInstallLocator(ISteamRootProvider steam)
{
    public const string InstallDirName = "Prehistoric Kingdom";

    /// <summary>Finds the game in any Steam library.</summary>
    public GameInstall Detect()
    {
        var steamRoot = steam.GetSteamRoot()
            ?? throw NotFound("Steam installation not found. Pick the game folder manually.");
        foreach (var library in GetLibraryPaths(steamRoot))
        {
            var root = Path.Combine(library, "steamapps", "common", InstallDirName);
            if (IsValid(root)) return new GameInstall(root, FindAppIdFor(root));
        }
        throw NotFound("Prehistoric Kingdom was not found in any Steam library. Pick the game folder manually.");
    }

    /// <summary>Validates a user-supplied game folder or path to the game exe.</summary>
    public GameInstall FromPath(string path)
    {
        var full = Path.GetFullPath(path);
        if (File.Exists(full) && string.Equals(Path.GetFileName(full), GameInstall.ExeName, StringComparison.OrdinalIgnoreCase))
            full = Path.GetDirectoryName(full)!;
        full = Path.TrimEndingDirectorySeparator(full);
        if (!IsValid(full))
            throw NotFound($"'{path}' is not a Prehistoric Kingdom install (expected '{GameInstall.ExeName}' and '{GameInstall.DataDirName}\\Managed\\Assembly-CSharp.dll').");
        return new GameInstall(full, FindAppIdFor(full));
    }

    public static bool IsValid(string rootDir)
    {
        var install = new GameInstall(rootDir, null);
        return File.Exists(Path.Combine(rootDir, GameInstall.ExeName)) && File.Exists(install.AssemblyCSharpPath);
    }

    /// <summary>Steam root plus every library listed in libraryfolders.vdf (corrupt file → root only).</summary>
    public static IReadOnlyList<string> GetLibraryPaths(string steamRoot)
    {
        var paths = new List<string> { steamRoot };
        var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            try
            {
                var libraries = KeyValuesParser.Parse(File.ReadAllText(vdf))["libraryfolders"];
                foreach (var lib in libraries?.Children ?? [])
                    if (lib["path"]?.Value is { Length: > 0 } p) paths.Add(p);
            }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
            {
                // fall back to the Steam root only
            }
        }
        var full = new List<string>();
        foreach (var p in paths)
        {
            try
            {
                full.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // skip library entries that are not valid paths
            }
        }
        return full.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Reads steamapps/appmanifest_*.acf next to the install to find its Steam app id.</summary>
    internal static string? FindAppIdFor(string rootDir)
    {
        var common = Path.GetDirectoryName(rootDir);
        var steamapps = common is null ? null : Path.GetDirectoryName(common);
        if (steamapps is null || !Directory.Exists(steamapps)) return null;
        var folder = Path.GetFileName(rootDir);
        IEnumerable<string> manifests;
        try
        {
            manifests = Directory.GetFiles(steamapps, "appmanifest_*.acf");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        foreach (var acf in manifests)
        {
            try
            {
                var state = KeyValuesParser.Parse(File.ReadAllText(acf))["AppState"];
                if (string.Equals(state?["installdir"]?.Value, folder, StringComparison.OrdinalIgnoreCase))
                    return state?["appid"]?.Value;
            }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
            {
                // ignore unreadable manifests (Steam rewrites them while updating)
            }
        }
        return null;
    }

    private static TyrantException NotFound(string message) =>
        new(TyrantErrorCode.GameNotFound, message, FixAction.PickGameFolder);
}
