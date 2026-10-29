using Tyrant.Cli;
using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Tests;

namespace Tyrant.Cli.Tests;

public class SpeciesCliTests : IDisposable
{
    public void Dispose() => CliServices.AssetReader = new BundleAssetReader();

    /// <summary>
    /// An indexed workspace whose dump has Allosaurus Anax (skins Default, Albino) and Carcharodontosaurus (Base, Alt 1), every
    /// skin but Carch's Base wearing the SkinDumps textures, read by a fake asset reader; mod-a exists.
    /// </summary>
    private static string TexturedAllosaurus(FakeGame game, bool behaviour = false)
    {
        var ws = IndexedWorkspace(game);
        var objects = new Tyrant.Dumper.Serialization.DumpResult();
        void Add(string type, string name, long id, string json) =>
            objects.Objects.Add(new Tyrant.Dumper.Serialization.DumpObject(typeof(object), new Tyrant.Dumper.Serialization.EngineObjectInfo(type, name, id), json));
        const string Var = "PrehistoricKingdom.AnimalSkinVariationAsset";
        var textured = "\"maleVariationData\":{\"$ref\":{\"type\":\"" + Var + "\",\"name\":\"A_M\",\"id\":10}},"
            + "\"femaleVariationData\":{\"$ref\":{\"type\":\"" + Var + "\",\"name\":\"A_F\",\"id\":11}}";
        const string Ai = "PrehistoricKingdom.AnimalsV2.AI.";
        var brain = behaviour ? "\"brain\":{\"$ref\":{\"type\":\"" + Ai + "AnimalBrain\",\"name\":\"Allo\",\"id\":2}}," : "";
        Add("PrehistoricKingdom.AnimalData", "Allosaurus", 1,
            "{\"speciesID\":\"Allosaurus Anax\", " + brain + "\"skinsData\":[{\"skinName\":\"Default\"," + textured + "},{\"skinName\":\"Albino\"," + textured + "}]}");
        Add("PrehistoricKingdom.AnimalData", "Carcharodontosaurus", 2,
            "{\"speciesID\":\"Carcharodontosaurus\", \"skinsData\":[{\"skinName\":\"Base\"},{\"skinName\":\"Alt 1\"," + textured + "}]}");
        if (behaviour)
        {
            string Link(string rule, int id) => "{\"asset\":{\"$ref\":{\"type\":\"" + Ai + "AggressionProfileAsset\",\"name\":\"" + rule + "\",\"id\":" + id + "}},\"inlineProfile\":{}}";
            Add(Ai + "AnimalBrain", "Allo", 2, "{\"combat\":{\"shared\":{\"combatProfile\":{\"$ref\":{\"type\":\"" + Ai + "AnimalCombatProfile\",\"name\":\"CombatProfile_PredatorApex\",\"id\":3}}}}}");
            Add(Ai + "AnimalCombatProfile", "CombatProfile_PredatorApex", 3, "{\"confidenceLevel\":\"Confident\",\"temperament\":\"Aggressive\",\"cannibalism\":\"None\",\"aggressionProfileEntries\":["
                + Link("Aggression_Predation", 4) + "," + Link("Aggression_NestDefense", 5) + "]}");
            Add(Ai + "AnimalCombatProfile", "CombatProfile_Deer", 6, "{\"confidenceLevel\":\"Timid\",\"temperament\":\"Passive\",\"cannibalism\":\"WhenStarving\",\"aggressionProfileEntries\":[" + Link("Aggression_PreyFleeFromPredator", 7) + "]}");
        }
        Add(Var, "A_M", 10, "{\"$type\":\"x\",\"$name\":\"A_M\",\"$id\":10,\"adultDiffuseMap\":{\"m_AssetGUID\":\"" + SkinDumps.MaleDiffuse
            + "\"},\"adultNormalMap\":{\"m_AssetGUID\":\"" + SkinDumps.MaleNormal + "\"}}");
        Add(Var, "A_F", 11, "{\"$type\":\"x\",\"$name\":\"A_F\",\"$id\":11,\"adultDiffuseMap\":{\"m_AssetGUID\":\"" + SkinDumps.FemaleDiffuse + "\"}}");
        Tyrant.Dumper.Serialization.DumpWriter.Write(Path.Combine(ws, "data"), objects, [], new Tyrant.Dumper.Serialization.DumpManifest { RequestId = "r1", BuildGuid = "b" });
        new AssetIndex { Assets = [.. SkinDumps.Textures], Fingerprint = GameFingerprint.Compute(new GameInstall(game.Root, null)) }
            .Save(Path.Combine(ws, "cache", AssetIndex.FileName));
        CliServices.AssetReader = new FakeAssetReader();
        Assert.Equal(ExitCodes.Ok, Run("mod", "new", "mod-a", "-w", ws).Code);
        return ws;
    }

