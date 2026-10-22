using Tyrant.Cli;
using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Models;
using Tyrant.Core.Tests;
using static Tyrant.Core.Tests.RigFixture;

namespace Tyrant.Cli.Tests;

public class ModModelCliTests
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

    /// <summary>A workspace with the species data, an index holding Carcharodontosaurus' prefab, mod 'big-carch' and a .glb; the CLI reads the prefab through a fake reader.</summary>
    private static (string Ws, string Glb) Setup(FakeGame game)
    {
        var ws = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws");
        Assert.Equal(ExitCodes.Ok, Run("workspace", "init", ws, "--game", game.Root).Code);
        Assert.Equal(ExitCodes.Ok, Run("mod", "new", "big-carch", "--name", "Big Carch", "-w", ws).Code);
        SkinDumps.Write(Path.Combine(ws, "data"));
        new AssetIndex { Assets = [.. SkinDumps.Textures, SkinDumps.Prefab], Fingerprint = GameFingerprint.Compute(new GameInstall(game.Root, null)) }
            .Save(Path.Combine(ws, "cache", AssetIndex.FileName));
        var prefab = ModelFixture.Prefab(ModelFixture.Triangle(name: "Carch_LOD00"), skinned: true);
        prefab = prefab with { Renderers = [prefab.Renderers[0] with { Materials = [new MaterialModel("Carch", [])] }] };
        CliServices.AssetReader = new FakeAssetReader { PrefabModelToReturn = prefab };
        var glb = Path.Combine(ws, "carch.glb");
        GltfModelWriter.WriteGlb(prefab, prefab.Renderers[0], glb, [new GltfMaterial("Carch")]);
        return (ws, glb);
    }

    [Fact]
    public void Species_textures_lists_a_species_skin_textures()
    {
        using var game = new FakeGame();
        var (ws, _) = Setup(game);
        try
        {
            var list = Run("species", "textures", "Carcharodontosaurus", "-w", ws);

            Assert.True(list.Code == ExitCodes.Ok, list.Err);
            Assert.Contains("T_carch_alt1_male_D", list.Out);
            Assert.Contains("adult colour", list.Out);
            Assert.Contains("Alt 1", list.Out);
        }
        finally
        {
            CliServices.AssetReader = new BundleAssetReader();
        }
    }

    [Fact]
    public void Skin_file_copy_base_puts_the_base_texture_in_the_skin()
    {
        using var game = new FakeGame();
        var (ws, _) = Setup(game);
        try
        {
            var dir = Path.Combine(ws, "mods", "big-carch");
            Directory.CreateDirectory(Path.Combine(dir, "skins", "spiked"));
            File.WriteAllText(Path.Combine(dir, "skins", "spiked", "male_D.png"), "png");
            File.WriteAllText(Path.Combine(dir, "mod.json"),
                "{\"format\":1,\"id\":\"big-carch\",\"name\":\"Big Carch\",\"version\":\"1.0.0\",\"replace\":[],\"skins\":[{\"id\":\"spiked\",\"species\":\"Carcharodontosaurus\",\"name\":\"Spiked\",\"base\":\"1\",\"male\":{\"diffuse\":\"skins/spiked/male_D.png\"}}]}");

            var copy = Run("mod", "skin-file", "big-carch", "spiked", "male", "normal", "--copy-base", "-w", ws);

            Assert.True(copy.Code == ExitCodes.Ok, copy.Err + copy.Out);
            Assert.Contains("skins/spiked/male_N.png", File.ReadAllText(Path.Combine(ws, "mods", "big-carch", "mod.json")));
        }
        finally
        {
            CliServices.AssetReader = new BundleAssetReader();
        }
    }

    [Fact]
    public void Skin_file_copy_base_works_for_a_skin_on_another_mods_species()
    {
        using var game = new FakeGame();
        var (ws, _) = Setup(game);
        try
        {
            Assert.Equal(ExitCodes.Ok, Run("mod", "new", "mod-a", "-w", ws).Code);
            var added = Run("mod", "add-species", "mod-a", "anax", "Carcharodontosaurus", "--name", "Anax", "--genus", "E", "--epithet", "a", "--starting-skin", "1", "-w", ws);
            Assert.True(added.Code == ExitCodes.Ok, added.Err + added.Out);
            var dir = Path.Combine(ws, "mods", "big-carch");
            Directory.CreateDirectory(Path.Combine(dir, "skins", "spiked"));
            File.WriteAllText(Path.Combine(dir, "skins", "spiked", "male_D.png"), "png");
            File.WriteAllText(Path.Combine(dir, "mod.json"), // base "0": mod-a's starting skin, the template's "Alt 1"
                "{\"format\":1,\"id\":\"big-carch\",\"name\":\"Big Carch\",\"version\":\"1.0.0\",\"replace\":[],\"skins\":[{\"id\":\"spiked\",\"species\":\"tyrant.mod-a.anax\",\"name\":\"Spiked\",\"base\":\"0\",\"male\":{\"diffuse\":\"skins/spiked/male_D.png\"}}]}");

            var copy = Run("mod", "skin-file", "big-carch", "spiked", "male", "normal", "--copy-base", "-w", ws);

            Assert.True(copy.Code == ExitCodes.Ok, copy.Err + copy.Out);
            Assert.Contains("skins/spiked/male_N.png", File.ReadAllText(Path.Combine(ws, "mods", "big-carch", "mod.json")));
        }
        finally
        {
            CliServices.AssetReader = new BundleAssetReader();
        }
    }

    [Fact]
    public void Replace_model_and_remove_model_work_from_the_command_line()
    {
        using var game = new FakeGame();
        var (ws, glb) = Setup(game);
        try
        {
            var add = Run("mod", "replace-model", "big-carch", "Carcharodontosaurus", glb, "-w", ws);
            var show = Run("mod", "show", "big-carch", "-w", ws);
            var rebuild = Run("mod", "rebuild-models", "big-carch", "-w", ws);
            var remove = Run("mod", "remove-model", "big-carch", "Carcharodontosaurus", "-w", ws);

            Assert.True(add.Code == ExitCodes.Ok, add.Err);
            Assert.Contains("LOD 0", add.Out);
            Assert.Contains("Models (1)", show.Out);
            Assert.Contains("Carcharodontosaurus", show.Out);
            Assert.True(rebuild.Code == ExitCodes.Ok, rebuild.Err);
            Assert.True(remove.Code == ExitCodes.Ok, remove.Err);
        }
        finally
        {
            CliServices.AssetReader = new BundleAssetReader();
        }
    }

    private static void WriteRigOnly(string ws) => File.WriteAllText(Path.Combine(ws, "mods", "big-carch", "mod.json"),
        """{"format":1,"id":"big-carch","name":"Big Carch","version":"1.0.0","replace":[],"models":[{"target":"Carcharodontosaurus","rig":{"Tail":{"move":[0,0.1,0]}}}]}""");

    [Fact]
    public void Mod_rig_shows_the_bones_and_clear_removes_the_rig_edit()
    {
        using var game = new FakeGame();
        var (ws, _) = Setup(game);
        try
        {
            WriteRigOnly(ws);

            var shown = Run("mod", "rig", "big-carch", "Carcharodontosaurus", "-w", ws);
            Assert.True(shown.Code == ExitCodes.Ok, shown.Err);
            Assert.Contains("Tail", shown.Out);
            Assert.Contains("move 0, 0.1, 0", shown.Out);
            Assert.DoesNotContain("rotate", shown.Out); // identity parts are left out
            Assert.Contains("no model of its own", shown.Out);

            var cleared = Run("mod", "rig", "big-carch", "Carcharodontosaurus", "--clear", "-w", ws);
            Assert.True(cleared.Code == ExitCodes.Ok, cleared.Err);
            Assert.Contains("Cleared the rig edit", cleared.Out);
            Assert.DoesNotContain("\"rig\"", File.ReadAllText(Path.Combine(ws, "mods", "big-carch", "mod.json")));

            Assert.Contains("No rig edit", Run("mod", "rig", "big-carch", "Carcharodontosaurus", "-w", ws).Out);
        }
        finally
        {
            CliServices.AssetReader = new BundleAssetReader();
        }
    }

    /// <summary>Setup, but the game's prefab is the 16-bone animal of the rig tests, so a model can carry a rig edit in its bones.</summary>
    private static (string Ws, PrefabModel Prefab) SetupRigged(FakeGame game)
    {
        var (ws, _) = Setup(game);
        var prefab = Game();
        prefab = prefab with { Renderers = [prefab.Renderers[0] with { Materials = [new MaterialModel("Quad_LOD0", [])] }] };
        CliServices.AssetReader = new FakeAssetReader { PrefabModelToReturn = prefab };
        return (ws, prefab);
    }

    private static string ModJson(string ws) => File.ReadAllText(Path.Combine(ws, "mods", "big-carch", "mod.json"));

    [Fact]
    public void Replace_model_prints_the_rig_edit_it_read()
    {
        using var game = new FakeGame();
        var (ws, prefab) = SetupRigged(game);
        try
        {
            var glb = Export(prefab, R());

            var added = Run("mod", "replace-model", "big-carch", "Carcharodontosaurus", glb, "-w", ws);

            Assert.True(added.Code == ExitCodes.Ok, added.Err + added.Out);
            Assert.Contains("Rig edit: from the model's bones (2 bones)", added.Out);
            Assert.Matches(@"  Neck1  move 0, 0\.05\d*, 0\.02\d*  rotate 10(\.\d+)?, [-0-9.e]+, [-0-9.e]+ deg", added.Out);
            Assert.Matches(@"  Tail1  rotate [-0-9.e]+, -15(\.\d+)?, [-0-9.e]+ deg", added.Out);
            Assert.Contains("\"Neck1\"", ModJson(ws));
        }
        finally
        {
            CliServices.AssetReader = new BundleAssetReader();
        }
    }

    [Fact]
    public void Dry_run_saves_nothing()
    {
        using var game = new FakeGame();
        var (ws, prefab) = SetupRigged(game);
        try
        {
            var before = ModJson(ws);

            var dry = Run("mod", "replace-model", "big-carch", "Carcharodontosaurus", Export(prefab, R()), "--dry-run", "-w", ws);

            Assert.True(dry.Code == ExitCodes.Ok, dry.Err + dry.Out);
            Assert.Contains("Rig edit: from the model's bones (2 bones)", dry.Out);
            Assert.DoesNotContain("Added the model", dry.Out);
            Assert.Equal(before, ModJson(ws));
            Assert.False(Directory.Exists(Path.Combine(ws, "mods", "big-carch", "models")));
        }
        finally
        {
            CliServices.AssetReader = new BundleAssetReader();
        }
    }

    [Fact]
    public void Keep_rig_and_use_file_rig_together_are_refused()
    {
        using var game = new FakeGame();
        var (ws, prefab) = SetupRigged(game);
        try
        {
            var before = ModJson(ws);

            var both = Run("mod", "replace-model", "big-carch", "Carcharodontosaurus", Export(prefab, R()), "--keep-rig", "--use-file-rig", "-w", ws);

            Assert.NotEqual(ExitCodes.Ok, both.Code);
            Assert.Contains("--keep-rig", both.Err + both.Out);
            Assert.Equal(before, ModJson(ws));
        }
        finally
        {
            CliServices.AssetReader = new BundleAssetReader();
        }
    }

    [Fact]
    public void The_game_skeleton_keeps_the_rig_edit_and_says_how_to_remove_it()
    {
        using var game = new FakeGame();
        var (ws, prefab) = SetupRigged(game);
        try
        {
            Assert.Equal(ExitCodes.Ok, Run("mod", "replace-model", "big-carch", "Carcharodontosaurus", Export(prefab, R()), "-w", ws).Code);
            var plain = Export(prefab, null);

            var kept = Run("mod", "replace-model", "big-carch", "Carcharodontosaurus", plain, "-w", ws);
            Assert.True(kept.Code == ExitCodes.Ok, kept.Err + kept.Out);
            Assert.Contains("the model has the game's skeleton", kept.Out);
            Assert.Contains("Kept the current rig edit (the model has the game's skeleton); --use-file-rig removes it.", kept.Out);
            Assert.Contains("\"Neck1\"", ModJson(ws));

            var removed = Run("mod", "replace-model", "big-carch", "Carcharodontosaurus", plain, "--use-file-rig", "-w", ws);
            Assert.True(removed.Code == ExitCodes.Ok, removed.Err + removed.Out);
            Assert.DoesNotContain("Kept the current", removed.Out);
            Assert.DoesNotContain("\"Neck1\"", ModJson(ws));
        }
        finally
        {
            CliServices.AssetReader = new BundleAssetReader();
        }
    }

    [Fact]
    public void Keep_rig_keeps_the_current_edit_whatever_the_model_says()
    {
        using var game = new FakeGame();
        var (ws, prefab) = SetupRigged(game);
        try
        {
            Assert.Equal(ExitCodes.Ok, Run("mod", "replace-model", "big-carch", "Carcharodontosaurus", Export(prefab, R()), "-w", ws).Code);

            var kept = Run("mod", "replace-model", "big-carch", "Carcharodontosaurus", Export(prefab, Rig(Turn("Head", System.Numerics.Vector3.UnitX, 30))), "--keep-rig", "-w", ws);

            Assert.True(kept.Code == ExitCodes.Ok, kept.Err + kept.Out);
            Assert.Contains("\"Neck1\"", ModJson(ws));
            Assert.DoesNotContain("\"Head\"", ModJson(ws));
        }
        finally
        {
            CliServices.AssetReader = new BundleAssetReader();
        }
    }

    [Fact]
    public void Replace_model_refuses_a_rig_edit_the_rules_refuse()
    {
        using var game = new FakeGame();
        var (ws, prefab) = SetupRigged(game);
        try
        {
            var glb = Export(prefab, R());
            var rigFile = R(); // matches the bones but names a bone the species does not have
            rigFile["Wing.L"] = Tyrant.Framework.Core.RigOffset.Identity with { Move = new Tyrant.Framework.Core.RigVector3(0, 0.1f, 0) };
            File.WriteAllText(glb + ".rig.json", Tyrant.Framework.Core.Json.Write(Tyrant.Framework.Core.RigEdit.ToJson(rigFile)));
            var before = ModJson(ws);

            var refused = Run("mod", "replace-model", "big-carch", "Carcharodontosaurus", glb, "-w", ws);

            Assert.NotEqual(ExitCodes.Ok, refused.Code);
            Assert.Contains("  ERROR  ", refused.Out);
            Assert.Contains("Wing.L", refused.Out);
            Assert.Equal(before, ModJson(ws));
        }
        finally
        {
            CliServices.AssetReader = new BundleAssetReader();
        }
    }
    /// <summary>A rig edit saved earlier that moves the neck and a bone the species does not have (it breaks a rule), with no model.</summary>
    private static void WriteBrokenRig(string ws) => File.WriteAllText(Path.Combine(ws, "mods", "big-carch", "mod.json"),
        """{"format":1,"id":"big-carch","name":"Big Carch","version":"1.0.0","replace":[],"models":[{"target":"Carcharodontosaurus","rig":{"Neck1":{"move":[0,0.05,0]},"Wing.L":{"move":[0,0.1,0]}}}]}""");

    [Fact]
    public void Keep_rig_succeeds_when_only_the_files_rig_edit_breaks_a_rule()
    {
        using var game = new FakeGame();
        var (ws, prefab) = SetupRigged(game);
        try
        {
            Assert.Equal(ExitCodes.Ok, Run("mod", "replace-model", "big-carch", "Carcharodontosaurus", Export(prefab, R()), "-w", ws).Code);
            var head = Rig(Turn("Head", System.Numerics.Vector3.UnitX, 30));
            var glb = Export(prefab, head);
            head["Wing.L"] = Tyrant.Framework.Core.RigOffset.Identity with { Move = new Tyrant.Framework.Core.RigVector3(0, 0.1f, 0) };
            File.WriteAllText(glb + ".rig.json", Tyrant.Framework.Core.Json.Write(Tyrant.Framework.Core.RigEdit.ToJson(head)));

            var refused = Run("mod", "replace-model", "big-carch", "Carcharodontosaurus", glb, "-w", ws);
            var kept = Run("mod", "replace-model", "big-carch", "Carcharodontosaurus", glb, "--keep-rig", "-w", ws);

            Assert.NotEqual(ExitCodes.Ok, refused.Code);
            Assert.True(kept.Code == ExitCodes.Ok, kept.Err + kept.Out);
            Assert.DoesNotContain("ERROR", kept.Out);
            Assert.Contains("\"Neck1\"", ModJson(ws));
            Assert.DoesNotContain("\"Wing.L\"", ModJson(ws));
        }
        finally
        {
            CliServices.AssetReader = new BundleAssetReader();
        }
    }

    [Fact]
    public void Use_file_rig_prints_and_refuses_on_the_files_rig_edit_only()
    {
        using var game = new FakeGame();
        var (ws, prefab) = SetupRigged(game);
        try
        {
            WriteBrokenRig(ws);
            var plain = Export(prefab, null);

            var dry = Run("mod", "replace-model", "big-carch", "Carcharodontosaurus", plain, "--use-file-rig", "--dry-run", "-w", ws);
            var removed = Run("mod", "replace-model", "big-carch", "Carcharodontosaurus", plain, "--use-file-rig", "-w", ws);

            Assert.True(dry.Code == ExitCodes.Ok, dry.Err + dry.Out);
            Assert.DoesNotContain("ERROR", dry.Out); // the broken rig edit is the one being removed
            Assert.True(removed.Code == ExitCodes.Ok, removed.Err + removed.Out);
            Assert.DoesNotContain("ERROR", removed.Out);
            Assert.DoesNotContain("Wing.L", ModJson(ws));
        }
        finally
        {
            CliServices.AssetReader = new BundleAssetReader();
        }
    }

    [Fact]
    public void Keep_rig_refuses_when_the_current_rig_edit_breaks_a_rule_and_says_how_to_clear_it()
    {
        using var game = new FakeGame();
        var (ws, prefab) = SetupRigged(game);
        try
        {
            WriteBrokenRig(ws);
            var before = ModJson(ws);

            var refused = Run("mod", "replace-model", "big-carch", "Carcharodontosaurus", Export(prefab, R()), "--keep-rig", "-w", ws);

            Assert.NotEqual(ExitCodes.Ok, refused.Code);
            Assert.Contains("  ERROR  The rig edit already on Carcharodontosaurus breaks a rule, not the model.", refused.Out);
            Assert.Contains("--clear", refused.Out);
            Assert.Equal(before, ModJson(ws));
        }
        finally
        {
            CliServices.AssetReader = new BundleAssetReader();
        }
    }

    [Fact]
    public void Dry_run_says_what_would_happen_to_the_rig_edit()
    {
        using var game = new FakeGame();
        var (ws, prefab) = SetupRigged(game);
        try
        {
            Assert.Equal(ExitCodes.Ok, Run("mod", "replace-model", "big-carch", "Carcharodontosaurus", Export(prefab, R()), "-w", ws).Code);

            var plain = Run("mod", "replace-model", "big-carch", "Carcharodontosaurus", Export(prefab, null), "--dry-run", "-w", ws);
            var other = Run("mod", "replace-model", "big-carch", "Carcharodontosaurus", Export(prefab, Rig(Turn("Head", System.Numerics.Vector3.UnitX, 30))), "--dry-run", "-w", ws);
            var keep = Run("mod", "replace-model", "big-carch", "Carcharodontosaurus", Export(prefab, null), "--keep-rig", "--dry-run", "-w", ws);

            Assert.Contains("Would keep the current rig edit (the model has the game's skeleton); --use-file-rig removes it.", plain.Out);
            Assert.DoesNotContain("Kept", plain.Out);
            Assert.Contains("Would replace the current rig edit with the model's; --keep-rig keeps the current one.", other.Out);
            Assert.DoesNotContain("Replaced", other.Out);
            Assert.Contains("Rig edit: the current one would be kept (--keep-rig)", keep.Out);
        }
        finally
        {
            CliServices.AssetReader = new BundleAssetReader();
        }
    }
}
