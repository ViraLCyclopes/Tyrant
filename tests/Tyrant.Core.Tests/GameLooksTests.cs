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