    private static Tyrant.Framework.Core.SpeciesBehaviour? StoredBehaviour(string ws) =>
        Tyrant.Framework.Core.AnimalDatabaseFile.Read(Path.Combine(ws, "mods", "mod-a", Tyrant.Framework.Core.AnimalDatabaseFile.FileName)).Single().Behaviour;

    const string ProfileRow = "/brain/combat/shared/combatProfile/";

    /// <summary>The stored record's confidence, temperament and cannibalism.</summary>
    private static string[] ProfileValues(string ws)
    {
        var fields = Tyrant.Framework.Core.AnimalDatabaseFile.Read(Path.Combine(ws, "mods", "mod-a", Tyrant.Framework.Core.AnimalDatabaseFile.FileName)).Single().Fields;
        return new[] { "confidenceLevel", "temperament", "cannibalism" }.Select(n => fields.Single(f => f.Path == ProfileRow + n).Value).ToArray();
    }

    private static string ModJson(string ws) => File.ReadAllText(Path.Combine(ws, "mods", "mod-a", "mod.json"));

    private static (int Code, string Out, string Err) AddMirabilis(string ws, params string[] extra) =>
        Run(["mod", "add-species", "mod-a", "mirabilis", "Allosaurus Anax", "--name", "Mirabilis", "--genus", "E", "--epithet", "m", .. extra, "-w", ws]);

    [Fact]
    public void Add_species_gives_the_species_its_starting_skin()
    {
        using var game = new FakeGame();
        var ws = TexturedAllosaurus(game);

        var (code, output, err) = AddMirabilis(ws);

        Assert.True(code == ExitCodes.Ok, err);
        Assert.Contains("Starting skin: mirabilis-start (Default).", output);
        Assert.Contains("\"starting\": true", ModJson(ws));
        Assert.DoesNotContain("copiedFrom", ModJson(ws));
    }

    [Fact]
    public void Start_from_another_species_records_where_it_came_from()
    {
        using var game = new FakeGame();
        var ws = TexturedAllosaurus(game);

        var (code, output, err) = AddMirabilis(ws, "--start-from", "Carcharodontosaurus/Alt 1", "--start-name", "Carch look");

        Assert.True(code == ExitCodes.Ok, err);
        Assert.Contains("Starting skin: mirabilis-start (Carch look).", output);
        Assert.Contains("copiedFrom", ModJson(ws));
    }

    [Fact]
    public void Copying_and_own_pngs_together_are_refused()
    {
        using var game = new FakeGame();
        var ws = TexturedAllosaurus(game);

        var (code, _, err) = AddMirabilis(ws, "--start-from", "Carcharodontosaurus/1", "--start-png", Path.Combine(ws, "x.png"));

        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("Copy a skin or use your own PNGs, not both.", err);
    }

    [Fact]
    public void A_species_without_a_starting_skin_gets_one_and_removing_it_says_what_happens()
    {
        using var game = new FakeGame();
        var ws = TexturedAllosaurus(game);
        Assert.Equal(ExitCodes.Ok, AddMirabilis(ws).Code);

        var removed = Run("mod", "remove-skin", "mod-a", "mirabilis-start", "--delete-files", "-w", ws);
        Assert.Contains("tyrant.mod-a.mirabilis will start with its template's game skin (Default) instead.", removed.Out);

        var (code, output, err) = Run("mod", "add-starting-skin", "mod-a", "mirabilis", "--start-from", "Allosaurus Anax/1", "-w", ws);
        Assert.True(code == ExitCodes.Ok, err);
        Assert.Contains("Starting skin: mirabilis-start (Albino).", output);
    }

    [Fact]
    public void Remove_species_takes_its_skins_and_files_and_warns_about_mods_that_use_it()
    {
        using var game = new FakeGame();
        var ws = TexturedAllosaurus(game);
        Assert.Equal(ExitCodes.Ok, AddMirabilis(ws).Code);
        var fans = Tyrant.Core.Mods.ModProject.Create(Tyrant.Core.Workspaces.Workspace.Open(ws), "fans", null, null);
        fans.Manifest.Skins.Add(new Tyrant.Framework.Core.SkinEntry { Id = "fan", Species = "tyrant.mod-a.mirabilis", Name = "Fan", Male = new() { ["diffuse"] = "skins/fan/male_D.png" } });
        fans.Save();

        var (code, output, err) = Run("mod", "remove-species", "mod-a", "mirabilis", "-w", ws);

        Assert.True(code == ExitCodes.Ok, err);
        Assert.Contains("Removed species tyrant.mod-a.mirabilis (Mirabilis) and 1 skin(s).", output);
        Assert.Contains("fans still has 1 skin(s), 0 model(s), 0 sound(s) for it; the game skips them.", output);
        Assert.Contains("Species (0):", Run("mod", "show", "mod-a", "-w", ws).Out);
        Assert.False(Directory.Exists(Path.Combine(ws, "mods", "mod-a", "skins", "mirabilis-start")));
    }

