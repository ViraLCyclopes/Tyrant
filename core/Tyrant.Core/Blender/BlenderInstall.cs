using System.Text.RegularExpressions;
using Tyrant.Core.Install;

namespace Tyrant.Core.Blender;

/// <summary>A blender.exe and its version; Tyrant's tools need 5.0 or newer.</summary>
public sealed record BlenderInstall(string Exe, Version Version)
{
    public bool Supported => Version.Major >= 5;
    public string MajorMinor => $"{Version.Major}.{Version.Minor}";
}

/// <summary>What finding Blender gave: the install (null = none), and the user's set path when it no longer exists.</summary>
public sealed record BlenderFind(BlenderInstall? Install, string? MissingConfigured);

/// <summary>Finds blender.exe: the user's path, then Steam libraries, then Program Files\Blender Foundation (highest version first).</summary>
public sealed partial class BlenderLocator(ISteamRootProvider steam, string programFiles, IBlenderProcess process)
{
    [GeneratedRegex(@"^Blender (\d+)\.(\d+)(?:\.(\d+))?", RegexOptions.Multiline)]
    private static partial Regex VersionLine();

    public static Version? ParseVersion(string output) =>
        VersionLine().Match(output) is { Success: true } m
            ? new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0)
            : null;

    public IReadOnlyList<string> Candidates()
    {
        var found = new List<string>();
        if (steam.GetSteamRoot() is { } root)
            foreach (var library in GameInstallLocator.GetLibraryPaths(root))
                found.Add(Path.Combine(library, "steamapps", "common", "Blender", "blender.exe"));
        var foundation = Path.Combine(programFiles, "Blender Foundation");
        if (Directory.Exists(foundation))
            found.AddRange(Directory.GetDirectories(foundation).Select(d => Path.Combine(d, "blender.exe")));
        return found.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The exe's version from "blender --version"; null when it does not answer like Blender.</summary>
    public BlenderInstall? Probe(string exe)
    {
        if (!File.Exists(exe)) return null;
        try
        {
            var run = process.Run(exe, ["--version"], timeout: TimeSpan.FromSeconds(30));
            return ParseVersion(run.Output) is { } version ? new BlenderInstall(exe, version) : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    public BlenderFind Find(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (Probe(configured) is { } mine) return new BlenderFind(mine, null);
            if (!File.Exists(configured)) return new BlenderFind(Detect(), configured);
            return new BlenderFind(null, null); // exists but is not Blender: show nothing rather than guessing
        }
        return new BlenderFind(Detect(), null);
    }

    private BlenderInstall? Detect() =>
        Candidates().Select(Probe).OfType<BlenderInstall>().OrderByDescending(b => b.Version).FirstOrDefault();
}
