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
    /// Installs and enables the zip with Blender's own extension installer. --factory-startup keeps the user's other add-ons
    /// from starting during the run (one of them may listen on a port, as a bridge add-on does).
    /// </summary>
    public static void Install(IBlenderProcess process, BlenderInstall blender, string zip, IReadOnlyDictionary<string, string>? env = null)
    {
        if (!blender.Supported) throw new TyrantException(TyrantErrorCode.BlenderMissing, TooOld(blender));
        var run = process.Run(blender.Exe, ["--factory-startup", "--command", "extension", "install-file", "-r", Repo, "-e", zip], env, TimeSpan.FromMinutes(2));
        if (run.ExitCode != 0)
            throw new TyrantException(TyrantErrorCode.BlenderFailed, $"Blender could not install Tyrant's add-on: {Tail(run.Output)}");
    }

    private static string Tail(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(" ", lines.TakeLast(5));
    }
}