    [Fact]
    public void Remove_species_with_keep_files_leaves_its_files()
    {
        using var game = new FakeGame();
        var ws = TexturedAllosaurus(game);
        Assert.Equal(ExitCodes.Ok, AddMirabilis(ws).Code);

        Assert.Equal(ExitCodes.Ok, Run("mod", "remove-species", "mod-a", "mirabilis", "--keep-files", "-w", ws).Code);

        Assert.True(Directory.Exists(Path.Combine(ws, "mods", "mod-a", "skins", "mirabilis-start")));
        Assert.Contains("Species (0):", Run("mod", "show", "mod-a", "-w", ws).Out);
    }

    [Fact]
    public void Set_species_skin_points_a_new_species_at_its_skin_commands()
    {
        using var game = new FakeGame();
        var ws = TexturedAllosaurus(game);
        Assert.Equal(ExitCodes.Ok, AddMirabilis(ws).Code);

        var (code, _, err) = Run("mod", "set-species-skin", "mod-a", "mirabilis", "--name", "Ghost", "-w", ws);

        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("mirabilis has its own starting skin 'mirabilis-start': rename it with 'mod rename-skin' and set its swatch with 'mod thumbnail'.", err);
    }

    [Fact]
    public void Set_starting_skin_makes_another_skin_the_default()
    {
        using var game = new FakeGame();
        var ws = TexturedAllosaurus(game);
        Assert.Equal(ExitCodes.Ok, AddMirabilis(ws).Code);
        var mod = Tyrant.Core.Mods.ModProject.Open(Tyrant.Core.Workspaces.Workspace.Open(ws), "mod-a"); // add-skin needs game textures the fake game lacks
        var start = mod.Manifest.Skins.Single(s => s.Starting);
        mod.Manifest.Skins.Add(new Tyrant.Framework.Core.SkinEntry { Id = "brown", Species = "tyrant.mod-a.mirabilis", Name = "Brown", Male = new() { ["diffuse"] = start.Male!["diffuse"] } });
        mod.Save();

        var (code, output, err) = Run("mod", "set-starting-skin", "mod-a", "brown", "-w", ws);

        Assert.True(code == ExitCodes.Ok, err);
        Assert.Contains("Brown is now the starting skin of tyrant.mod-a.mirabilis; Default (mirabilis-start) is an ordinary skin.", output);
        Assert.Contains("Default took its place in the skin picker.", output);
        Assert.Equal(ExitCodes.Error, Run("mod", "set-starting-skin", "mod-a", "brown", "-w", ws).Code);
    }

    [Fact]
    public void Skin_order_lists_the_shown_order()
    {
        using var game = new FakeGame();
        var ws = TexturedAllosaurus(game);
        Assert.Equal(ExitCodes.Ok, AddMirabilis(ws).Code);
        var mod = Tyrant.Core.Mods.ModProject.Open(Tyrant.Core.Workspaces.Workspace.Open(ws), "mod-a");
        var start = mod.Manifest.Skins.Single(s => s.Starting);
        mod.Manifest.Skins.Add(new Tyrant.Framework.Core.SkinEntry { Id = "brown", Species = "tyrant.mod-a.mirabilis", Name = "Brown", Male = new() { ["diffuse"] = start.Male!["diffuse"] } });
        mod.Save();

        var (code, output, err) = Run("mod", "skin-order", "mod-a", "mirabilis", "-w", ws);

        Assert.True(code == ExitCodes.Ok, err);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal("1. Default (mirabilis-start) \u00b7 starting skin", lines[0]);
        Assert.Equal("2. Brown (brown)", lines[1]);
    }

    [Fact]
    public void Set_skin_order_saves_it()
    {
        using var game = new FakeGame();
        var ws = TexturedAllosaurus(game);
        Assert.Equal(ExitCodes.Ok, AddMirabilis(ws).Code);
        var mod = Tyrant.Core.Mods.ModProject.Open(Tyrant.Core.Workspaces.Workspace.Open(ws), "mod-a");
        var start = mod.Manifest.Skins.Single(s => s.Starting);
        mod.Manifest.Skins.Add(new Tyrant.Framework.Core.SkinEntry { Id = "brown", Species = "tyrant.mod-a.mirabilis", Name = "Brown", Male = new() { ["diffuse"] = start.Male!["diffuse"] } });
        mod.Save();

        var (code, output, err) = Run("mod", "set-skin-order", "mod-a", "mirabilis", "brown", "mirabilis-start", "-w", ws);

        Assert.True(code == ExitCodes.Ok, err);
        Assert.Contains("1. Brown (brown)", output);
        Assert.Contains("2. Default (mirabilis-start) \u00b7 starting skin", output);
        var again = Run("mod", "skin-order", "mod-a", "mirabilis", "-w", ws);
        Assert.Contains("1. Brown (brown)", again.Out);
        Assert.Equal(ExitCodes.Error, Run("mod", "set-skin-order", "mod-a", "mirabilis", "brown", "-w", ws).Code);
    }

