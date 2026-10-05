using Tyrant.Core.Blender;
using Tyrant.Core.Install;

namespace Tyrant.Core.Tests;

/// <summary>Runs the real Blender 5.x (found like Tyrant finds it) with throwaway user folders; skipped without Blender.</summary>
[Trait("Category", "Blender")]
public class BlenderIntegrationTests
{
    internal static BlenderInstall? Blender() =>
        new BlenderLocator(new RegistrySteamRootProvider(), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), new BlenderProcess())
            .Find(Environment.GetEnvironmentVariable("TYRANT_BLENDER")) is { Install: { Supported: true } b } ? b : null;

    /// <summary>Blender's user folders pointed at a temp dir, so the user's own prefs and extensions are never touched.</summary>
    internal static Dictionary<string, string> ThrowawayUser(out string root)
    {
        root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "blender-user")).FullName;
        return new()
        {
            ["BLENDER_USER_RESOURCES"] = root,
            ["BLENDER_USER_CONFIG"] = Path.Combine(root, "config"),
            ["BLENDER_USER_SCRIPTS"] = Path.Combine(root, "scripts"),
            ["BLENDER_USER_EXTENSIONS"] = Path.Combine(root, "extensions"),
        };
    }

    internal static string AddonZip()
    {
        var zip = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "tyrant_blender.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
        System.IO.Compression.ZipFile.CreateFromDirectory(Path.Combine(BlenderAddonTests.RepoRoot(), "tools", "blender", "tyrant_blender"), zip);
        return zip;
    }

    [SkippableFact]
    public void Installed_addon_is_enabled_in_the_next_normal_start()
    {
        var blender = Blender();
        Skip.If(blender is null, "Blender 5.x not found");
        var env = ThrowawayUser(out _);
        var process = new BlenderProcess();

        BlenderAddon.Install(process, blender!, AddonZip(), env);
        var check = process.Run(blender!.Exe, ["-b", "--python", Path.Combine(BlenderAddonTests.RepoRoot(), "tools", "blender", "tests", "addon_enabled.py")], env);

        Assert.Contains("ENABLED", check.Output);
    }
}
