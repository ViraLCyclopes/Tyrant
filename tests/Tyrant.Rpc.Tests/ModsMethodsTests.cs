using System.Text.Json;
using Tyrant.Core.Assets;
using Tyrant.Core.Dumping;
using Tyrant.Core.Install;
using Tyrant.Core.Tests;

namespace Tyrant.Rpc.Tests;

public class ModsMethodsTests
{
    private static readonly AssetRecord Texture = new("StandaloneWindows64/carch_assets_assets/t.bundle", 7, "Texture2D",
        "T_carcharodontosaurus_alt1_male_D", "Assets/Art/T_carcharodontosaurus_alt1_male_D.png", "e3583acd2b3b5b14c875f42d110d97ce", null);

    private static async Task<(RpcHarness H, string Ws)> Opened(FakeGame game, bool withLoaderZip = false)
    {
        var options = TestStudio.Options(loaderZip: withLoaderZip ? TestStudio.FakeMelonLoaderZip() : null, dumperDir: TestStudio.FakeGameModsDir());
        var h = new RpcHarness(options);
        var ws = TestStudio.TempDir();
        await h.Call("workspace.create", new { dir = ws, gamePath = game.Root });
        AssetFixtures.WriteIndex(ws, [Texture], GameFingerprint.Compute(new GameInstall(game.Root, null)));
        return (h, ws);
    }

    private static string Png(string ws)
    {
        var path = Path.Combine(ws, "spots.png");
        using var stream = File.Create(path);
        new StbImageWriteSharp.ImageWriter().WritePng(new byte[4 * 4 * 4], 4, 4, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        return path;
    }

    private static JsonElement Row(JsonElement list, string id) => list.GetProperty("mods").EnumerateArray().Single(m => m.GetProperty("id").GetString() == id);

    [Fact]
    public async Task Create_and_replace_show_up_in_the_list()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game);

        await h.Call("mods.create", new { id = "red-spot", name = "Red spot" });
        var list = await h.Call("mods.replace", new { id = "red-spot", texture = Texture.Name, png = Png(ws) });

