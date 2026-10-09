using Tyrant.Core.Dumping;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class ModDeleteTests
{
    private static (FakeGame Game, GameInstall Install, Workspace Ws, ModProject Mod) Setup()
    {
        var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(Path.Combine(TestTemp.Root, Guid.NewGuid().ToString("N"), "ws"), install);
        return (game, install, ws, ModProject.Create(ws, "red-spot", "Red spot", null));
    }

    private static string Installed(GameInstall install, string id)
    {
        var dir = Path.Combine(ModLoaderInstaller.ModsDir(install), id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ModManifest.FileName), "{}");
        return dir;
    }

    private static string BlenderProjects(Workspace ws)
    {
        var dir = Path.Combine(ws.Dir, "blender", "red-spot");
        Directory.CreateDirectory(Path.Combine(dir, "carcharodontosaurus-blue"));
        return dir;
    }

    [Fact]
    public void Deleting_recycles_the_mod_and_its_blender_projects_and_removes_it_from_the_game()
    {
        var (game, install, ws, mod) = Setup();
        using var _ = game;
        var inGame = Installed(install, "red-spot");
        var blender = BlenderProjects(ws);

        var recycled = ModProject.Delete(ws, install, "red-spot", removeFromGame: true, new GameMods());

        Assert.Equal([mod.Dir, blender], recycled.Recycled);
        Assert.Empty(recycled.Kept);
        Assert.True(TestRecycleBin.Instance.Holds(mod.Dir));
        Assert.True(TestRecycleBin.Instance.Holds(blender));
        Assert.False(Directory.Exists(inGame));
        Assert.DoesNotContain("red-spot", ModProject.Ids(ws));
    }

    [Fact]
    public void Keeping_it_in_the_game_leaves_the_installed_copy()
    {
        var (game, install, ws, _) = Setup();
        using var __ = game;
        var inGame = Installed(install, "red-spot");

        ModProject.Delete(ws, install, "red-spot", removeFromGame: false, new GameMods());

        Assert.True(Directory.Exists(inGame));
    }

    [Fact]
    public void A_mod_that_is_not_installed_is_deleted_even_when_asked_to_remove_it_from_the_game()
    {
        var (game, install, ws, mod) = Setup();
        using var _ = game;

        Assert.Equal([mod.Dir], ModProject.Delete(ws, install, "red-spot", removeFromGame: true, new GameMods()).Recycled);
    }

    [Fact]
    public void A_running_game_stops_the_delete_before_anything_moves()
    {
        var (game, install, ws, mod) = Setup();
        using var _ = game;
        var inGame = Installed(install, "red-spot");

        var ex = Assert.Throws<TyrantException>(() => ModProject.Delete(ws, install, "red-spot", removeFromGame: true, new GameMods(_ => true)));

        Assert.Equal(TyrantErrorCode.GameRunning, ex.Code);
        Assert.True(Directory.Exists(mod.Dir));
        Assert.True(Directory.Exists(inGame));
    }

    [Fact]
    public void A_mod_folder_in_use_fails_with_a_clear_message_and_leaves_the_blender_projects()
    {
        var (game, install, ws, mod) = Setup();
        using var _ = game;
        var blender = BlenderProjects(ws);
        TestRecycleBin.Instance.RefuseUnder(mod.Dir);

        var ex = Assert.Throws<TyrantException>(() => ModProject.Delete(ws, install, "red-spot", removeFromGame: false, new GameMods()));

        Assert.Equal(TyrantErrorCode.DeleteFailed, ex.Code);
        Assert.Contains("open in another program", ex.Message);
        Assert.True(Directory.Exists(mod.Dir));
        Assert.True(Directory.Exists(blender));
    }

    [Fact]
    public void A_blender_folder_in_use_stays_and_is_reported()
    {
        var (game, install, ws, mod) = Setup();
        using var _ = game;
        var blender = BlenderProjects(ws);
        TestRecycleBin.Instance.RefuseUnder(blender);

        var removed = ModProject.Delete(ws, install, "red-spot", removeFromGame: false, new GameMods());

        Assert.False(Directory.Exists(mod.Dir));
        Assert.True(Directory.Exists(blender));
        Assert.Equal([mod.Dir], removed.Recycled);
        var kept = Assert.Single(removed.Kept);
        Assert.StartsWith(blender + ": ", kept);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("..")]
    [InlineData("../red-spot")]
    public void An_unknown_or_unsafe_id_is_refused(string id)
    {
        var (game, install, ws, mod) = Setup();
        using var _ = game;

        Assert.Equal(TyrantErrorCode.ModNotFound, Assert.Throws<TyrantException>(() => ModProject.Delete(ws, install, id, false, new GameMods())).Code);
        Assert.True(Directory.Exists(mod.Dir));
    }

    [Fact]
    public void A_mod_with_a_broken_mod_json_can_still_be_deleted()
    {
        var (game, install, ws, mod) = Setup();
        using var _ = game;
        File.WriteAllText(Path.Combine(mod.Dir, ModManifest.FileName), "{ not json");

        ModProject.Delete(ws, install, "red-spot", false, new GameMods());

        Assert.False(Directory.Exists(mod.Dir));
    }
}