    [Fact]
    public void Set_skin_order_refuses_a_species_without_a_starting_skin()
    {
        using var game = new FakeGame();
        var ws = TexturedAllosaurus(game);
        Assert.Equal(ExitCodes.Ok, AddMirabilis(ws).Code);
        var mod = Tyrant.Core.Mods.ModProject.Open(Tyrant.Core.Workspaces.Workspace.Open(ws), "mod-a");
        mod.Manifest.Skins.RemoveAll(s => s.Starting);
        mod.Save();

        var set = Run("mod", "set-skin-order", "mod-a", "mirabilis", "x", "-w", ws);
        var show = Run("mod", "skin-order", "mod-a", "mirabilis", "-w", ws);

        Assert.Equal(ExitCodes.Error, set.Code);
        Assert.Equal(ExitCodes.Error, show.Code);
        Assert.Contains("needs an editable starting skin first", set.Err);
        Assert.Contains("needs an editable starting skin first", show.Err);
    }

    [Fact]
    public void Set_species_photo_sets_and_clears_the_nursery_picture()
    {
        using var game = new FakeGame();
        var ws = TexturedAllosaurus(game);
        Assert.Equal(ExitCodes.Ok, AddMirabilis(ws).Code);
        var png = Path.Combine(ws, "photo.png");
        File.WriteAllBytes(png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jWWQAAAAASUVORK5CYII="));

        var (code, output, err) = Run("mod", "set-species-photo", "mod-a", "mirabilis", "--png", png, "-w", ws);

        Assert.True(code == ExitCodes.Ok, err);
        Assert.Contains("mirabilis: Nursery picture <- icons/mirabilis.png", output);
        Assert.True(File.Exists(Path.Combine(ws, "mods", "mod-a", "icons", "mirabilis.png")));

        var cleared = Run("mod", "set-species-photo", "mod-a", "mirabilis", "--clear", "-w", ws);
        Assert.Contains("mirabilis uses its template's Nursery picture.", cleared.Out);
        Assert.Equal(ExitCodes.Error, Run("mod", "set-species-photo", "mod-a", "mirabilis", "-w", ws).Code);

        var alias = Run("mod", "set-species-nursery-picture", "mod-a", "mirabilis", "--png", png, "-w", ws);
        Assert.True(alias.Code == ExitCodes.Ok, alias.Err);
        Assert.Contains("mirabilis: Nursery picture <- icons/mirabilis.png", alias.Out);
    }

    [Fact]
    public void Set_species_text_writes_english_and_translations_and_a_cleared_translation_uses_english()
    {
        using var game = new FakeGame();
        var ws = TexturedAllosaurus(game);
        Assert.Equal(ExitCodes.Ok, AddMirabilis(ws).Code);
        var file = Path.Combine(ws, "notes.txt");
        File.WriteAllText(file, "Line one.\n\nLine two.");
        string Text(string code, string slot) => File.ReadAllText(Path.Combine(ws, "mods", "mod-a", "localization", code, "mirabilis", slot + ".txt"));

        var english = Run("mod", "set-species-text", "mod-a", "mirabilis", "description", "--text", "A big one.", "-w", ws);
        Assert.True(english.Code == ExitCodes.Ok, english.Err);
        Assert.Contains("mirabilis: en-US description set (every empty language uses it).", english.Out);
        Assert.Equal("A big one.", Text("en-US", "description"));

        var french = Run("mod", "set-species-text", "mod-a", "mirabilis", "characterNotes", "--lang", "fr", "--file", file, "-w", ws);
        Assert.True(french.Code == ExitCodes.Ok, french.Err);
        Assert.Equal("Line one.\n\nLine two.", Text("fr", "characterNotes"));

        var cleared = Run("mod", "set-species-text", "mod-a", "mirabilis", "characterNotes", "--lang", "fr", "--english", "-w", ws);
        Assert.Contains("mirabilis: fr characterNotes uses English.", cleared.Out);
        Assert.Equal("", Text("fr", "characterNotes"));

        Assert.Equal(ExitCodes.Error, Run("mod", "set-species-text", "mod-a", "mirabilis", "description", "-w", ws).Code);
        Assert.Equal(ExitCodes.Error, Run("mod", "set-species-text", "mod-a", "mirabilis", "description", "--text", "a", "--file", file, "-w", ws).Code);
        Assert.Equal(ExitCodes.Error, Run("mod", "set-species-text", "mod-a", "mirabilis", "description", "--english", "-w", ws).Code);
        var englishName = Run("mod", "set-species-text", "mod-a", "mirabilis", "name", "--text", "X", "-w", ws);
        Assert.NotEqual(ExitCodes.Ok, englishName.Code);
        Assert.Contains("Display name", englishName.Err);
    }

    [Fact]
    public void Set_species_paleopedia_picture_sets_and_clears_the_paleopedia_page_picture()
    {
        using var game = new FakeGame();
        var ws = TexturedAllosaurus(game);
        Assert.Equal(ExitCodes.Ok, AddMirabilis(ws).Code);
        var png = Path.Combine(ws, "page.png");
        File.WriteAllBytes(png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jWWQAAAAASUVORK5CYII="));

        var (code, output, err) = Run("mod", "set-species-paleopedia-picture", "mod-a", "mirabilis", "--png", png, "-w", ws);

        Assert.True(code == ExitCodes.Ok, err);
        Assert.Contains("mirabilis: Paleopedia picture <- icons/mirabilis-paleopedia.png", output);
        Assert.True(File.Exists(Path.Combine(ws, "mods", "mod-a", "icons", "mirabilis-paleopedia.png")));
        Assert.False(File.Exists(Path.Combine(ws, "mods", "mod-a", "icons", "mirabilis.png"))); // the Nursery picture is separate

        var cleared = Run("mod", "set-species-paleopedia-picture", "mod-a", "mirabilis", "--clear", "-w", ws);
        Assert.Contains("mirabilis uses its template's Paleopedia picture.", cleared.Out);
        Assert.Equal(ExitCodes.Error, Run("mod", "set-species-paleopedia-picture", "mod-a", "mirabilis", "-w", ws).Code);
        Assert.Equal(ExitCodes.Error, Run("mod", "set-species-paleopedia-picture", "mod-a", "mirabilis", "--png", png, "--clear", "-w", ws).Code);
    }

    [Fact]
    public void Set_species_behaviour_starts_from_a_profile_and_adds_a_rule()
    {
        using var game = new FakeGame();
        var ws = TexturedAllosaurus(game, behaviour: true);
        Assert.Equal(ExitCodes.Ok, AddMirabilis(ws).Code);

        var (code, output, err) = Run("mod", "set-species-behaviour", "mod-a", "mirabilis", "--start-from", "deer", "--add-rule", "Flees with its family", "-w", ws);

        Assert.True(code == ExitCodes.Ok, err);
        Assert.Contains("tyrant.mod-a.mirabilis: behaviour from Deer, 2 rules", output);
        var stored = StoredBehaviour(ws)!;
        Assert.Equal("CombatProfile_Deer", stored.StartFrom);
        Assert.Equal(["Aggression_PreyFleeFromPredator", "Aggression_PreyFleeWithFamily"], stored.Rules);
        Assert.Equal(["Timid", "Passive", "WhenStarving"], ProfileValues(ws)); // what the game starts from, as the Behaviour card shows

        var removed = Run("mod", "set-species-behaviour", "mod-a", "mirabilis", "--remove-rule", "PreyFleeFromPredator", "-w", ws);
        Assert.True(removed.Code == ExitCodes.Ok, removed.Err);
        Assert.Contains("behaviour from Deer, 1 rules", removed.Out);
        Assert.Equal(["Timid", "Passive", "WhenStarving"], ProfileValues(ws));
    }

    [Fact]
    public void Reset_returns_to_the_template()
    {
        using var game = new FakeGame();
        var ws = TexturedAllosaurus(game, behaviour: true);
        Assert.Equal(ExitCodes.Ok, AddMirabilis(ws).Code);
        Assert.Equal(ExitCodes.Ok, Run("mod", "set-species-behaviour", "mod-a", "mirabilis", "--start-from", "Deer", "-w", ws).Code);
        Assert.Equal("CombatProfile_Deer", StoredBehaviour(ws)!.StartFrom);
        Assert.Equal(["Timid", "Passive", "WhenStarving"], ProfileValues(ws));

        var (code, output, err) = Run("mod", "set-species-behaviour", "mod-a", "mirabilis", "--reset", "-w", ws);

        Assert.True(code == ExitCodes.Ok, err);
        Assert.Contains("tyrant.mod-a.mirabilis: template behaviour", output);
        Assert.Null(StoredBehaviour(ws));
        Assert.Equal(["Confident", "Aggressive", "None"], ProfileValues(ws));
        Assert.Equal(ExitCodes.Error, Run("mod", "set-species-behaviour", "mod-a", "mirabilis", "--reset", "--start-from", "Deer", "-w", ws).Code);
    }

    [Fact]
    public void Unknown_rule_fails_with_the_text()
    {
        using var game = new FakeGame();
        var ws = TexturedAllosaurus(game, behaviour: true);
        Assert.Equal(ExitCodes.Ok, AddMirabilis(ws).Code);

        var (code, _, err) = Run("mod", "set-species-behaviour", "mod-a", "mirabilis", "--add-rule", "Dances wildly", "-w", ws);

        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("Dances wildly", err);
        Assert.Null(StoredBehaviour(ws));
    }

    [Fact]
    public void Species_behaviour_list_shows_profiles_and_rules_and_needs_a_dump_to_reset()
    {
        using var game = new FakeGame();
        var ws = TexturedAllosaurus(game);
        Assert.Equal(ExitCodes.Ok, AddMirabilis(ws).Code);

        var empty = Run("mod", "set-species-behaviour", "mod-a", "mirabilis", "--list", "-w", ws);
        Assert.Equal(ExitCodes.Ok, empty.Code);
        Assert.Contains("game data dump", empty.Out);
        var reset = Run("mod", "set-species-behaviour", "mod-a", "mirabilis", "--reset", "-w", ws);
        Assert.Equal(ExitCodes.Error, reset.Code);
        Assert.Contains("game data dump", reset.Err);

        var withDump = TexturedAllosaurus(game, behaviour: true);
        var listed = Run("mod", "set-species-behaviour", "mod-a", "mirabilis", "--list", "-w", withDump);
        Assert.Contains("Apex predator", listed.Out);
        Assert.Contains("Hunts prey", listed.Out);
    }

    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var (oldOut, oldErr) = (Console.Out, Console.Error);
        var (o, e) = (new StringWriter(), new StringWriter());
        Console.SetOut(o);
        Console.SetError(e);
        try { return (CliApp.Run(args), o.ToString(), e.ToString()); }
        finally { Console.SetOut(oldOut); Console.SetError(oldErr); }
    }

    private static string IndexedWorkspace(FakeGame game)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws");
        Assert.Equal(ExitCodes.Ok, Run("workspace", "init", dir, "--game", game.Root).Code);
        Assert.Equal(ExitCodes.Ok, Run("assets", "index", "-w", dir).Code);
        return dir;
    }

