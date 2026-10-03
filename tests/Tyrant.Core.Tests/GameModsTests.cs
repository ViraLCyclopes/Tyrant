using Tyrant.Core.Dumping;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class GameModsTests
{
    private static (FakeGame Game, GameInstall Install, Workspace Ws) Setup(bool framework = true)
    {
        var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), install);
        if (framework)
        {
            // What ModLoaderInstaller leaves behind: the framework DLL and the install record.
            Directory.CreateDirectory(Path.Combine(game.Root, "Mods"));
            File.WriteAllText(Path.Combine(game.Root, "Mods", ModLoaderInstaller.FrameworkFile), "framework");
            Directory.CreateDirectory(ModLoaderInstaller.RecordDir(install));
            File.WriteAllText(Path.Combine(ModLoaderInstaller.RecordDir(install), "install.json"), """{"installedLoader":true,"files":[],"createdDirs":[]}""");
        }
        return (game, install, ws);
    }

    private static ModProject Mod(Workspace ws, string id, params string[] files)
    {
        var mod = ModProject.Create(ws, id, null, null);
        foreach (var file in files)
        {
            Directory.CreateDirectory(Path.Combine(mod.Dir, "textures"));
            File.WriteAllText(Path.Combine(mod.Dir, "textures", file), file);
            mod.Manifest.Replace.Add(new TextureReplacement { Texture = Path.GetFileNameWithoutExtension(file), File = "textures/" + file });
        }
        mod.Save();
        return mod;
    }

    private static string[] ListedIds(GameInstall install) => ModList.Parse(File.ReadAllText(GameMods.ListPath(install))).Select(e => e.Id).ToArray();

    [Fact]
    public void Install_copies_the_mod_into_user_data_and_turns_it_on()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        var mod = Mod(ws, "red-spot", "T_A_D.png");

        new GameMods().Install(install, mod);

        var dir = Path.Combine(ModLoaderInstaller.ModsDir(install), "red-spot");
        Assert.True(File.Exists(Path.Combine(dir, "mod.json")));
        Assert.True(File.Exists(Path.Combine(dir, "textures", "T_A_D.png")));
        var installed = Assert.Single(new GameMods().List(install));
        Assert.Equal(("red-spot", true, 1, null as string), (installed.Id, installed.Enabled, installed.Replacements, installed.Error));
        Assert.Equal(ModInstallState.Installed, new GameMods().StateOf(install, mod));
    }

    [Fact]
    public void Install_without_the_framework_is_refused()
    {
        var (game, install, ws) = Setup(framework: false);
        using var _ = game;

        var ex = Assert.Throws<TyrantException>(() => new GameMods().Install(install, Mod(ws, "red-spot")));

        Assert.Equal(TyrantErrorCode.FrameworkMissing, ex.Code);
    }

    [Fact]
    public void Install_and_remove_refuse_while_the_game_runs()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        var mod = Mod(ws, "red-spot", "T_A_D.png");
        new GameMods().Install(install, mod);
        var running = new GameMods(_ => true);

        Assert.Equal(TyrantErrorCode.GameRunning, Assert.Throws<TyrantException>(() => running.Install(install, mod)).Code);
        Assert.Equal(TyrantErrorCode.GameRunning, Assert.Throws<TyrantException>(() => running.Remove(install, "red-spot")).Code);
        Assert.True(Directory.Exists(Path.Combine(ModLoaderInstaller.ModsDir(install), "red-spot")));
    }

    [Fact]
    public void Reinstalling_replaces_the_files_and_keeps_the_load_order()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        var red = Mod(ws, "red-spot", "T_A_D.png");
        var mods = new GameMods();
        mods.Install(install, red);
        mods.Install(install, Mod(ws, "blue", "T_B_D.png"));
        File.Delete(Path.Combine(red.Dir, "textures", "T_A_D.png"));
        File.WriteAllText(Path.Combine(red.Dir, "textures", "T_C_D.png"), "c");
        Assert.Equal(ModInstallState.Changed, mods.StateOf(install, red));

        mods.Install(install, red);

        var dir = Path.Combine(ModLoaderInstaller.ModsDir(install), "red-spot", "textures");
        Assert.False(File.Exists(Path.Combine(dir, "T_A_D.png")));
        Assert.True(File.Exists(Path.Combine(dir, "T_C_D.png")));
        Assert.Equal(new[] { "red-spot", "blue" }, ListedIds(install));
        Assert.Equal(ModInstallState.Installed, mods.StateOf(install, red));
    }

    [Fact]
    public void Disable_enable_and_remove_update_the_mods_list()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        var mods = new GameMods();
        mods.Install(install, Mod(ws, "red-spot"));

        mods.SetEnabled(install, "red-spot", false);
        var off = Assert.Single(mods.List(install)).Enabled;
        mods.Remove(install, "red-spot");

        Assert.False(off);
        Assert.Empty(mods.List(install));
        Assert.Empty(ListedIds(install));
        Assert.Equal(TyrantErrorCode.ModNotFound, Assert.Throws<TyrantException>(() => mods.Remove(install, "red-spot")).Code);
    }

    [Fact]
    public void A_broken_installed_mod_is_listed_with_its_problem()
    {
        var (game, install, _) = Setup();
        using var _ = game;
        var dir = Path.Combine(ModLoaderInstaller.ModsDir(install), "hand-made");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "mod.json"), "{ nope");

        var listed = Assert.Single(new GameMods().List(install));

        Assert.Equal("hand-made", listed.Id);
        Assert.Contains("not valid JSON", listed.Error);
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("a/b")]
    [InlineData(@"..\..")]
    public void Remove_and_enable_refuse_ids_that_are_not_a_plain_folder_name(string id)
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        var mods = new GameMods();
        mods.Install(install, Mod(ws, "red-spot"));

        Assert.Equal(TyrantErrorCode.ModIdInvalid, Assert.Throws<TyrantException>(() => mods.Remove(install, id)).Code);
        Assert.Equal(TyrantErrorCode.ModIdInvalid, Assert.Throws<TyrantException>(() => mods.SetEnabled(install, id, false)).Code);
        Assert.True(File.Exists(Path.Combine(ModLoaderInstaller.RecordDir(install), "install.json"))); // nothing outside the mod was touched
        Assert.True(Directory.Exists(Path.Combine(ModLoaderInstaller.ModsDir(install), "red-spot")));
    }

    [Fact]
    public void Remove_refuses_an_absolute_path()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        var mod = Mod(ws, "red-spot");

        var ex = Assert.Throws<TyrantException>(() => new GameMods().Remove(install, mod.Dir)); // e.g. a folder dragged into the terminal

        Assert.Equal(TyrantErrorCode.ModIdInvalid, ex.Code);
        Assert.True(Directory.Exists(mod.Dir));
    }

    [Fact]
    public void An_unreadable_installed_mod_is_listed_with_its_problem_instead_of_failing_the_list()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        new GameMods().Install(install, Mod(ws, "red-spot"));
        var path = Path.Combine(ModLoaderInstaller.ModsDir(install), "red-spot", "mod.json");

        IReadOnlyList<InstalledMod> listed;
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
            listed = new GameMods().List(install);

        Assert.NotNull(Assert.Single(listed).Error);
    }
}
