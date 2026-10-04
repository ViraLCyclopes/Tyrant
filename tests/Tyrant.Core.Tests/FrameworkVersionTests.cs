using System.Xml.Linq;
using Tyrant.Core.Dumping;
using Tyrant.Core.Install;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class FrameworkVersionTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tyrant.slnx"))) dir = dir.Parent;
        return dir!.FullName;
    }

    [Fact]
    public void The_framework_dll_carries_the_framework_version()
    {
        var csproj = XDocument.Load(Path.Combine(RepoRoot(), "game", "Tyrant.Framework", "Tyrant.Framework.csproj"));
        Assert.Equal(FrameworkInfo.Version, csproj.Descendants("Version").Single().Value);
    }

    [Fact]
    public void The_installed_framework_version_is_read_from_its_dll()
    {
        using var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        Directory.CreateDirectory(Path.Combine(game.Root, "Mods"));
        // Any versioned .NET assembly will do: this test assembly is 0.1.0 (Directory.Build.props).
        File.Copy(typeof(FrameworkVersionTests).Assembly.Location, Path.Combine(game.Root, "Mods", ModLoaderInstaller.FrameworkFile));

        Assert.Equal("0.1.0", ModLoaderInstaller.InstalledFrameworkVersion(install));
    }

    [Fact]
    public void A_missing_or_unversioned_framework_has_no_version()
    {
        using var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        Assert.Null(ModLoaderInstaller.InstalledFrameworkVersion(install));
        Directory.CreateDirectory(Path.Combine(game.Root, "Mods"));
        File.WriteAllText(Path.Combine(game.Root, "Mods", ModLoaderInstaller.FrameworkFile), "not a dll");
        Assert.Null(ModLoaderInstaller.InstalledFrameworkVersion(install));
    }

    [Fact]
    public void The_framework_layout_puts_the_mod_in_Mods_and_its_libraries_in_UserLibs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Tyrant.Framework.dll"), "f");
        File.WriteAllText(Path.Combine(dir, "Tyrant.Framework.Core.dll"), "c");
        File.WriteAllText(Path.Combine(dir, "Tyrant.Dumper.dll"), "d");

        var layout = ModLoaderInstaller.FrameworkLayout(dir).Select(f => f.Relative).OrderBy(r => r).ToArray();

        Assert.Equal(["Mods/Tyrant.Framework.dll", "UserLibs/Tyrant.Framework.Core.dll"], layout);
    }
}
