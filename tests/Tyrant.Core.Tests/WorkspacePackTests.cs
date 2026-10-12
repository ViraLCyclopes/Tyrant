using Tyrant.Core.Dumping;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Textures;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

/// <summary>textures.tpack in the workspace's mod folder: written by Cook textures and by install, with its own up-to-date check.</summary>
public class WorkspacePackTests
{
    private static (FakeGame Game, GameInstall Install, Workspace Ws, ModProject Mod) Setup()
    {
        var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), install);
        Directory.CreateDirectory(Path.Combine(game.Root, "Mods"));
        File.WriteAllText(Path.Combine(game.Root, "Mods", ModLoaderInstaller.FrameworkFile), "framework");
        Directory.CreateDirectory(ModLoaderInstaller.RecordDir(install));
        File.WriteAllText(Path.Combine(ModLoaderInstaller.RecordDir(install), "install.json"), """{"installedLoader":true,"files":[],"createdDirs":[]}""");
        var mod = ModEditTests.WithSkins(ws, ModProject.Create(ws, "red-spot", "Red spot", null));
        return (game, install, ws, mod);
    }

    private static string WorkspacePack(ModProject mod) => Path.Combine(mod.Dir, TexturePack.FileName);

    private static string State(ModTextureCooker cooker, ModProject mod) => cooker.PackState(mod.Manifest, mod.Dir);

    [Fact]
    public void Cooking_writes_the_pack_into_the_workspace_folder_and_it_is_then_up_to_date()
    {
        var (game, _, ws, mod) = Setup();
        using var _g = game;
        var cooker = ModTextureCooker.For(ws, null, null);
        Assert.Equal("missing", State(cooker, mod));

        var path = cooker.CookInPlace(mod);

        Assert.Equal(WorkspacePack(mod), path);
        using (var stream = File.OpenRead(path!))
            Assert.Equal(["skins/blue/female_D.png", "skins/blue/male_D.png", "skins/blue/male_pattern.png"], TexturePack.Read(stream).Entries.Select(e => e.Source).Order().ToArray());
        Assert.Equal("current", State(cooker, mod));
    }

    [Fact]
    public void Install_cooks_into_the_workspace_and_gives_the_game_the_same_pack()
    {
        var (game, install, ws, mod) = Setup();
        using var _g = game;
        var cooker = ModTextureCooker.For(ws, null, null);

        new GameMods().Install(install, mod, staging => cooker.CookForInstall(mod, staging));

        var installed = Path.Combine(ModLoaderInstaller.ModsDir(install), mod.Id, TexturePack.FileName);
        Assert.Equal(File.ReadAllBytes(WorkspacePack(mod)), File.ReadAllBytes(installed));
        Assert.Equal("current", new GameMods().List(install).Single().Cooked);
        Assert.Equal(ModInstallState.Installed, new GameMods().StateOf(install, mod)); // the workspace pack is not a change to install
    }

    [Fact]
    public void An_old_pack_goes_from_both_places_once_there_is_nothing_to_cook()
    {
        var (game, install, ws, mod) = Setup();
        using var _g = game;
        var cooker = ModTextureCooker.For(ws, null, null);
        cooker.CookInPlace(mod);
        mod.Manifest.Skins.Clear();
        mod.Save();

        new GameMods().Install(install, mod, staging => cooker.CookForInstall(mod, staging));

        Assert.False(File.Exists(WorkspacePack(mod)));
        Assert.False(File.Exists(Path.Combine(ModLoaderInstaller.ModsDir(install), mod.Id, TexturePack.FileName)));
        Assert.Equal("none", State(cooker, mod));
    }

    [Fact]
    public void The_pack_is_out_of_date_after_a_png_or_the_compression_changes()
    {
        var (game, _, ws, mod) = Setup();
        using var _g = game;
        var cooker = ModTextureCooker.For(ws, null, null);
        cooker.CookInPlace(mod);

        var png = Path.Combine(mod.Dir, "skins", "blue", "female_D.png");
        var bytes = File.ReadAllBytes(png);
        bytes[^20] ^= 0x55; // same size, different tail
        File.WriteAllBytes(png, bytes);
        Assert.Equal("stale", State(cooker, mod));

        cooker.CookInPlace(mod);
        Assert.Equal("current", State(cooker, mod));
        mod.SetCompression(null, TextureCompression.Bc7);
        Assert.Equal("stale", State(cooker, mod)); // the same PNGs, but they would cook to another format

        File.WriteAllText(WorkspacePack(mod), "not a pack");
        Assert.Equal("stale", State(cooker, mod));
    }
}
