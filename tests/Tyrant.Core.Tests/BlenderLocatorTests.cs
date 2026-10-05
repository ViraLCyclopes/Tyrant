using Tyrant.Core.Blender;
using Tyrant.Core.Install;

namespace Tyrant.Core.Tests;

public class BlenderLocatorTests
{
    private sealed class Steam(string? root) : ISteamRootProvider
    {
        public string? GetSteamRoot() => root;
    }

    private static string Temp() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"))).FullName;

    private static string Exe(string dir)
    {
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "blender.exe");
        File.WriteAllText(exe, "");
        return exe;
    }

    [Theory]
    [InlineData("Blender 5.2.2 LTS\n\tbuild date: 2026-09-01", 5, 2, 2)]
    [InlineData("Blender 4.5.0\n", 4, 5, 0)]
    [InlineData("INFO | Running python\nBlender 5.0.1 Alpha\n", 5, 0, 1)]
    public void Reads_the_version_line(string output, int major, int minor, int patch)
    {
        Assert.Equal(new Version(major, minor, patch), BlenderLocator.ParseVersion(output));
    }

    [Fact]
    public void Garbage_is_no_version() => Assert.Null(BlenderLocator.ParseVersion("not blender"));

    [Fact]
    public void Finds_blender_in_a_steam_library_and_program_files_highest_first()
    {
        var steam = Temp();
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\" { }");
        var steamExe = Exe(Path.Combine(steam, "steamapps", "common", "Blender"));
        var programFiles = Temp();
        var old = Exe(Path.Combine(programFiles, "Blender Foundation", "Blender 4.5"));
        var newer = Exe(Path.Combine(programFiles, "Blender Foundation", "Blender 5.1"));
        var process = new FakeBlenderProcess();
        process.Versions[steamExe] = "Blender 5.2.2 LTS";
        process.Versions[old] = "Blender 4.5.0";
        process.Versions[newer] = "Blender 5.1.0";

        var found = new BlenderLocator(new Steam(steam), programFiles, process).Find(null);

        Assert.Equal(steamExe, found.Install!.Exe);
        Assert.Equal(new Version(5, 2, 2), found.Install.Version);
        Assert.True(found.Install.Supported);
    }

    [Fact]
    public void A_configured_path_wins_even_when_older()
    {
        var programFiles = Temp();
        var newer = Exe(Path.Combine(programFiles, "Blender Foundation", "Blender 5.1"));
        var mine = Exe(Path.Combine(Temp(), "my blender"));
        var process = new FakeBlenderProcess();
        process.Versions[newer] = "Blender 5.1.0";
        process.Versions[mine] = "Blender 4.5.0";

        var found = new BlenderLocator(new Steam(null), programFiles, process).Find(mine);

        Assert.Equal(mine, found.Install!.Exe);
        Assert.False(found.Install.Supported);
    }

    [Fact]
    public void Configured_path_that_vanished_falls_back_to_detection()
    {
        var programFiles = Temp();
        var newer = Exe(Path.Combine(programFiles, "Blender Foundation", "Blender 5.1"));
        var process = new FakeBlenderProcess();
        process.Versions[newer] = "Blender 5.1.0";
        var gone = Path.Combine(Temp(), "gone", "blender.exe");

        var found = new BlenderLocator(new Steam(null), programFiles, process).Find(gone);

        Assert.Equal(newer, found.Install!.Exe);
        Assert.Equal(gone, found.MissingConfigured);
    }

    [Fact]
    public void Nothing_found_is_null_not_an_error()
    {
        var found = new BlenderLocator(new Steam(null), Temp(), new FakeBlenderProcess()).Find(null);
        Assert.Null(found.Install);
    }
}
