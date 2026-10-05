using System.IO.Compression;
using System.Text.RegularExpressions;
using Tyrant.Core.Errors;

namespace Tyrant.Core.Blender;

public enum AddonState { Missing, Older, Current, Newer }

/// <summary>Tyrant's Blender extension: where Blender keeps it, which version is installed, which one this Tyrant ships.</summary>
public static partial class BlenderAddon
{
    public const string Id = "tyrant_blender";
    public const string Repo = "user_default";
    public const string ModuleName = "bl_ext." + Repo + "." + Id;
    public const string ManifestName = "blender_manifest.toml";

    [GeneratedRegex(@"^\s*version\s*=\s*""([^""]+)""", RegexOptions.Multiline)]
    private static partial Regex VersionLine();

    public static string? VersionOf(string manifestToml) => VersionLine().Match(manifestToml) is { Success: true } m ? m.Groups[1].Value : null;

    public static string? BundledVersion(string zipPath)
    {
        if (!File.Exists(zipPath)) return null;
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.GetEntry(ManifestName);
            if (entry is null) return null;
            using var reader = new StreamReader(entry.Open());
            return VersionOf(reader.ReadToEnd());
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    public static string InstalledDir(BlenderInstall blender, string appData) =>
        Path.Combine(appData, "Blender Foundation", "Blender", blender.MajorMinor, "extensions", Repo, Id);

    public static string? InstalledVersion(BlenderInstall blender, string appData)
    {
        var manifest = Path.Combine(InstalledDir(blender, appData), ManifestName);
        return File.Exists(manifest) ? VersionOf(File.ReadAllText(manifest)) : null;
    }

    public static AddonState State(string? installed, string? bundled)
    {
        if (installed is null) return AddonState.Missing;
        if (!Version.TryParse(installed, out var have)) return AddonState.Older;
        if (bundled is null || !Version.TryParse(bundled, out var ship)) return AddonState.Current;
        return have < ship ? AddonState.Older : have > ship ? AddonState.Newer : AddonState.Current;
    }

    /// <summary>The "needs 5.0" message, also used by the app and CLI before opening.</summary>
    public static string TooOld(BlenderInstall blender) => $"Tyrant's Blender tools need Blender 5.0 or newer (found {blender.MajorMinor}).";

    /// <summary>
    /// Installs the zip with Blender's own extension installer, then enables it. The install runs with --factory-startup (the
    /// user's other add-ons stay asleep; one may listen on a port) and --no-prefs: Blender's installer saves the preferences,
    /// and under --factory-startup those are the factory ones, which would wipe the user's settings and add-ons. Enabling then
    /// runs with the user's own preferences and saves them back with Tyrant's add-on turned on.
    /// </summary>
    public static void Install(IBlenderProcess process, BlenderInstall blender, string zip, IReadOnlyDictionary<string, string>? env = null)
    {
        if (!blender.Supported) throw new TyrantException(TyrantErrorCode.BlenderMissing, TooOld(blender));
        var install = process.Run(blender.Exe, ["--factory-startup", "--command", "extension", "install-file", "-r", Repo, "--no-prefs", zip], env, TimeSpan.FromMinutes(2));
        if (install.ExitCode != 0)
            throw new TyrantException(TyrantErrorCode.BlenderFailed, $"Blender could not install Tyrant's add-on: {Tail(install.Output)}");
        var enable = process.Run(blender.Exe, ["-b", "--python-expr", EnableScript], env, TimeSpan.FromMinutes(2));
        if (enable.ExitCode != 0 || !enable.Output.Contains(EnabledMark, StringComparison.Ordinal))
            throw new TyrantException(TyrantErrorCode.BlenderFailed, $"Blender installed Tyrant's add-on but could not turn it on: {Tail(enable.Output)}");
    }

    private const string EnabledMark = "TYRANT-ADDON-ENABLED";

    /// <summary>Turns the add-on on in the user's own preferences and saves them (os._exit: Blender ignores sys.exit in --python-expr).</summary>
    internal static readonly string EnableScript = string.Join("\n",
        "import addon_utils, bpy, os, sys",
        "try:",
        $"    addon_utils.enable('{ModuleName}', default_set=True, persistent=True)",
        "    bpy.ops.wm.save_userpref()",
        $"    print('{EnabledMark}')",
        "    sys.stdout.flush()",
        "    os._exit(0)",
        "except Exception as ex:",
        "    print('error:', ex)",
        "    sys.stdout.flush()",
        "    os._exit(1)");

    private static string Tail(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(" ", lines.TakeLast(5));
    }
}