    [Fact]
    public void Species_stats_prints_a_templates_cost_and_stats_from_the_dump()
    {
        using var game = new FakeGame();
        var ws = IndexedWorkspace(game);
        var objects = new Tyrant.Dumper.Serialization.DumpResult();
        objects.Objects.Add(new Tyrant.Dumper.Serialization.DumpObject(typeof(object),
            new Tyrant.Dumper.Serialization.EngineObjectInfo("PrehistoricKingdom.AnimalData", "Allosaurus", 1),
            """{"speciesID":"Allosaurus Anax", "cost":20000, "massAverageAdult":4010, "matureMaxAge":{"x":15,"y":40}}"""));
        Tyrant.Dumper.Serialization.DumpWriter.Write(Path.Combine(ws, "data"), objects, [], new Tyrant.Dumper.Serialization.DumpManifest { RequestId = "r1", BuildGuid = "b" });

        var (code, output, _) = Run("species", "stats", "Allosaurus Anax", "-w", ws);
        var (missing, _, err) = Run("species", "stats", "Nope", "-w", ws);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("Purchase cost: 20000", output);
        Assert.Contains("Adult mass (kg): 4010", output);
        Assert.Contains("Lifespan (years): 40", output);
        Assert.Equal(ExitCodes.Error, missing);
        Assert.Contains("tyrant species list", err);
    }

