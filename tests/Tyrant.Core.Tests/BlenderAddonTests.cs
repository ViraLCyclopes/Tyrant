using System.IO.Compression;
using Tyrant.Core.Blender;

namespace Tyrant.Core.Tests;

public class BlenderAddonTests
{
    private static string Temp() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"))).FullName;

    [Fact]
    public void Reads_the_manifest_version()
    {
        Assert.Equal("0.3.1", BlenderAddon.VersionOf("schema_version = \"1.0.0\"\nid = \"tyrant_blender\"\nversion = \"0.3.1\"\n"));
        Assert.Null(BlenderAddon.VersionOf("id = \"x\""));
    }

    [Fact]
    public void Reads_the_bundled_zips_version()
    {
        var zip = Path.Combine(Temp(), "tyrant_blender.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("blender_manifest.toml").Open()))
            writer.Write("id = \"tyrant_blender\"\nversion = \"0.2.0\"\n");
        Assert.Equal("0.2.0", BlenderAddon.BundledVersion(zip));
        Assert.Null(BlenderAddon.BundledVersion(Path.Combine(Temp(), "missing.zip")));
    }

    [Fact]
    public void Finds_the_installed_copy_under_the_blender_version_folder()
    {
        var appData = Temp();
        var blender = new BlenderInstall("blender.exe", new Version(5, 2, 2));
        var dir = BlenderAddon.InstalledDir(blender, appData);
        Assert.Equal(Path.Combine(appData, "Blender Foundation", "Blender", "5.2", "extensions", "user_default", "tyrant_blender"), dir);
        Assert.Null(BlenderAddon.InstalledVersion(blender, appData));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "blender_manifest.toml"), "version = \"0.1.0\"");
        Assert.Equal("0.1.0", BlenderAddon.InstalledVersion(blender, appData));
    }

    [Theory]
    [InlineData(null, "0.2.0", AddonState.Missing)]
    [InlineData("0.1.0", "0.2.0", AddonState.Older)]
    [InlineData("0.2.0", "0.2.0", AddonState.Current)]
    [InlineData("0.3.0", "0.2.0", AddonState.Newer)]
    [InlineData("garbage", "0.2.0", AddonState.Older)]
    public void Compares_installed_with_bundled(string? installed, string bundled, AddonState expected) =>
        Assert.Equal(expected, BlenderAddon.State(installed, bundled));

    [Fact]
    public void The_source_manifest_carries_tyrants_version()
    {
        var root = RepoRoot();
        var props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));
        var version = System.Text.RegularExpressions.Regex.Match(props, "<Version>([^<]+)</Version>").Groups[1].Value;
        var manifest = File.ReadAllText(Path.Combine(root, "tools", "blender", "tyrant_blender", "blender_manifest.toml"));
        Assert.Equal(version, BlenderAddon.VersionOf(manifest));
        Assert.Contains("blender_version_min = \"5.0.0\"", manifest);
    }

    [Fact]
    public void Install_runs_blenders_extension_installer_without_the_users_addons()
    {
        var process = new FakeBlenderProcess();
        var blender = new BlenderInstall(@"C:\b\blender.exe", new Version(5, 2, 2));

        BlenderAddon.Install(process, blender, @"C:\t\tyrant_blender.zip");

        var run = Assert.Single(process.Runs);
        Assert.Equal(["--factory-startup", "--command", "extension", "install-file", "-r", "user_default", "-e", @"C:\t\tyrant_blender.zip"], run.Args);
    }

    [Fact]
    public void A_failed_install_reports_blenders_output()
    {
        var process = new FakeBlenderProcess { OnRun = (_, _) => new BlenderRun(1, "Error: bad zip") };
        var ex = Assert.Throws<Tyrant.Core.Errors.TyrantException>(() =>
            BlenderAddon.Install(process, new BlenderInstall("b.exe", new Version(5, 2)), "x.zip"));
        Assert.Contains("bad zip", ex.Message);
    }

    [Fact]
    public void Install_refuses_blender_4()
    {
        var ex = Assert.Throws<Tyrant.Core.Errors.TyrantException>(() =>
            BlenderAddon.Install(new FakeBlenderProcess(), new BlenderInstall("b.exe", new Version(4, 5)), "x.zip"));
        Assert.Contains("Blender 5.0 or newer (found 4.5)", ex.Message);
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tyrant.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Tyrant.slnx not found above the test folder.");
    }
}
