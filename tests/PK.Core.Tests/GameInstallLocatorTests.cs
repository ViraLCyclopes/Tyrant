using PK.Core.Errors;
using PK.Core.Install;

namespace PK.Core.Tests;

public class GameInstallLocatorTests
{
    private sealed class StubSteam(string? root) : ISteamRootProvider
    {
        public string? GetSteamRoot() => root;
    }

    /// <summary>Creates a fake Steam root whose libraryfolders.vdf lists the given libraries.</summary>
    private static string MakeSteamRoot(params string[] extraLibraries)
    {
        var steamRoot = Path.Combine(Path.GetTempPath(), "pk-tests", Guid.NewGuid().ToString("N"), "Steam");
        Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps"));
        var entries = string.Concat(extraLibraries.Select((lib, i) =>
            $"\t\"{i + 1}\"\n\t{{\n\t\t\"path\"\t\t\"{lib.Replace(@"\", @"\\")}\"\n\t}}\n"));
        File.WriteAllText(Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"),
            $"\"libraryfolders\"\n{{\n\t\"0\"\n\t{{\n\t\t\"path\"\t\t\"{steamRoot.Replace(@"\", @"\\")}\"\n\t}}\n{entries}}}\n");
        return steamRoot;
    }

    [Fact]
    public void Detect_finds_game_in_secondary_library()
    {
        using var game = new FakeGame();
        game.WriteAppManifest("1234560");
        var locator = new GameInstallLocator(new StubSteam(MakeSteamRoot(game.LibraryRoot)));

        var install = locator.Detect();

        Assert.Equal(Path.GetFullPath(game.Root), install.RootDir);
        Assert.Equal("1234560", install.SteamAppId);
    }

    [Fact]
    public void Detect_without_steam_throws_game_not_found_with_pick_folder_fix()
    {
        var locator = new GameInstallLocator(new StubSteam(null));
        var ex = Assert.Throws<PkException>(() => locator.Detect());
        Assert.Equal(PkErrorCode.GameNotFound, ex.Code);
        Assert.Equal(FixAction.PickGameFolder, ex.Fix);
    }

    [Fact]
    public void Detect_survives_corrupt_libraryfolders_vdf()
    {
        var steamRoot = MakeSteamRoot();
        File.WriteAllText(Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\" { \"0\" {");
        var locator = new GameInstallLocator(new StubSteam(steamRoot));
        var ex = Assert.Throws<PkException>(() => locator.Detect());
        Assert.Equal(PkErrorCode.GameNotFound, ex.Code);
    }

    [Fact]
    public void FromPath_accepts_root_folder_or_exe_path()
    {
        using var game = new FakeGame();
        var locator = new GameInstallLocator(new StubSteam(null));

        Assert.Equal(Path.GetFullPath(game.Root), locator.FromPath(game.Root).RootDir);
        Assert.Equal(Path.GetFullPath(game.Root), locator.FromPath(game.Root + Path.DirectorySeparatorChar).RootDir);
        Assert.Equal(Path.GetFullPath(game.Root), locator.FromPath(Path.Combine(game.Root, GameInstall.ExeName)).RootDir);
    }

    [Fact]
    public void FromPath_rejects_folder_without_game_files()
    {
        var empty = Directory.CreateTempSubdirectory("pk-tests-").FullName;
        var ex = Assert.Throws<PkException>(() => new GameInstallLocator(new StubSteam(null)).FromPath(empty));
        Assert.Equal(PkErrorCode.GameNotFound, ex.Code);
        Assert.Equal(FixAction.PickGameFolder, ex.Fix);
    }

    [Fact]
    public void App_id_is_null_when_manifest_missing_or_corrupt()
    {
        using var game = new FakeGame();
        File.WriteAllText(Path.Combine(game.SteamApps, "appmanifest_1.acf"), "\"AppState\" {");
        game.WriteAppManifest("999", installDir: "Some Other Game");
        Assert.Null(GameInstallLocator.FindAppIdFor(game.Root));
    }

    [Fact]
    public void Install_paths_follow_unity_layout()
    {
        var install = new GameInstall(@"E:\Games\Prehistoric Kingdom", null);
        Assert.Equal(@"E:\Games\Prehistoric Kingdom\Prehistoric Kingdom_Data\Managed\Assembly-CSharp.dll", install.AssemblyCSharpPath);
        Assert.Equal(@"E:\Games\Prehistoric Kingdom\Prehistoric Kingdom_Data\boot.config", install.BootConfigPath);
    }

    [Fact]
    public void ContainsPath_matches_root_and_children_but_not_similar_siblings()
    {
        var install = new GameInstall(@"E:\Games\Prehistoric Kingdom", null);
        Assert.True(install.ContainsPath(@"E:\Games\Prehistoric Kingdom"));
        Assert.True(install.ContainsPath(@"e:\games\prehistoric kingdom\Prehistoric Kingdom_Data\x.json"));
        Assert.False(install.ContainsPath(@"E:\Games\Prehistoric Kingdom Modding\x.json"));
    }

    [Fact]
    public void Locked_manifest_is_skipped()
    {
        using var game = new FakeGame();
        var locked = Path.Combine(game.SteamApps, "appmanifest_1.acf");
        File.WriteAllText(locked, "x");
        game.WriteAppManifest("777");

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal("777", GameInstallLocator.FindAppIdFor(game.Root));
    }

    [Fact]
    public void Library_with_invalid_path_is_ignored()
    {
        var steamRoot = MakeSteamRoot("bad\0path");
        Assert.Equal(new[] { Path.GetFullPath(steamRoot) }, GameInstallLocator.GetLibraryPaths(steamRoot));
    }
}