    [Fact]
    public void Species_list_on_install_without_animals_says_so()
    {
        using var game = new FakeGame();
        var (code, output, _) = Run("species", "list", "-w", IndexedWorkspace(game));
        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("No species", output);
    }

    [Fact]
    public void Species_pack_unknown_name_reports_not_found()
    {
        using var game = new FakeGame();
        var (code, _, err) = Run("species", "pack", "stegosaurus", "-w", IndexedWorkspace(game));
        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("ASSET_NOT_FOUND", err);
    }

    [Fact]
    public void Export_model_unknown_key_reports_not_found()
    {
        using var game = new FakeGame();
        var (code, _, err) = Run("assets", "export-model", "nope", "-w", IndexedWorkspace(game));
        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("ASSET_NOT_FOUND", err);
    }

    [Theory]
    [InlineData("species", "pack", "stegosaurus")]
    [InlineData("assets", "export-model", "nope")]
    public void Fbx_without_blender_says_fbx_needs_blender_before_anything_else(string group, string command, string key)
    {
        using var game = new FakeGame();
        var (code, _, err) = Run(group, command, key, "-w", IndexedWorkspace(game), "--format", "fbx");
        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("BLENDER_MISSING", err);
        Assert.Contains("FBX needs Blender", err);
    }

    [Fact]
    public void An_unknown_format_is_refused()
    {
        using var game = new FakeGame();
        var (code, _, err) = Run("assets", "export-model", "nope", "-w", IndexedWorkspace(game), "--format", "obj");
        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("glb, fbx or both", err);
    }

    [Fact]
    public void Species_ik_is_listed_and_an_unknown_species_reports_not_found()
    {
        Assert.Contains("ik", Run("species", "--help").Out);
        using var game = new FakeGame();
        var (code, _, err) = Run("species", "ik", "stegosaurus", "-w", IndexedWorkspace(game));
        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("ASSET_NOT_FOUND", err);
    }

