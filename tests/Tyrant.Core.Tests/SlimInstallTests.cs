using Tyrant.Core.Dumping;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Textures;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class SlimInstallTests
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
        return (game, install, ws, ModEditTests.WithSkins(ws, ModProject.Create(ws, "red-spot", "Red spot", null)));
    }

    private static string Installed(GameInstall install, params string[] parts) => Path.Combine([ModLoaderInstaller.ModsDir(install), "red-spot", .. parts]);

    private static void InstallSlim(GameInstall install, Workspace ws, ModProject mod) =>
        new GameMods().Install(install, mod, staging => ModTextureCooker.For(ws, null, null).CookForInstall(mod, staging));

    [Fact]
    public void Install_leaves_out_the_cooked_pngs_and_adds_auto_thumbnails()
    {
        var (game, install, ws, mod) = Setup();
        using var _ = game;

        InstallSlim(install, ws, mod);

        Assert.False(File.Exists(Installed(install, "skins", "blue", "male_D.png")));
        Assert.False(File.Exists(Installed(install, "skins", "blue", "male_pattern.png")));
        Assert.True(File.Exists(Installed(install, TexturePack.FileName)));
        Assert.True(File.Exists(Installed(install, "skins", "blue", "thumbnail.auto.png")));
        Assert.Equal("skins/blue/thumbnail.auto.png", ModManifest.Parse(File.ReadAllText(Installed(install, "mod.json"))).Skins[0].Thumbnail);
        Assert.True(File.Exists(Path.Combine(mod.Dir, "skins", "blue", "male_D.png")));
        Assert.Null(ModManifest.Parse(File.ReadAllText(Path.Combine(mod.Dir, "mod.json"))).Skins[0].Thumbnail);
    }

    [Fact]
    public void A_slim_install_is_installed_until_a_png_a_setting_or_another_file_changes()
    {
        var (game, install, ws, mod) = Setup();
        using var _ = game;
        InstallSlim(install, ws, mod);
        Assert.Equal(ModInstallState.Installed, new GameMods().StateOf(install, mod));

        var png = Path.Combine(mod.Dir, "skins", "blue", "female_D.png");
        var bytes = File.ReadAllBytes(png);
        bytes[^20] ^= 0x55;
        File.WriteAllBytes(png, bytes);
        Assert.Equal(ModInstallState.Changed, new GameMods().StateOf(install, mod));

        InstallSlim(install, ws, mod);
        mod.SetCompression(null, TextureCompression.Bc7);
        Assert.Equal(ModInstallState.Changed, new GameMods().StateOf(install, mod));

        InstallSlim(install, ws, mod);
        mod.RenameSkin("blue", "Ocean blue");
        Assert.Equal(ModInstallState.Changed, new GameMods().StateOf(install, mod));
    }

    [Fact]
    public void An_old_install_with_its_pngs_is_changed_until_installed_again()
    {
        var (game, install, ws, mod) = Setup();
        using var _ = game;
        var cooker = ModTextureCooker.For(ws, null, null);
        new GameMods().Install(install, mod, staging => cooker.WritePack(mod.Manifest, staging, staging)); // as before: PNGs + pack

        Assert.Equal(ModInstallState.Changed, new GameMods().StateOf(install, mod));
        InstallSlim(install, ws, mod);
        Assert.Equal(ModInstallState.Installed, new GameMods().StateOf(install, mod));
    }

    [Fact]
    public void An_install_without_a_pack_compares_file_by_file_as_before()
    {
        var (game, install, _, mod) = Setup();
        using var _g = game;
        new GameMods().Install(install, mod); // no prepare step: no pack, every PNG

        Assert.Equal(ModInstallState.Installed, new GameMods().StateOf(install, mod));
    }

    [Fact]
    public void A_mod_with_nothing_to_cook_installs_whole()
    {
        var (game, install, ws, mod) = Setup();
        using var _g = game;
        mod.Manifest.Skins.Clear();
        File.WriteAllText(Path.Combine(mod.Dir, "notes.png"), "kept"); // named by nothing, copied as today
        mod.Save();

        InstallSlim(install, ws, mod);

        Assert.True(File.Exists(Installed(install, "notes.png")));
        Assert.False(File.Exists(Installed(install, TexturePack.FileName)));
        Assert.Equal(ModInstallState.Installed, new GameMods().StateOf(install, mod));
    }
}