        var row = Row(list, "red-spot");
        Assert.Equal(("Red spot", 1, "notInstalled"), (row.GetProperty("name").GetString(), row.GetProperty("replacements").GetInt32(), row.GetProperty("state").GetString()));
        Assert.False(list.GetProperty("frameworkInstalled").GetBoolean());
    }

    [Fact]
    public async Task Check_reports_errors_and_warnings()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game);
        await h.Call("mods.create", new { id = "red-spot" });
        await h.Call("mods.replace", new { id = "red-spot", texture = Texture.Name, png = Png(ws) });
        File.Delete(Path.Combine(ws, "mods", "red-spot", "textures", "T_carcharodontosaurus_alt1_male_D.png"));

        var report = await h.Call("mods.check", new { id = "red-spot" });

        Assert.Contains("missing", report.GetProperty("errors")[0].GetString());
    }

    [Fact]
    public async Task Installing_a_mod_installs_the_framework_first()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game, withLoaderZip: true);
        await h.Call("mods.create", new { id = "red-spot" });
        await h.Call("mods.replace", new { id = "red-spot", texture = Texture.Name, png = Png(ws) });

        var result = await h.RunJob("mods.install", new { id = "red-spot" });
        var list = await h.Call("mods.list");

        Assert.Contains("Installed 'red-spot'", result.GetProperty("message").GetString());
        Assert.True(File.Exists(Path.Combine(game.Root, "Mods", "Tyrant.Framework.dll")));
        Assert.True(File.Exists(Path.Combine(game.Root, "UserData", "Tyrant", "Mods", "red-spot", "mod.json")));
        Assert.True(list.GetProperty("frameworkInstalled").GetBoolean());
        Assert.Equal(("installed", true), (Row(list, "red-spot").GetProperty("state").GetString(), Row(list, "red-spot").GetProperty("enabled").GetBoolean()));
    }

    [Fact]
    public async Task The_mods_list_says_when_the_games_framework_is_out_of_date()
    {
        using var game = new FakeGame();
        var (h, _) = await Opened(game, withLoaderZip: true);
        await h.RunJob("dump.install");
        Assert.False((await h.Call("mods.list")).GetProperty("frameworkOutdated").GetBoolean());
        File.WriteAllText(Path.Combine(game.Root, "Mods", "Tyrant.Framework.dll"), "an older framework");

        var list = await h.Call("mods.list");

        Assert.True(list.GetProperty("frameworkOutdated").GetBoolean());
    }

    [Fact]
    public async Task Export_writes_the_zip_and_import_brings_it_back()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game);
        await h.Call("mods.create", new { id = "red-spot" });
        await h.Call("mods.replace", new { id = "red-spot", texture = Texture.Name, png = Png(ws) });

        var zip = (await h.RunJob("mods.export", new { id = "red-spot" })).GetProperty("path").GetString()!;
        var again = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.import", new { file = zip }));
        var imported = await h.Call("mods.import", new { file = zip, replace = true });

        Assert.True(File.Exists(zip));
        Assert.EndsWith("red-spot-1.0.0.zip", zip);
        Assert.Equal("MOD_EXISTS", again.DataCode);
        Assert.Equal("red-spot", imported.GetProperty("id").GetString());
        Assert.Contains(imported.GetProperty("mods").GetProperty("mods").EnumerateArray(), m => m.GetProperty("id").GetString() == "red-spot");
    }

    [Fact]
    public async Task A_mod_with_problems_is_not_exported()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game);
        await h.Call("mods.create", new { id = "red-spot" });
        await h.Call("mods.replace", new { id = "red-spot", texture = Texture.Name, png = Png(ws) });
        File.Delete(Path.Combine(ws, "mods", "red-spot", "textures", "T_carcharodontosaurus_alt1_male_D.png"));

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.RunJob("mods.export", new { id = "red-spot" }));

        Assert.Equal("MOD_INVALID", ex.DataCode);
        Assert.False(Directory.Exists(Path.Combine(ws, "exports")) && Directory.GetFiles(Path.Combine(ws, "exports"), "*.zip").Length > 0);
    }

    [Fact]
    public async Task The_framework_zip_is_saved_in_exports()
    {
        using var game = new FakeGame();
        var (h, _) = await Opened(game);

        var path = (await h.Call("game.packageFramework", new { })).GetProperty("path").GetString()!;

        Assert.True(File.Exists(path));
        Assert.EndsWith($"Tyrant-Framework-{Tyrant.Framework.Core.FrameworkInfo.Version}.zip", path);
    }

    [Fact]
    public async Task A_mod_with_errors_is_not_installed()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game, withLoaderZip: true);
        await h.Call("mods.create", new { id = "red-spot" });
        await h.Call("mods.replace", new { id = "red-spot", texture = Texture.Name, png = Png(ws) });
        File.Delete(Path.Combine(ws, "mods", "red-spot", "textures", "T_carcharodontosaurus_alt1_male_D.png"));

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.RunJob("mods.install", new { id = "red-spot" }));

        Assert.Equal("MOD_INVALID", ex.DataCode);
        Assert.False(Directory.Exists(Path.Combine(game.Root, "UserData", "Tyrant", "Mods", "red-spot")));
    }

    [Fact]
    public async Task Turning_off_and_removing_update_the_list()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game, withLoaderZip: true);
        await h.Call("mods.create", new { id = "red-spot" });
        await h.Call("mods.replace", new { id = "red-spot", texture = Texture.Name, png = Png(ws) });
        await h.RunJob("mods.install", new { id = "red-spot" });

        var off = await h.Call("mods.enable", new { id = "red-spot", enabled = false });
        var removed = await h.Call("mods.remove", new { id = "red-spot" });

        Assert.False(Row(off, "red-spot").GetProperty("enabled").GetBoolean());
        Assert.Equal("notInstalled", Row(removed, "red-spot").GetProperty("state").GetString());
    }

    [Fact]
    public async Task Looks_changes_and_bring_in_round_trip()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game, withLoaderZip: true);
        await h.Call("mods.create", new { id = "red-spot" });
        await h.Call("mods.replace", new { id = "red-spot", texture = Texture.Name, png = Png(ws) });
        await h.RunJob("mods.install", new { id = "red-spot" });
        const string folder = "materials/species/Carcharodontosaurus";
        Assert.False(Row(await h.Call("mods.list"), "red-spot").GetProperty("gameLooks").GetBoolean());
        var saved = Path.Combine(game.Root, "UserData", "Tyrant", "Mods", "red-spot", "materials", "species", "Carcharodontosaurus");
        Directory.CreateDirectory(saved); // what the in-game editor saves
        File.WriteAllText(Path.Combine(saved, "materials.json"), """{"format":1,"species":"Carcharodontosaurus","materials":{"Carch":{"iridescence":{"strength":0.9}}}}""");

        var listed = await h.Call("mods.list");
        var changes = await h.Call("mods.looksChanges", new { id = "red-spot" });
        var detail = await h.Call("mods.bringInLooks", new { id = "red-spot", folders = new[] { folder } });
        var after = await h.Call("mods.looksChanges", new { id = "red-spot" });

        Assert.True(Row(listed, "red-spot").GetProperty("gameLooks").GetBoolean());
        var change = Assert.Single(changes.GetProperty("changes").EnumerateArray());
        Assert.Equal((folder, true, false), (change.GetProperty("folder").GetString(), change.GetProperty("gameChanged").GetBoolean(), change.GetProperty("projectChanged").GetBoolean()));
        Assert.Contains("0.9", detail.GetProperty("materialFiles").GetProperty(folder + "/materials.json").GetString());
        Assert.Empty(after.GetProperty("changes").EnumerateArray());
        Assert.False(Row(await h.Call("mods.list"), "red-spot").GetProperty("gameLooks").GetBoolean());
    }

    [Fact]
    public async Task Installing_a_mod_updates_an_outdated_framework()
    {
        using var game = new FakeGame();
        var gameMods = TestStudio.FakeGameModsDir();
        var options = TestStudio.Options(loaderZip: TestStudio.FakeMelonLoaderZip(), dumperDir: gameMods);
        var h = new RpcHarness(options);
        var ws = TestStudio.TempDir();
        await h.Call("workspace.create", new { dir = ws, gamePath = game.Root });
        AssetFixtures.WriteIndex(ws, [Texture], GameFingerprint.Compute(new GameInstall(game.Root, null)));
        await h.Call("mods.create", new { id = "red-spot" });
        await h.Call("mods.replace", new { id = "red-spot", texture = Texture.Name, png = Png(ws) });
        await h.RunJob("mods.install", new { id = "red-spot" });
        File.WriteAllText(Path.Combine(gameMods, "Tyrant.Framework.dll"), "framework v2");
        Assert.Equal("outdated", (await h.Call("workspace.status")).GetProperty("framework").GetString());

        await h.RunJob("mods.install", new { id = "red-spot" });

        Assert.Equal("framework v2", File.ReadAllText(Path.Combine(game.Root, "Mods", "Tyrant.Framework.dll")));
        Assert.Equal("current", (await h.Call("workspace.status")).GetProperty("framework").GetString());
    }

    [Fact]
    public async Task mods_species_reports_no_dump()
    {
        using var game = new FakeGame();
        var (h, _) = await Opened(game);

        var result = await h.Call("mods.species");

        Assert.False(result.GetProperty("hasDump").GetBoolean());
        Assert.Equal(0, result.GetProperty("species").GetArrayLength());
    }

    [Fact]
    public async Task Species_and_add_skin_work_from_the_dump()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game);
        SkinDumps.Write(Path.Combine(ws, "data"));
        AssetFixtures.WriteIndex(ws, SkinDumps.Textures, GameFingerprint.Compute(new GameInstall(game.Root, null)));
        await h.Call("mods.create", new { id = "red-spot" });

        var species = await h.Call("mods.species");
        var list = await h.Call("mods.addSkin", new { id = "red-spot", species = "Carcharodontosaurus", name = "Red spot", @base = "Alt 1", male = true, female = false, maps = false });

        var carch = species.GetProperty("species").EnumerateArray().Single(s => s.GetProperty("speciesId").GetString() == "Carcharodontosaurus");
        Assert.Equal(new[] { "Base", "Alt 1" }, carch.GetProperty("skins").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        Assert.Equal(1, Row(list, "red-spot").GetProperty("skins").GetInt32());
        Assert.True(File.Exists(Path.Combine(ws, "mods", "red-spot", "skins", "red-spot", "male_D.png")));
    }

    [Fact]
    public async Task Skin_numbers_of_removed_mods_can_be_listed_and_forgotten()
    {
        using var game = new FakeGame();
        var (h, _) = await Opened(game);
        var dir = Path.Combine(game.Root, "UserData", "Tyrant");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "skin-slots.json"), """{ "format": 1, "species": { "Carcharodontosaurus": { "gone-mod/blue": 3 } } }""");

        var before = await h.Call("mods.skinSlots");
        var after = await h.Call("mods.forgetSkins", new { keys = new[] { "gone-mod/blue" } });

        Assert.Equal("gone-mod/blue", before.GetProperty("orphans")[0].GetProperty("key").GetString());
        Assert.Equal(0, after.GetProperty("orphans").GetArrayLength());
    }

    [Fact]
    public async Task Check_reports_missing_cutouts_and_restore_cutouts_answers_with_the_fixed_files()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game);
        await h.Call("mods.create", new { id = "red-spot" });
        var check = await h.Call("mods.check", new { id = "red-spot" });
        var restoredNothing = await h.Call("mods.restoreCutouts", new { id = "red-spot" });
        File.Delete(Path.Combine(ws, "cache", "asset-index.json"));

        var withoutIndex = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.restoreCutouts", new { id = "red-spot" }));

        Assert.Equal(0, check.GetProperty("missingCutouts").GetArrayLength());
        Assert.Equal(0, restoredNothing.GetProperty("restored").GetArrayLength());
        Assert.Equal(0, restoredNothing.GetProperty("problems").GetArrayLength());
        Assert.Equal("ASSET_INDEX_MISSING", withoutIndex.DataCode); // not a silent "nothing to restore"
    }

    private sealed class AlwaysRunning : IGameLauncher
    {
        public bool IsRunning(GameInstall install) => true;
        public void Launch(GameInstall install) { }
    }

    [Fact]
    public async Task Delete_recycles_the_mod_and_removes_it_from_the_game_when_asked()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game);
        await h.Call("mods.create", new { id = "red-spot" });
        await h.Call("mods.create", new { id = "blue-pack" });
        var inGame = Path.Combine(ModLoaderInstaller.ModsDir(new GameInstall(game.Root, null)), "red-spot");
        Directory.CreateDirectory(inGame);
        File.WriteAllText(Path.Combine(inGame, "mod.json"), "{\"format\":1,\"id\":\"red-spot\",\"name\":\"Red spot\",\"version\":\"1.0.0\",\"replace\":[]}");

        var list = await h.Call("mods.delete", new { id = "red-spot", removeFromGame = true });

        Assert.Equal(["blue-pack"], list.GetProperty("mods").EnumerateArray().Select(m => m.GetProperty("id").GetString()));
        Assert.False(Directory.Exists(Path.Combine(ws, "mods", "red-spot")));
        Assert.False(Directory.Exists(inGame));
    }

    [Fact]
    public async Task Delete_is_refused_while_the_game_runs()
    {
        using var game = new FakeGame();
        var h = new RpcHarness(TestStudio.Options(launcher: new AlwaysRunning(), dumperDir: TestStudio.FakeGameModsDir()));
        var ws = TestStudio.TempDir();
        await h.Call("workspace.create", new { dir = ws, gamePath = game.Root });
        await h.Call("mods.create", new { id = "red-spot" });

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.delete", new { id = "red-spot", removeFromGame = true }));

        Assert.Equal("GAME_RUNNING", ex.DataCode);
        Assert.True(Directory.Exists(Path.Combine(ws, "mods", "red-spot")));
    }

    [Fact]
    public async Task Add_skin_can_put_the_new_skin_in_a_group()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game);
        SkinDumps.Write(Path.Combine(ws, "data"));
        AssetFixtures.WriteIndex(ws, SkinDumps.Textures, GameFingerprint.Compute(new GameInstall(game.Root, null)));
        await h.Call("mods.create", new { id = "red-spot" });

        await h.Call("mods.addSkin", new { id = "red-spot", species = "Carcharodontosaurus", name = "Red spot", @base = "Alt 1", male = true, female = false, maps = false, group = "Jurassic Park" });
        await h.Call("mods.addSkin", new { id = "red-spot", species = "Carcharodontosaurus", name = "Tan", @base = "Alt 1", male = true, female = false, maps = false });

        var skins = (await h.Call("mods.get", new { id = "red-spot" })).GetProperty("skins").EnumerateArray()
            .Select(s => (s.GetProperty("id").GetString(), s.GetProperty("group").GetString())).ToArray();
        Assert.Equal(new (string?, string?)[] { ("tan", null), ("red-spot", "Jurassic Park") }, skins);
    }
}