    [Fact]
    public void Species_ik_lines_are_plain_ascii_for_any_console()
    {
        var chain = new Tyrant.Core.Blender.BlenderIkChain("Leg L", "limb", "L", false,
            [new("Femur.L", 0.5f), new("Calve.L", 0.4f), new("Heel.L", 0f)], 1f, [0f, 0f, 0f],
            [new("Calve.L", "Pelvis", [0f, 0f, 1f], 0.4f)], false, new("ctrl_foot.L", "ctrl_knee.L", null));

        var line = Tyrant.Cli.Commands.SpeciesIkCommand.Describe(chain);

        Assert.Contains("Femur.L -> Calve.L -> Heel.L, pole from Pelvis", line);
        Assert.All(line, c => Assert.True(c < 128, $"non-ASCII '{c}' in: {line}"));
    }

    [Fact]
    public void Species_rig_info_lists_moved_and_growth_bones_in_ascii()
    {
        var info = new Tyrant.Core.Rigging.RigInfo(["MainBone", "Pelvis", "Neck.002"], ["Pelvis"], ["Neck.002"], [], ["The game's animations for this species were not found"]);

        var lines = Tyrant.Cli.Commands.SpeciesRigInfoCommand.Lines(info).ToList();

        Assert.Contains("Moved by the game's animations: Pelvis", lines);
        Assert.Contains("Positioned by growth: Neck.002", lines);
        Assert.Contains("Scaled by growth: (none)", lines);
        Assert.Contains(lines, l => l.StartsWith("  WARN", StringComparison.Ordinal));
        Assert.All(string.Join("", lines), c => Assert.True(c < 128, $"non-ASCII '{c}'"));
    }

    [Fact]
    public void Species_animations_lines_show_name_length_rate_and_flags_in_ascii()
    {
        var lines = Tyrant.Cli.Commands.SpeciesAnimationsCommand.Lines([
            new Tyrant.Core.Animation.AnimationInfo("Carch|LocWalk", "LocWalk", 2.5f, 24, true, true),
            new Tyrant.Core.Animation.AnimationInfo("Carch|Roar", "Roar", 3f, 30, false, false),
        ]).ToList();

        Assert.Contains(lines, l => l.Contains("LocWalk") && l.Contains("2.50 s") && l.Contains("24 fps") && l.Contains("loops") && l.Contains("travels"));
        Assert.Contains(lines, l => l.Contains("Carch|LocWalk")); // the id the other commands take (or the shown name)
        Assert.Contains(lines, l => l.Contains("Roar") && !l.Contains("loops"));
        Assert.Contains(lines, l => l.Contains("2 animation(s)"));
        Assert.All(string.Join("", lines), c => Assert.True(c < 128, $"non-ASCII '{c}'"));
    }

    [Fact]
    public void Export_animations_needs_names_or_all()
    {
        using var game = new FakeGame();

        var (code, _, err) = Run("species", "export-animations", "carcharodontosaurus", "-w", IndexedWorkspace(game));

        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("--all", err);
    }

    [Fact]
    public void Starting_skin_is_chosen_changed_and_shown()
    {
        using var game = new FakeGame();
        var ws = TexturedAllosaurus(game);

        Assert.Equal(ExitCodes.Ok, AddMirabilis(ws, "--starting-skin", "Albino").Code);
        Assert.Contains("starting skin 1 Albino", Run("mod", "show", "mod-a", "-w", ws).Out);
        Assert.Equal(ExitCodes.Ok, Run("mod", "remove-skin", "mod-a", "mirabilis-start", "--delete-files", "-w", ws).Code); // as an older species

        var (code, _, err) = Run("mod", "set-species-skin", "mod-a", "mirabilis", "--starting-skin", "9", "-w", ws);
        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("no skin number 9", err);
        Assert.Equal(ExitCodes.Ok, Run("mod", "set-species-skin", "mod-a", "mirabilis", "--starting-skin", "0", "--name", "Ghost", "-w", ws).Code);
        Assert.Contains("starting skin 0 Ghost", Run("mod", "show", "mod-a", "-w", ws).Out);
    }

    private static string SeedAllosaurus(FakeGame game)
    {
        var ws = TexturedAllosaurus(game);
        Assert.Equal(ExitCodes.Ok, AddMirabilis(ws).Code);
        Assert.Equal(ExitCodes.Ok, Run("mod", "remove-skin", "mod-a", "mirabilis-start", "--delete-files", "-w", ws).Code); // as an older species
        return ws;
    }

    [Fact]
    public void Set_species_skin_sets_clears_and_rejects_conflicting_swatch_options()
    {
        using var game = new FakeGame();
        var ws = SeedAllosaurus(game);
        var png = Path.Combine(ws, "swatch.png");
        File.WriteAllBytes(png, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0]);

        Assert.Equal(ExitCodes.Ok, Run("mod", "set-species-skin", "mod-a", "mirabilis", "--swatch", png, "-w", ws).Code);
        Assert.Contains("swatch icons/mirabilis.swatch.png", Run("mod", "show", "mod-a", "-w", ws).Out);

        var (code, _, err) = Run("mod", "set-species-skin", "mod-a", "mirabilis", "--swatch", png, "--no-swatch", "-w", ws);
        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("not both", err);

