using System.IO.Compression;
using Tyrant.Core.Dumping;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Textures;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class TextureInstallTests
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

    private static string Installed(GameInstall install, string id) => Path.Combine(ModLoaderInstaller.ModsDir(install), id);

    [Fact]
    public void Install_writes_a_current_pack_and_the_pack_does_not_count_as_a_change()
    {
        var (game, install, ws, mod) = Setup();
        using var _ = game;
        var cooker = ModTextureCooker.For(ws, null, null);

        new GameMods().Install(install, mod, staging => cooker.CookForInstall(mod, staging));

        Assert.True(File.Exists(Path.Combine(Installed(install, mod.Id), TexturePack.FileName)));
        Assert.Equal(ModInstallState.Installed, new GameMods().StateOf(install, mod));
        Assert.Equal("current", new GameMods().List(install).Single().Cooked);
    }

    [Fact]
    public void A_failed_cook_leaves_the_installed_copy_as_it_was()
    {
        var (game, install, ws, mod) = Setup();
        using var _ = game;
        new GameMods().Install(install, mod);
        var before = Directory.GetFiles(Installed(install, mod.Id), "*", SearchOption.AllDirectories).Length;

        var error = Assert.Throws<TyrantException>(() => new GameMods().Install(install, mod, staging => throw new TyrantException(TyrantErrorCode.ModInvalid, "skins/blue/male_D.png could not be cooked")));

        Assert.Contains("male_D.png", error.Message);
        Assert.Equal(before, Directory.GetFiles(Installed(install, mod.Id), "*", SearchOption.AllDirectories).Length);
        Assert.False(Directory.Exists(Installed(install, mod.Id) + ".installing"));
    }

    [Fact]
    public void An_install_without_a_pack_or_with_an_edited_png_is_stale()
    {
        var (game, install, ws, mod) = Setup();
        using var _ = game;
        new GameMods().Install(install, mod); // as before this feature: no pack
        Assert.Equal("stale", new GameMods().List(install).Single().Cooked);

        var cooker = ModTextureCooker.For(ws, null, null);
        new GameMods().Install(install, mod, staging => cooker.WritePack(mod.Manifest, staging, staging));
        var png = Path.Combine(Installed(install, mod.Id), "skins", "blue", "female_D.png");
        var bytes = File.ReadAllBytes(png);
        bytes[^20] ^= 0x55; // same size, different tail
        File.WriteAllBytes(png, bytes);
        Assert.Equal("stale", CookedPacks.StateOf(Installed(install, mod.Id), mod.Manifest));
        Assert.Equal("none", CookedPacks.StateOf(Installed(install, mod.Id), new ModManifest { Id = "red-spot" }));
    }

    [Fact]
    public void Export_puts_the_pack_in_the_zip_and_import_leaves_it_out()
    {
        var (game, install, ws, mod) = Setup();
        using var _ = game;
        var cooker = ModTextureCooker.For(ws, null, null);
        var zip = Path.Combine(ws.Dir, "exports", "red-spot.zip");

        ModSharing.Export(mod, zip, "0.3.0", dir => cooker.WritePack(mod.Manifest, mod.Dir, dir));

        using (var archive = ZipFile.OpenRead(zip))
            Assert.Contains(archive.Entries, e => e.FullName == "UserData/Tyrant/Mods/red-spot/textures.tpack");
        Assert.False(File.Exists(Path.Combine(mod.Dir, TexturePack.FileName))); // the workspace keeps only PNGs

        var other = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), install);
        var imported = ModSharing.Import(other, zip, replace: false);
        Assert.False(File.Exists(Path.Combine(imported.Dir, TexturePack.FileName)));
    }

    [Fact]
    public void Compression_is_set_per_mod_and_per_texture_and_dropped_with_its_file()
    {
        var (game, _, ws, mod) = Setup();
        using var _ = game;

        mod.SetCompression("skins\\blue\\male_D.png", TextureCompression.Bc7);
        mod.SetCompression(null, TextureCompression.Auto);
        Assert.Equal(TextureCompression.Bc7, ModProject.Open(ws, mod.Id).Manifest.Textures.For("skins/blue/male_D.png"));
        Assert.Contains("skins/blue/male_D.png", ModProject.Open(ws, mod.Id).Manifest.Textures.Overrides.Keys); // '/' as in mod.json

        Assert.Equal(TyrantErrorCode.TargetNotFound, Assert.Throws<TyrantException>(() => mod.SetCompression("skins/blue/thumbnail.png", TextureCompression.Bc7)).Code);
        Assert.Throws<TyrantException>(() => mod.SetCompression(null, null));

        mod.SetCompression(null, TextureCompression.Bc7); // the override now equals the default: dropped
        Assert.Empty(ModProject.Open(ws, mod.Id).Manifest.Textures.Overrides);

        mod.SetCompression("skins/blue/male_pattern.png", TextureCompression.Auto);
        mod.SetSkinFile("blue", "male", "pattern", null); // the file is no longer a texture
        Assert.Empty(ModProject.Open(ws, mod.Id).Manifest.Textures.Overrides);
    }

    [Fact]
    public void Export_without_pngs_leaves_out_the_cooked_pngs_and_needs_framework_0_2_0()
    {
        var (game, install, ws, mod) = Setup();
        using var _ = game;
        var cooker = ModTextureCooker.For(ws, null, null);
        var zip = Path.Combine(ws.Dir, "exports", "slim.zip");

        ModSharing.Export(mod, zip, "0.2.0", dir => cooker.WritePack(mod.Manifest, mod.Dir, dir), includePngs: false);

        using var archive = ZipFile.OpenRead(zip);
        var names = archive.Entries.Select(e => e.FullName).ToList();
        Assert.DoesNotContain("UserData/Tyrant/Mods/red-spot/skins/blue/male_D.png", names);
        Assert.Contains("UserData/Tyrant/Mods/red-spot/textures.tpack", names);
        Assert.Contains("UserData/Tyrant/Mods/red-spot/skins/blue/thumbnail.auto.png", names);
        using var reader = new StreamReader(archive.GetEntry("UserData/Tyrant/Mods/red-spot/mod.json")!.Open());
        var shared = ModManifest.Parse(reader.ReadToEnd());
        Assert.Equal("skins/blue/thumbnail.auto.png", shared.Skins[0].Thumbnail);
        Assert.Equal("0.2.0", shared.RequiresTyrant);
    }

    [Fact]
    public void Export_with_pngs_is_the_whole_mod_and_its_pack()
    {
        var (game, install, ws, mod) = Setup();
        using var _ = game;
        var cooker = ModTextureCooker.For(ws, null, null);
        var zip = Path.Combine(ws.Dir, "exports", "full.zip");

        ModSharing.Export(mod, zip, "0.2.0", dir => cooker.WritePack(mod.Manifest, mod.Dir, dir), includePngs: true);

        using var archive = ZipFile.OpenRead(zip);
        var names = archive.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("UserData/Tyrant/Mods/red-spot/skins/blue/male_D.png", names);
        Assert.Contains("UserData/Tyrant/Mods/red-spot/textures.tpack", names);
        Assert.DoesNotContain(names, n => n.Contains("thumbnail.auto"));
    }

    [Fact]
    public void Importing_a_slim_zip_extracts_its_pngs_and_drops_the_auto_thumbnails()
    {
        var (game, install, ws, mod) = Setup();
        using var _ = game;
        var cooker = ModTextureCooker.For(ws, null, null);
        var zip = Path.Combine(ws.Dir, "exports", "slim.zip");
        ModSharing.Export(mod, zip, "0.2.0", dir => cooker.WritePack(mod.Manifest, mod.Dir, dir), includePngs: false);
        var other = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), install);

        var imported = ModSharing.ImportReported(other, zip, replace: false);

        Assert.Equal(3, imported.Extracted);
        Assert.Empty(imported.Warnings);
        Assert.True(File.Exists(Path.Combine(imported.Mod.Dir, "skins", "blue", "male_D.png")));
        Assert.False(File.Exists(Path.Combine(imported.Mod.Dir, TexturePack.FileName)));
        Assert.False(File.Exists(Path.Combine(imported.Mod.Dir, "skins", "blue", "thumbnail.auto.png")));
        Assert.Null(imported.Mod.Manifest.Skins[0].Thumbnail);
    }

    [Fact]
    public void A_png_missing_from_zip_and_pack_is_a_warning_not_a_failure()
    {
        var (game, install, ws, _) = Setup();
        using var _g = game;
        var zip = Path.Combine(ws.Dir, "broken.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("UserData/Tyrant/Mods/lost/mod.json").Open()))
            writer.Write("""{"format":1,"id":"lost","name":"Lost","version":"1.0.0","replace":[],"skins":[{"id":"aaa","species":"S","name":"A","male":{"diffuse":"skins/aaa/male_D.png"}}]}""");

        var imported = ModSharing.ImportReported(ws, zip, replace: false);

        Assert.Equal(0, imported.Extracted);
        Assert.Equal(["skins/aaa/male_D.png is not in the zip or its cooked pack"], imported.Warnings);
    }

    [Fact]
    public void A_mod_only_in_the_game_can_be_copied_into_the_workspace_with_its_pngs()
    {
        var (game, install, ws, mod) = Setup();
        using var _ = game;
        new GameMods().Install(install, mod, staging => ModTextureCooker.For(ws, null, null).CookForInstall(mod, staging));
        var other = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), install);

        var copied = ModSharing.CopyFromGame(other, install, "red-spot", replace: false);

        Assert.Equal(3, copied.Extracted);
        Assert.True(File.Exists(Path.Combine(copied.Mod.Dir, "skins", "blue", "female_D.png")));
        Assert.Null(copied.Mod.Manifest.Skins[0].Thumbnail);
        var missing = Assert.Throws<TyrantException>(() => ModSharing.CopyFromGame(other, install, "nope", false));
        Assert.Equal(TyrantErrorCode.TargetNotFound, missing.Code);
    }
}
