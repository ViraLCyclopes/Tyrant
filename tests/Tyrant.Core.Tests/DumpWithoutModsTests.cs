using System.Text.Json;
using Tyrant.Core.Assets;
using Tyrant.Core.Dumping;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Dumper.Serialization;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class DumpWithoutModsTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));

    private static GameInstall Installed(FakeGame game)
    {
        var install = new GameInstall(game.Root, null);
        Directory.CreateDirectory(ModLoaderInstaller.RecordDir(install));
        File.WriteAllText(Path.Combine(ModLoaderInstaller.RecordDir(install), "install.json"), """{"installedLoader":true,"files":[],"createdDirs":[]}""");
        return install;
    }

    /// <summary>The game as Tyrant starts it: notes what the framework would see, then the dumper writes its dump (or nothing).</summary>
    private sealed class FakeGameStart(bool dumps) : IGameLauncher
    {
        public bool ModsOffDuringLaunch { get; private set; }

        public bool IsRunning(GameInstall install) => false;

        public void Launch(GameInstall install)
        {
            ModsOffDuringLaunch = DumpMarker.IsActive(ModLoaderInstaller.RecordDir(install), DateTime.UtcNow);
            if (!dumps) return;
            var request = JsonDocument.Parse(File.ReadAllText(DumpRunner.RequestPath(install))).RootElement;
            var result = new DumpResult();
            result.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo("PrehistoricKingdom.AnimalData", "Animal", 1), "{}"));
            DumpWriter.Write(request.GetProperty("outputDir").GetString()!, result, [], new DumpManifest { RequestId = request.GetProperty("requestId").GetString()!, BuildGuid = "b" });
        }
    }

    [Fact]
    public void The_game_starts_with_mods_off_for_the_dump_and_they_are_back_after_it()
    {
        using var game = new FakeGame();
        var install = Installed(game);
        var ws = Workspace.Create(TempDir(), install);
        var start = new FakeGameStart(dumps: true);

        new DumpRunner(start) { PollInterval = TimeSpan.FromMilliseconds(10) }.Run(install, ws, TimeSpan.FromSeconds(5), null, CancellationToken.None);

        Assert.True(start.ModsOffDuringLaunch);
        Assert.False(DumpMarker.IsActive(ModLoaderInstaller.RecordDir(install), DateTime.UtcNow));
        Assert.False(File.Exists(Path.Combine(ModLoaderInstaller.RecordDir(install), DumpMarker.FileName)));
    }

    [Fact]
    public void A_dump_that_never_arrives_gives_the_player_their_mods_back()
    {
        using var game = new FakeGame();
        var install = Installed(game);
        var ws = Workspace.Create(TempDir(), install);

        Assert.Throws<TyrantException>(() => new DumpRunner(new FakeGameStart(dumps: false)) { PollInterval = TimeSpan.FromMilliseconds(10) }
            .Run(install, ws, TimeSpan.FromMilliseconds(200), null, CancellationToken.None));

        Assert.False(File.Exists(Path.Combine(ModLoaderInstaller.RecordDir(install), DumpMarker.FileName)));
    }

    private static DumpManifestFile Made(string dumperVersion) => new(1, "r", "b", dumperVersion, "2026-11-04T00:00:00Z", [], [], []);

    [Theory]
    [InlineData("0.1.0", true)]
    [InlineData("1.0.0", true)]
    [InlineData("", true)]
    [InlineData("1.1.0", false)]
    [InlineData("2.0.0", false)]
    public void Only_a_dumper_that_runs_without_mods_makes_a_game_only_dump(string version, bool mayIncludeMods) =>
        Assert.Equal(mayIncludeMods, Made(version).MayIncludeMods);

    [Fact]
    public void The_installed_dumper_is_one_that_runs_without_mods() =>
        Assert.False(Made(DumperInfo.Version).MayIncludeMods);

    [Fact]
    public void A_workspace_is_only_flagged_when_it_has_an_old_dump()
    {
        using var game = new FakeGame();
        var ws = Workspace.Create(TempDir(), new GameInstall(game.Root, null));
        Assert.False(DumpManifestFile.MayIncludeModsIn(ws)); // no dump yet: Check says so already

        DumpWriter.Write(ws.DataDir, new DumpResult(), [], new DumpManifest { RequestId = "r", BuildGuid = "b", DumperVersion = "1.0.0" });
        Assert.True(DumpManifestFile.MayIncludeModsIn(ws));

        DumpWriter.Write(ws.DataDir, new DumpResult(), [], new DumpManifest { RequestId = "r", BuildGuid = "b", DumperVersion = DumperInfo.Version });
        Assert.False(DumpManifestFile.MayIncludeModsIn(ws));
    }

    private static (FakeGame Game, Workspace Ws, ModProject Mod, IReadOnlyList<SpeciesSkins> Species) SpeciesMod()
    {
        var game = new FakeGame();
        var ws = Workspace.Create(TempDir(), new GameInstall(game.Root, null));
        SkinDumps.Write(ws.DataDir);
        var species = SpeciesSkinsReader.Load(ws);
        var mod = ModProject.Create(ws, "zhuchengtyrannus", null, null);
        mod.AddSpecies(species, "zucheng", "Carcharodontosaurus", "Zhuchengtyrannus", "Zhuchengtyrannus", "magnus");
        return (game, ws, mod, species);
    }

    [Fact]
    public void A_starting_skin_the_template_does_not_have_is_an_error_naming_the_ones_it_has()
    {
        var (game, _, mod, species) = SpeciesMod();
        using var _ = game;
        mod.Manifest.Species[0].StartingSkin = 21;

        var result = new ModChecker(_ => (4, 4)).Check(mod, SkinDumps.Index(), species);

        var error = Assert.Single(result.Errors, e => e.Contains("zucheng") && e.Contains("21"));
        Assert.Contains("Base", error);
        Assert.Contains("Alt 1", error);
    }

    [Fact]
    public void A_starting_skin_the_template_has_is_fine()
    {
        var (game, _, mod, species) = SpeciesMod();
        using var _ = game;
        mod.Manifest.Species[0].StartingSkin = 1;

        var result = new ModChecker(_ => (4, 4)).Check(mod, SkinDumps.Index(), species);

        Assert.DoesNotContain(result.Errors, e => e.Contains("zucheng") && e.Contains("starts"));
    }

    [Fact]
    public void Check_warns_that_an_old_dump_may_list_mods_as_the_game()
    {
        var (game, _, mod, species) = SpeciesMod();
        using var _ = game;

        var old = new ModChecker(_ => (4, 4)).Check(mod, SkinDumps.Index(), species, dumpMayIncludeMods: true);
        var fresh = new ModChecker(_ => (4, 4)).Check(mod, SkinDumps.Index(), species);

        Assert.Contains(old.Warnings, w => w.Contains("Run data dump"));
        Assert.DoesNotContain(fresh.Warnings, w => w.Contains("Run data dump"));
    }
}
