using Tyrant.Cli;
using Tyrant.Core.Tests;

namespace Tyrant.Cli.Tests;

public class ModEditCliTests
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

    private static string WorkspaceWithSkin(FakeGame game)
    {
        var ws = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws");
        Assert.Equal(ExitCodes.Ok, Run("workspace", "init", ws, "--game", game.Root).Code);
        Assert.Equal(ExitCodes.Ok, Run("mod", "new", "red-spot", "--name", "Red spot", "-w", ws).Code);
        var dir = Path.Combine(ws, "mods", "red-spot");
        Png(Path.Combine(dir, "skins", "blue", "male_D.png"));
        File.WriteAllText(Path.Combine(dir, "mod.json"),
            "{\"format\":1,\"id\":\"red-spot\",\"name\":\"Red spot\",\"version\":\"1.0.0\",\"replace\":[{\"texture\":\"T_A_D\",\"file\":\"textures/a.png\"}],\"skins\":[{\"id\":\"blue\",\"species\":\"Carcharodontosaurus\",\"name\":\"Blue\",\"base\":\"0\",\"male\":{\"diffuse\":\"skins/blue/male_D.png\"}}]}");
        return ws;
    }

    private static string Png(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        new StbImageWriteSharp.ImageWriter().WritePng(new byte[4 * 4 * 4], 4, 4, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        return path;
    }

    private static string ModJson(string ws) => File.ReadAllText(Path.Combine(ws, "mods", "red-spot", "mod.json"));

    private static string MaterialsPath(string ws, string folder) =>
        Path.Combine(ws, "mods", "red-spot", "materials", folder, "materials.json");

    private static string Field(string ws, string name) =>
        System.Text.Json.JsonDocument.Parse(ModJson(ws)).RootElement.TryGetProperty(name, out var v) ? v.ToString() : "";

    [Fact]
    public void Show_lists_the_details_replacements_and_skins()
    {
        using var game = new FakeGame();
        var ws = WorkspaceWithSkin(game);

        var (code, output, _) = Run("mod", "show", "red-spot", "-w", ws);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("Red spot", output);
        Assert.Contains("T_A_D", output);
        Assert.Contains("blue", output);
        Assert.Contains("Carcharodontosaurus", output);
    }

    [Fact]
    public void Set_changes_only_the_given_details()
    {
        using var game = new FakeGame();
        var ws = WorkspaceWithSkin(game);

        Assert.Equal(ExitCodes.Ok, Run("mod", "set", "red-spot", "--version", "1.2.0", "--author", "Shadow", "-w", ws).Code);

        Assert.Equal(("1.2.0", "Shadow", "Red spot"), (Field(ws, "version"), Field(ws, "author"), Field(ws, "name")));
    }

    [Fact]
    public void Rename_and_remove_a_skin()
    {
        using var game = new FakeGame();
        var ws = WorkspaceWithSkin(game);

        Assert.Equal(ExitCodes.Ok, Run("mod", "rename-skin", "red-spot", "blue", "Ocean blue", "-w", ws).Code);
        Assert.Contains("Ocean blue", ModJson(ws));
        Assert.Equal(ExitCodes.Ok, Run("mod", "remove-skin", "red-spot", "blue", "--delete-files", "-w", ws).Code);
        Assert.DoesNotContain("Ocean blue", ModJson(ws));
        Assert.False(File.Exists(Path.Combine(ws, "mods", "red-spot", "skins", "blue", "male_D.png")));
    }

    [Fact]
    public void Remove_skin_says_which_files_stayed()
    {
        using var game = new FakeGame();
        var ws = WorkspaceWithSkin(game);
        TestRecycleBin.Instance.RefuseUnder(Path.Combine(ws, "mods", "red-spot", "skins", "blue", "male_D.png"));

        var run = Run("mod", "remove-skin", "red-spot", "blue", "--delete-files", "-w", ws);

        Assert.Equal(ExitCodes.Ok, run.Code);
        Assert.Contains("stayed  skins/blue/male_D.png: ", run.Out);
        Assert.True(File.Exists(Path.Combine(ws, "mods", "red-spot", "skins", "blue", "male_D.png")));
    }

    [Fact]
    public void Colors_show_set_and_clear()
    {
        using var game = new FakeGame();
        var ws = WorkspaceWithSkin(game);
        var file = Path.Combine(ws, "colors.json");
        File.WriteAllText(file, "{\"pattern\":{\"a\":\"#3060ff\"}}");

        Assert.Equal(ExitCodes.Ok, Run("mod", "colors", "red-spot", "blue", "--set", file, "-w", ws).Code);
        Assert.Contains("#3060ff", Run("mod", "colors", "red-spot", "blue", "--show", "-w", ws).Out);
        Assert.Equal(ExitCodes.Ok, Run("mod", "colors", "red-spot", "blue", "--clear", "-w", ws).Code);
        Assert.DoesNotContain("colors", ModJson(ws));
        Assert.Equal(ExitCodes.Error, Run("mod", "colors", "red-spot", "blue", "-w", ws).Code); // none of --show/--set/--clear
    }

    [Fact]
    public void Materials_show_set_clear_and_import()
    {
        using var game = new FakeGame();
        var ws = WorkspaceWithSkin(game);
        var file = Path.Combine(ws, "materials.json");
        File.WriteAllText(file, "{\"Titanoboa\":{\"iridescence\":{\"strength\":0.3}}}");
        var ramp = Png(Path.Combine(ws, "art", "ramp.png"));

        Assert.Equal(ExitCodes.Ok, Run("mod", "materials", "red-spot", "blue", "--set", file, "-w", ws).Code);
        Assert.Contains("\"strength\"", Run("mod", "materials", "red-spot", "blue", "--show", "-w", ws).Out);
        Assert.Equal(ExitCodes.Ok, Run("mod", "materials", "red-spot", "blue", "--ramp", ramp, "--material", "Titanoboa", "-w", ws).Code);
        Assert.Contains("materials/skins/blue/iridescence-ramp.png", File.ReadAllText(MaterialsPath(ws, "skins/blue")));
        var mask = Png(Path.Combine(ws, "art", "mask.png"));
        Assert.Equal(ExitCodes.Ok, Run("mod", "materials", "red-spot", "blue", "--mask", mask, "--material", "Titanoboa", "-w", ws).Code);
        Assert.Contains("materials/skins/blue/iridescence-mask.png", File.ReadAllText(MaterialsPath(ws, "skins/blue")));
        var fur = Png(Path.Combine(ws, "art", "fur.png"));
        Assert.Equal(ExitCodes.Ok, Run("mod", "materials", "red-spot", "blue", "--fur-mask", fur, "--material", "Titanoboa", "-w", ws).Code);
        Assert.Equal(ExitCodes.Ok, Run("mod", "materials", "red-spot", "blue", "--fur-infant-mask", fur, "--material", "Titanoboa", "-w", ws).Code);
        var text = File.ReadAllText(MaterialsPath(ws, "skins/blue"));
        Assert.Contains("materials/skins/blue/fur-mask.png", text);
        Assert.Contains("materials/skins/blue/fur-infant-mask.png", text);
        Assert.Equal(ExitCodes.Ok, Run("mod", "materials", "red-spot", "blue", "--clear", "-w", ws).Code);
        Assert.False(File.Exists(MaterialsPath(ws, "skins/blue")));
        Assert.Equal(ExitCodes.Error, Run("mod", "materials", "red-spot", "blue", "-w", ws).Code); // no action
        Assert.Equal(ExitCodes.Error, Run("mod", "materials", "red-spot", "blue", "--species", "Trex", "--show", "-w", ws).Code); // skin and species
    }

    [Fact]
    public void Materials_set_accepts_fur_grow()
    {
        using var game = new FakeGame();
        var ws = WorkspaceWithSkin(game);
        var file = Path.Combine(ws, "materials.json");
        File.WriteAllText(file, "{\"Titanoboa\":{\"fur\":{\"grow\":true},\"settings\":{\"_Length\":0.5}}}");

        Assert.Equal(ExitCodes.Ok, Run("mod", "materials", "red-spot", "blue", "--set", file, "-w", ws).Code);
        Assert.Contains("\"grow\"", Run("mod", "materials", "red-spot", "blue", "--show", "-w", ws).Out);
        File.WriteAllText(file, "{\"Titanoboa\":{\"fur\":{\"grow\":\"yes\"}}}");
        Assert.Equal(ExitCodes.Error, Run("mod", "materials", "red-spot", "blue", "--set", file, "-w", ws).Code);
    }

    [Fact]
    public void Materials_preset_starts_a_material_from_a_species_over_its_own_settings()
    {
        using var game = new FakeGame();
        var ws = WorkspaceWithSkin(game);
        SkinDumps.Write(Path.Combine(ws, "data"));
        new Tyrant.Core.Assets.AssetIndex { Assets = [.. SkinDumps.Textures, SkinDumps.Prefab], Fingerprint = Tyrant.Core.Install.GameFingerprint.Compute(new Tyrant.Core.Install.GameInstall(game.Root, null)) }
            .Save(Path.Combine(ws, "cache", Tyrant.Core.Assets.AssetIndex.FileName));
        var previous = CliServices.AssetReader;
        CliServices.AssetReader = new FakeAssetReader
        {
            PrefabMaterials = [new Tyrant.Core.Models.MaterialModel("Carch_Mat", [new Tyrant.Core.Models.TextureSlot("_AdultDiffuse", null, 1), new Tyrant.Core.Models.TextureSlot("_IridescenceTexture", null, 2)])
            {
                Floats = new Dictionary<string, float> { ["_IridescenceScale"] = -0.25f },
            }],
        };
        try
        {
            var file = Path.Combine(ws, "materials.json");
            File.WriteAllText(file, "{\"Carch_Mat\":{\"settings\":{\"_Smoothness\":0.4}}}");
            Assert.Equal(ExitCodes.Ok, Run("mod", "materials", "red-spot", "blue", "--set", file, "-w", ws).Code);

            var preset = Run("mod", "materials", "red-spot", "blue", "--preset", "Carcharodontosaurus", "--kind", "iridescence", "-w", ws);

            Assert.True(preset.Code == ExitCodes.Ok, preset.Err);
            Assert.Contains("Carch_Mat of 'blue' starts from Carcharodontosaurus's iridescence", preset.Out);
            var shown = Run("mod", "materials", "red-spot", "blue", "--show", "-w", ws).Out;
            Assert.Contains("\"species\": \"Carcharodontosaurus\"", shown); // the ramp, by reference
            Assert.Contains("\"_Smoothness\": 0.4", shown); // what the preset does not set stays
            Assert.Contains("\"scale\": -0.25", shown);

            var noFur = Run("mod", "materials", "red-spot", "blue", "--preset", "Carcharodontosaurus", "--kind", "fur", "-w", ws);
            Assert.Equal(ExitCodes.Error, noFur.Code);
            Assert.Contains("Carcharodontosaurus has no skin with fur", noFur.Err);
            var noKind = Run("mod", "materials", "red-spot", "blue", "--preset", "Carcharodontosaurus", "-w", ws);
            Assert.Equal(ExitCodes.Error, noKind.Code);
            Assert.Contains("--kind iridescence or --kind fur", noKind.Err);
            var kindAlone = Run("mod", "materials", "red-spot", "blue", "--show", "--kind", "fur", "-w", ws);
            Assert.Equal(ExitCodes.Error, kindAlone.Code);
            Assert.Contains("--kind goes with --preset", kindAlone.Err);
        }
        finally
        {
            CliServices.AssetReader = previous;
        }
    }

    [Fact]
    public void Materials_of_a_species_without_the_game_data()
    {
        using var game = new FakeGame();
        var ws = WorkspaceWithSkin(game);
        Directory.CreateDirectory(Path.GetDirectoryName(MaterialsPath(ws, "species/Titanoboa"))!);
        File.WriteAllText(MaterialsPath(ws, "species/Titanoboa"),
            "{\"format\":1,\"species\":\"Titanoboa\",\"materials\":{\"Titanoboa\":{\"iridescence\":{\"strength\":0.3}}}}");
        var mask = Png(Path.Combine(ws, "art", "mask.png"));
        var set = Path.Combine(ws, "materials.json");
        File.WriteAllText(set, "{\"Titanoboa\":{\"iridescence\":{\"strength\":0.5}}}");

        Assert.Contains("\"strength\"", Run("mod", "materials", "red-spot", "--species", "titanoboa", "--show", "-w", ws).Out);
        Assert.Equal(ExitCodes.Ok, Run("mod", "materials", "red-spot", "--species", "titanoboa", "--mask", mask, "--material", "Titanoboa", "-w", ws).Code);
        Assert.Contains("materials/species/Titanoboa/iridescence-mask.png", File.ReadAllText(MaterialsPath(ws, "species/Titanoboa")));
        var noGame = Run("mod", "materials", "red-spot", "--species", "Titanoboa", "--set", set, "-w", ws);
        Assert.Equal(ExitCodes.Error, noGame.Code);
        Assert.Contains("needs the game's data", noGame.Err);
        Assert.Equal(ExitCodes.Error, Run("mod", "materials", "red-spot", "--species", "Nobody", "--mask", mask, "--material", "X", "-w", ws).Code);
        Assert.Equal(ExitCodes.Ok, Run("mod", "materials", "red-spot", "--species", "titanoboa", "--clear", "-w", ws).Code);
        Assert.False(File.Exists(MaterialsPath(ws, "species/Titanoboa")));
    }

    [Fact]
    public void Materials_of_a_vanilla_skin_show_set_clear()
    {
        using var game = new FakeGame();
        var ws = WorkspaceWithSkin(game);
        var set = Path.Combine(ws, "materials.json");
        File.WriteAllText(set, "{\"Titanoboa\":{\"iridescence\":{\"strength\":0.3,\"mask\":{\"from\":\"extra\"}}}}");
        var fur = Png(Path.Combine(ws, "art", "fur.png"));
        string[] skin = ["mod", "materials", "red-spot", "--species", "Titanoboa", "--vanilla-skin", "Alt 1"];
        string[] Plus(params string[] more) => [.. skin, .. more, "-w", ws];

        Assert.Equal(ExitCodes.Ok, Run(Plus("--set", set)).Code);
        Assert.Contains("\"strength\"", Run(Plus("--show")).Out);
        Assert.Contains("\"extra\"", File.ReadAllText(MaterialsPath(ws, "vanilla-skins/Titanoboa-Alt 1")));
        Assert.Equal(ExitCodes.Ok, Run(Plus("--fur-mask", fur, "--material", "Titanoboa")).Code);
        Assert.Contains("materials/vanilla-skins/Titanoboa-Alt 1/fur-mask.png", File.ReadAllText(MaterialsPath(ws, "vanilla-skins/Titanoboa-Alt 1")));
        Assert.Equal(ExitCodes.Error, Run("mod", "materials", "red-spot", "--vanilla-skin", "Alt 1", "--show", "-w", ws).Code); // needs --species
        Assert.Equal(ExitCodes.Error, Run("mod", "materials", "red-spot", "blue", "--species", "Titanoboa", "--vanilla-skin", "Alt 1", "--show", "-w", ws).Code);
        Assert.Equal(ExitCodes.Ok, Run(Plus("--clear")).Code);
        Assert.False(File.Exists(MaterialsPath(ws, "vanilla-skins/Titanoboa-Alt 1")));
    }

    [Fact]
    public void Skin_file_thumbnail_and_unreplace()
    {
        using var game = new FakeGame();
        var ws = WorkspaceWithSkin(game);
        var art = Png(Path.Combine(ws, "art", "extra.png"));

        Assert.Equal(ExitCodes.Ok, Run("mod", "skin-file", "red-spot", "blue", "male", "extra", art, "-w", ws).Code);
        Assert.Contains("skins/blue/male_extra.png", ModJson(ws));
        Assert.Equal(ExitCodes.Ok, Run("mod", "skin-file", "red-spot", "blue", "male", "extra", "--base", "-w", ws).Code);
        Assert.DoesNotContain("male_extra", ModJson(ws));
        Assert.Equal(ExitCodes.Error, Run("mod", "skin-file", "red-spot", "blue", "male", "diffuse", "--base", "-w", ws).Code); // last file
        Assert.Equal(ExitCodes.Ok, Run("mod", "thumbnail", "red-spot", "blue", art, "-w", ws).Code);
        Assert.Contains("thumbnail.png", ModJson(ws));
        Assert.Equal(ExitCodes.Ok, Run("mod", "thumbnail", "red-spot", "blue", "--auto", "-w", ws).Code);
        Assert.DoesNotContain("thumbnail", ModJson(ws));
        Assert.Equal(ExitCodes.Ok, Run("mod", "unreplace", "red-spot", "T_A_D", "-w", ws).Code);
        Assert.DoesNotContain("T_A_D", ModJson(ws));
    }

    [Fact]
    public void Skin_portraits_are_set_and_cleared_from_the_command_line()
    {
        using var game = new FakeGame();
        var ws = WorkspaceWithSkin(game);
        var art = Png(Path.Combine(ws, "art", "portrait.png"));

        var set = Run("mod", "set-skin-portrait", "red-spot", "blue", "--slot", "adultMale", "--png", art, "-w", ws);
        Assert.Contains("skins/blue/portraits/adultMale.png", set.Out);
        var bad = Run("mod", "set-skin-portrait", "red-spot", "blue", "--slot", "adult", "--png", art, "-w", ws);
        Assert.Contains("adultMale", bad.Err);
        var neither = Run("mod", "set-skin-portrait", "red-spot", "blue", "--slot", "infant", "-w", ws);
        Assert.Contains("--png or --clear", neither.Err);
        Assert.Contains("uses the game's", Run("mod", "set-skin-portrait", "red-spot", "blue", "--slot", "adultMale", "--clear", "-w", ws).Out);
    }

    [Fact]
    public void Set_compression_for_the_mod_and_one_texture()
    {
        using var game = new FakeGame();
        var ws = WorkspaceWithSkin(game);
        Tyrant.Framework.Core.ModManifest Manifest() => Tyrant.Framework.Core.ModManifest.Parse(File.ReadAllText(Path.Combine(ws, "mods", "red-spot", "mod.json")));

        Assert.Equal(ExitCodes.Ok, Run("mod", "set-compression", "red-spot", "bc7", "-w", ws).Code);
        Assert.Equal(Tyrant.Framework.Core.TextureCompression.Bc7, Manifest().Textures.Compression);
        Assert.Equal(ExitCodes.Ok, Run("mod", "set-compression", "red-spot", "auto", "--texture", "skins/blue/male_D.png", "-w", ws).Code);
        Assert.Equal(Tyrant.Framework.Core.TextureCompression.Auto, Manifest().Textures.For("skins/blue/male_D.png"));
        Assert.Equal(ExitCodes.Ok, Run("mod", "set-compression", "red-spot", "default", "--texture", "skins/blue/male_D.png", "-w", ws).Code);
        Assert.Empty(Manifest().Textures.Overrides);

        Assert.Contains("--texture", Run("mod", "set-compression", "red-spot", "default", "-w", ws).Err);
        var bad = Run("mod", "set-compression", "red-spot", "fast", "-w", ws);
        Assert.NotEqual(ExitCodes.Ok, bad.Code);
        Assert.Contains("auto, bc7 or default", bad.Err);
    }
}
