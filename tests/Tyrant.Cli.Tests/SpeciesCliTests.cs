using Tyrant.Cli;
using Tyrant.Core.Tests;

namespace Tyrant.Cli.Tests;

public class SpeciesCliTests
{
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
        var ws = IndexedWorkspace(game);
        var objects = new Tyrant.Dumper.Serialization.DumpResult();
        objects.Objects.Add(new Tyrant.Dumper.Serialization.DumpObject(typeof(object),
            new Tyrant.Dumper.Serialization.EngineObjectInfo("PrehistoricKingdom.AnimalData", "Allosaurus", 1),
            """{"speciesID":"Allosaurus Anax", "skinsData":[{"skinName":"Default"},{"skinName":"Albino"}]}"""));
        Tyrant.Dumper.Serialization.DumpWriter.Write(Path.Combine(ws, "data"), objects, [], new Tyrant.Dumper.Serialization.DumpManifest { RequestId = "r1", BuildGuid = "b" });
        Assert.Equal(ExitCodes.Ok, Run("mod", "new", "mod-a", "-w", ws).Code);

        Assert.Equal(ExitCodes.Ok, Run("mod", "add-species", "mod-a", "mirabilis", "Allosaurus Anax", "--name", "Mirabilis", "--genus", "E", "--epithet", "m",
            "--starting-skin", "Albino", "-w", ws).Code);
        Assert.Contains("starting skin 1 Albino", Run("mod", "show", "mod-a", "-w", ws).Out);

        var (code, _, err) = Run("mod", "set-species-skin", "mod-a", "mirabilis", "--starting-skin", "9", "-w", ws);
        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("no skin number 9", err);
        Assert.Equal(ExitCodes.Ok, Run("mod", "set-species-skin", "mod-a", "mirabilis", "--starting-skin", "0", "--name", "Ghost", "-w", ws).Code);
        Assert.Contains("starting skin 0 Ghost", Run("mod", "show", "mod-a", "-w", ws).Out);
    }

    private static string SeedAllosaurus(FakeGame game)
    {
        var ws = IndexedWorkspace(game);
        var objects = new Tyrant.Dumper.Serialization.DumpResult();
        objects.Objects.Add(new Tyrant.Dumper.Serialization.DumpObject(typeof(object),
            new Tyrant.Dumper.Serialization.EngineObjectInfo("PrehistoricKingdom.AnimalData", "Allosaurus", 1),
            """{"speciesID":"Allosaurus Anax", "skinsData":[{"skinName":"Default"},{"skinName":"Albino"}]}"""));
        Tyrant.Dumper.Serialization.DumpWriter.Write(Path.Combine(ws, "data"), objects, [], new Tyrant.Dumper.Serialization.DumpManifest { RequestId = "r1", BuildGuid = "b" });
        Assert.Equal(ExitCodes.Ok, Run("mod", "new", "mod-a", "-w", ws).Code);
        Assert.Equal(ExitCodes.Ok, Run("mod", "add-species", "mod-a", "mirabilis", "Allosaurus Anax", "--name", "Mirabilis", "--genus", "E", "--epithet", "m", "-w", ws).Code);
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
