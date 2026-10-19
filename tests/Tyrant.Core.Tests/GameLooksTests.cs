using System.Text.Json;
using Tyrant.Core.Dumping;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class GameLooksTests
{
    private const string Species = "materials/species/Carcharodontosaurus";
    private const string Vanilla = "materials/vanilla-skins/Carcharodontosaurus-Alt 1";

    private static (FakeGame Game, GameInstall Install, Workspace Ws) Setup()
    {
        var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), install);
        // What ModLoaderInstaller leaves behind: the framework DLL and the install record.
        Directory.CreateDirectory(Path.Combine(game.Root, "Mods"));
        File.WriteAllText(Path.Combine(game.Root, "Mods", ModLoaderInstaller.FrameworkFile), "framework");
        Directory.CreateDirectory(ModLoaderInstaller.RecordDir(install));
        File.WriteAllText(Path.Combine(ModLoaderInstaller.RecordDir(install), "install.json"), """{"installedLoader":true,"files":[],"createdDirs":[]}""");
        return (game, install, ws);
    }

    /// <summary>A materials.json for <paramref name="folder"/> (a species or a vanilla skin's), as the in-game editor writes it.</summary>
    internal static void WriteMaterials(string modDir, string folder, string strength, string? mask = null)
    {
        var dir = Path.Combine(modDir, folder.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(dir);
        var identity = folder == Vanilla ? "\"species\":\"Carcharodontosaurus\",\"skin\":\"Alt 1\"" : "\"species\":\"Carcharodontosaurus\"";
        var maskJson = mask is null ? "" : $",\"mask\":\"{folder}/{mask}\"";
        File.WriteAllText(Path.Combine(dir, MaterialFolders.FileName),
            $"{{\"format\":1,{identity},\"materials\":{{\"Carch\":{{\"iridescence\":{{\"strength\":{strength}{maskJson}}}}}}}}}\n");
    }

    private static ModProject ModWithMaterials(Workspace ws, params string[] folders)
    {
        var mod = ModProject.Create(ws, "red-spot", "Red spot", null);
        foreach (var folder in folders) WriteMaterials(mod.Dir, folder, "0.5");
        return ModProject.Open(ws, mod.Id);
    }

    private static string Installed(GameInstall install, string id = "red-spot") => Path.Combine(ModLoaderInstaller.ModsDir(install), id);

    [Fact]
    public void Install_records_stamps_outside_the_mod_folder()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        var mod = ModWithMaterials(ws, Species);

        new GameMods().Install(install, mod);

        var stamps = Path.Combine(ModLoaderInstaller.RecordDir(install), "install-stamps.json");
        using var json = JsonDocument.Parse(File.ReadAllText(stamps));
        Assert.Equal(1, json.RootElement.GetProperty("format").GetInt32());
        Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("mods").GetProperty("red-spot").GetProperty(Species).GetString()));
        Assert.Equal(ModInstallState.Installed, new GameMods().StateOf(install, mod));
        Assert.Empty(GameLooks.Changes(install, mod));
        Assert.DoesNotContain(Directory.GetFiles(Installed(install), "*", SearchOption.AllDirectories), f => f.EndsWith("install-stamps.json", StringComparison.Ordinal));
    }

    [Fact]
    public void An_install_whose_stamps_cannot_be_written_still_goes_in_with_a_warning()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        var mod = ModWithMaterials(ws, Species);
        Directory.CreateDirectory(Path.Combine(ModLoaderInstaller.RecordDir(install), "install-stamps.json")); // in the way: no file can go there

        var warnings = new GameMods().Install(install, mod);

        Assert.Contains("could not record", Assert.Single(warnings));
        Assert.True(File.Exists(Path.Combine(Installed(install), Species, MaterialFolders.FileName)));
        Assert.Equal(ModInstallState.Installed, new GameMods().StateOf(install, mod));
    }

    [Fact]
    public void Removing_a_mod_forgets_its_stamps()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        var mod = ModWithMaterials(ws, Species);
        new GameMods().Install(install, mod);

        new GameMods().Remove(install, mod.Id);

        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(ModLoaderInstaller.RecordDir(install), "install-stamps.json")));
        Assert.False(json.RootElement.GetProperty("mods").TryGetProperty("red-spot", out var _));
    }

    [Fact]
    public void Game_written_materials_show_as_game_changed()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        var mod = ModWithMaterials(ws, Species);
        new GameMods().Install(install, mod);

        WriteMaterials(Installed(install), Species, "0.9"); // edited in game
        WriteMaterials(Installed(install), Vanilla, "0.7"); // a new owner, saved in game: never stamped

        Assert.Equal([new LooksChange(Species, true, false), new LooksChange(Vanilla, true, false)], GameLooks.Changes(install, mod));
    }

    [Fact]
    public void Both_sides_changed_is_reported()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        var mod = ModWithMaterials(ws, Species, Vanilla);
        new GameMods().Install(install, mod);

        WriteMaterials(Installed(install), Species, "0.9");
        WriteMaterials(mod.Dir, Species, "0.1");
        WriteMaterials(mod.Dir, Vanilla, "0.2"); // only the project

        Assert.Equal([new LooksChange(Species, true, true), new LooksChange(Vanilla, false, true)], GameLooks.Changes(install, mod));
    }

    [Fact]
    public void Bring_in_replaces_only_the_chosen_material_folders()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        var mod = ModWithMaterials(ws, Species, Vanilla);
        new GameMods().Install(install, mod);
        WriteMaterials(Installed(install), Species, "0.9", mask: "iridescence-mask.png");
        ModEditTests.Png(Path.Combine(Installed(install), Species.Replace('/', Path.DirectorySeparatorChar)), "iridescence-mask.png");
        WriteMaterials(Installed(install), Vanilla, "0.7");
        var manifest = File.ReadAllText(Path.Combine(mod.Dir, ModManifest.FileName));

        var brought = GameLooks.BringIn(install, mod, [Species]);

        Assert.Equal(1, brought);
        var folder = Path.Combine(mod.Dir, Species.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(File.ReadAllText(Path.Combine(Installed(install), Species, MaterialFolders.FileName)), File.ReadAllText(Path.Combine(folder, MaterialFolders.FileName)));
        Assert.True(File.Exists(Path.Combine(folder, "iridescence-mask.png")));
        Assert.Contains("0.5", File.ReadAllText(Path.Combine(mod.Dir, Vanilla, MaterialFolders.FileName))); // not chosen: the project's stays
        Assert.Equal(manifest, File.ReadAllText(Path.Combine(mod.Dir, ModManifest.FileName)));
        Assert.Equal(0.9, mod.Manifest.SpeciesMaterials["Carcharodontosaurus"]["Carch"].Iridescence!.Strength!.Normal, 3); // the project's materials were read again
        Assert.Equal([new LooksChange(Vanilla, true, false)], GameLooks.Changes(install, mod));
    }

    [Fact]
    public void Bring_in_keeps_the_masks_a_slim_install_left_out()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        var mod = ModProject.Create(ws, "red-spot", "Red spot", null);
        WriteMaterials(mod.Dir, Species, "0.5", mask: "iridescence-mask.png");
        ModEditTests.Png(Path.Combine(mod.Dir, Species.Replace('/', Path.DirectorySeparatorChar)), "iridescence-mask.png");
        mod = ModProject.Open(ws, mod.Id);
        var cooker = Tyrant.Core.Textures.ModTextureCooker.For(ws, null, null);
        new GameMods().Install(install, mod, staging => cooker.CookForInstall(mod, staging));
        Assert.False(File.Exists(Path.Combine(Installed(install), Species, "iridescence-mask.png"))); // in the pack instead
        Assert.Empty(GameLooks.Changes(install, mod));

        WriteMaterials(Installed(install), Species, "0.9", mask: "iridescence-mask.png");
        Assert.Equal([new LooksChange(Species, true, false)], GameLooks.Changes(install, mod));
        GameLooks.BringIn(install, mod, [Species]);

        Assert.True(File.Exists(Path.Combine(mod.Dir, Species, "iridescence-mask.png")));
        Assert.Contains("0.9", File.ReadAllText(Path.Combine(mod.Dir, Species, MaterialFolders.FileName)));
        Assert.Empty(GameLooks.Changes(install, mod));
    }

    [Fact]
    public void Bring_in_refuses_a_game_folder_with_an_invalid_materials_json_and_changes_nothing()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        var mod = ModWithMaterials(ws, Species, Vanilla);
        new GameMods().Install(install, mod);
        WriteMaterials(Installed(install), Vanilla, "0.7"); // fine on its own
        var bad = Path.Combine(Installed(install), Species.Replace('/', Path.DirectorySeparatorChar), MaterialFolders.FileName);
        File.WriteAllText(bad, """{"format":1,"species":"Carcharodontosaurus","materials":{"Carch":{"settings":{"_DroopDirection":[0,0,-30]}}}}""");
        var project = File.ReadAllText(Path.Combine(mod.Dir, Species, MaterialFolders.FileName));
        var vanilla = File.ReadAllText(Path.Combine(mod.Dir, Vanilla, MaterialFolders.FileName));
        var stamps = File.ReadAllText(GameLooks.StampPath(install));

        var ex = Assert.Throws<Tyrant.Core.Errors.TyrantException>(() => GameLooks.BringIn(install, mod, [Vanilla, Species]));

        Assert.Equal(Tyrant.Core.Errors.TyrantErrorCode.ModInvalid, ex.Code);
        Assert.Contains(Species, ex.Message);
        Assert.Contains("_DroopDirection", ex.Message);
        Assert.Equal(project, File.ReadAllText(Path.Combine(mod.Dir, Species, MaterialFolders.FileName)));
        Assert.Equal(vanilla, File.ReadAllText(Path.Combine(mod.Dir, Vanilla, MaterialFolders.FileName))); // the good one was not brought in either
        Assert.Equal(stamps, File.ReadAllText(GameLooks.StampPath(install)));
        Assert.Equal(0.5, mod.Manifest.SpeciesMaterials["Carcharodontosaurus"]["Carch"].Iridescence!.Strength!.Normal, 3);
    }

    [Fact]
    public void A_mod_copied_from_the_game_is_stamped_so_later_game_edits_show_on_the_game_side_only()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        // A mod made in the game (Vanilla Looks): only in the game's mods folder.
        var installed = Installed(install, ModCatalog.VanillaLooksId);
        Directory.CreateDirectory(installed);
        File.WriteAllText(Path.Combine(installed, ModManifest.FileName), $"{{\"format\":1,\"id\":\"{ModCatalog.VanillaLooksId}\",\"name\":\"Vanilla Looks\"}}");
        WriteMaterials(installed, Species, "0.5");

        var copied = ModSharing.CopyFromGame(ws, install, ModCatalog.VanillaLooksId, replace: false).Mod;
        Assert.Empty(GameLooks.Changes(install, copied));

        WriteMaterials(installed, Species, "0.8"); // the in-game editor saves again

        Assert.Equal([new LooksChange(Species, true, false)], GameLooks.Changes(install, copied));
    }

    [Fact]
    public void Bring_in_refuses_a_folder_that_is_not_a_material_folder()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        var mod = ModWithMaterials(ws, Species);
        new GameMods().Install(install, mod);

        foreach (var bad in new[] { "textures", "materials/species", "materials/species/../../x", "materials/other/x" })
            Assert.Equal(Tyrant.Core.Errors.TyrantErrorCode.ModInvalid, Assert.Throws<Tyrant.Core.Errors.TyrantException>(() => GameLooks.BringIn(install, mod, [bad])).Code);
    }

    [Fact]
    public void A_mod_installed_before_stamps_shows_only_folders_that_differ()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        var mod = ModWithMaterials(ws, Species);
        new GameMods().Install(install, mod);
        File.Delete(Path.Combine(ModLoaderInstaller.RecordDir(install), "install-stamps.json")); // installed by an older Tyrant

        Assert.Empty(GameLooks.Changes(install, mod)); // the same as the project: nothing to bring in
        WriteMaterials(Installed(install), Species, "0.9");
        Assert.Equal([new LooksChange(Species, true, true)], GameLooks.Changes(install, mod)); // who changed it is unknown
    }

    [Fact]
    public void A_mod_not_in_the_game_has_no_changes()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        Assert.Empty(GameLooks.Changes(install, ModWithMaterials(ws, Species)));
    }
}