        Assert.Equal(ExitCodes.Ok, Run("mod", "set-species-skin", "mod-a", "mirabilis", "--no-swatch", "-w", ws).Code);
        Assert.DoesNotContain("swatch", Run("mod", "show", "mod-a", "-w", ws).Out);
    }

    [Fact]
    public void Species_portraits_are_set_and_cleared_from_the_command_line()
    {
        using var game = new FakeGame();
        var ws = SeedAllosaurus(game);
        var art = Path.Combine(ws, "portrait.png");
        File.WriteAllBytes(art, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0]);

        var set = Run("mod", "set-species-portrait", "mod-a", "mirabilis", "--slot", "adultMale", "--png", art, "-w", ws);
        Assert.Contains("portraits/mirabilis/adultMale.png", set.Out);
        var bad = Run("mod", "set-species-portrait", "mod-a", "mirabilis", "--slot", "adult", "--png", art, "-w", ws);
        Assert.Contains("adultMale", bad.Err);
        var neither = Run("mod", "set-species-portrait", "mod-a", "mirabilis", "--slot", "infant", "-w", ws);
        Assert.Contains("--png or --clear", neither.Err);
        Assert.Contains("uses the game's", Run("mod", "set-species-portrait", "mod-a", "mirabilis", "--slot", "adultMale", "--clear", "-w", ws).Out);
        Assert.NotEqual(ExitCodes.Ok, Run("mod", "set-species-portrait", "mod-a", "nope", "--slot", "adultMale", "--clear", "-w", ws).Code);
    }

    [Fact]
    public void Set_species_skin_with_an_empty_name_goes_back_to_the_template_skin_name()
    {
        using var game = new FakeGame();
        var ws = SeedAllosaurus(game);

        Assert.Equal(ExitCodes.Ok, Run("mod", "set-species-skin", "mod-a", "mirabilis", "--starting-skin", "1", "--name", "Ghost", "-w", ws).Code);
        Assert.Contains("starting skin 1 Ghost", Run("mod", "show", "mod-a", "-w", ws).Out);

        Assert.Equal(ExitCodes.Ok, Run("mod", "set-species-skin", "mod-a", "mirabilis", "--name", "", "-w", ws).Code);
        Assert.Contains("starting skin 1 Albino", Run("mod", "show", "mod-a", "-w", ws).Out);
    }

    [Fact]
    public void Set_species_skin_with_an_unknown_skin_name_lists_the_skins()
    {
        using var game = new FakeGame();
        var ws = SeedAllosaurus(game);

        var (code, _, err) = Run("mod", "set-species-skin", "mod-a", "mirabilis", "--starting-skin", "Purple", "-w", ws);

        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("no skin 'Purple'", err);
        Assert.Contains("0 Default", err);
        Assert.Contains("1 Albino", err);
    }

    [Fact]
    public void Mod_show_names_the_mods_whose_species_a_skin_is_for()
    {
        // add-skin itself needs the template's game textures (not in the fake game), so the skin entry is written to the manifest directly.
        using var game = new FakeGame();
        var ws = SeedAllosaurus(game);
        Assert.Equal(ExitCodes.Ok, Run("mod", "new", "mod-b", "-w", ws).Code);
        var other = Tyrant.Core.Mods.ModProject.Open(Tyrant.Core.Workspaces.Workspace.Open(ws), "mod-b");
        other.Manifest.Skins.Add(new Tyrant.Framework.Core.SkinEntry { Id = "red", Species = "tyrant.mod-a.mirabilis", Name = "Red", Male = new() { ["diffuse"] = "skins/red/male_diffuse.png" } });
        Directory.CreateDirectory(Path.Combine(other.Dir, "skins", "red"));
        File.WriteAllBytes(Path.Combine(other.Dir, "skins", "red", "male_diffuse.png"), [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0]);
        other.Save();

        var shown = Run("mod", "show", "mod-b", "-w", ws);
        Assert.Contains("Uses species from: mod-a (1 skin(s))", shown.Out + shown.Err);
    }

    [Fact]
    public void Add_skin_finds_another_mods_species_but_not_a_missing_one()
    {
        // The fake game has no template textures, so a full add-skin cannot succeed; the error shows how far it got.
        using var game = new FakeGame();
        var ws = SeedAllosaurus(game);
        Assert.Equal(ExitCodes.Ok, Run("mod", "new", "mod-b", "-w", ws).Code);

        var found = Run("mod", "add-skin", "mod-b", "tyrant.mod-a.mirabilis", "--name", "Blue", "-w", ws);
        var missing = Run("mod", "add-skin", "mod-b", "tyrant.mod-z.nope", "--name", "X", "-w", ws);

        Assert.Equal(ExitCodes.Error, found.Code);
        Assert.DoesNotContain("No species", found.Out + found.Err);
        Assert.Equal(ExitCodes.Error, missing.Code);
        Assert.Contains("No species", missing.Out + missing.Err);
    }
}
