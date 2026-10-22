using System.Numerics;
using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.ModelReplacements;
using Tyrant.Core.Models;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;
using static Tyrant.Core.Tests.RigFixture;

namespace Tyrant.Core.Tests;

/// <summary>A model file brings its rig edit to the destination: read, previewed, and chosen (ModProject.PreviewModelRig / ReplaceModel).</summary>
public class ModProjectModelRigTests
{
    private const string Carch = "Carcharodontosaurus";

    private sealed record Setup(FakeGame Game, Workspace Ws, GameInstall Install, ModProject Mod, FakeAssetReader Reader, PrefabModel Prefab) : IDisposable
    {
        public IReadOnlyList<SpeciesSkins> Species => SpeciesSkinsReader.Load(Ws);

        public ModelRigPreview Preview(string model, string? target = Carch, string? skin = null, Func<Blender.IModelConverter>? converter = null) =>
            Mod.PreviewModelRig(Ws, Install, Index(), Species, Reader, model, skin is null ? target : null, null, skin, converter);

        public ModelReport Replace(string model, ModelRigChoice choice = ModelRigChoice.Auto, string? target = Carch, IReadOnlyDictionary<string, RigOffset>? rig = null,
            Func<Blender.IModelConverter>? converter = null) =>
            Mod.ReplaceModel(Install, Index(), Species, Reader, model, target, null, null, converter, rig, choice, Ws);

        public IReadOnlyDictionary<string, RigOffset>? Saved(string target = Carch) =>
            ModProject.Open(Ws, Mod.Id).Manifest.Models.Single(m => m.Target == target).Rig;

        public void Dispose() => Game.Dispose();
    }

    private static Setup Make()
    {
        var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), install);
        SkinDumps.Write(ws.DataDir);
        var prefab = Game();
        // The glTF writer names the fixture's material after its mesh.
        prefab = prefab with { Renderers = [prefab.Renderers[0] with { Materials = [new MaterialModel("Quad_LOD0", [])] }] };
        return new Setup(game, ws, install, ModProject.Create(ws, "big-carch", null, null), new FakeAssetReader { PrefabModelToReturn = prefab }, prefab);
    }

    private static AssetIndex Index() => new() { Assets = [.. SkinDumps.Textures, SkinDumps.Prefab] };

    private static Dictionary<string, RigOffset> HeadTurn() => Rig(Turn("Head", Vector3.UnitX, 30));

    [Fact]
    public void A_file_with_a_rig_edit_sets_it_on_a_destination_without_one()
    {
        using var s = Make();
        var glb = Export(s.Prefab, R());

        var preview = s.Preview(glb);
        s.Replace(glb);

        Assert.Equal(RigSource.Bones, preview.Reading.Source);
        Assert.Null(preview.Current);
        Assert.Equal(ModelRigQuestion.None, preview.Question);
        Assert.True(preview.Changes);
        Assert.Empty(preview.Errors);
        AssertRig(R(), s.Saved()!);
    }

    [Fact]
    public void A_different_rig_edit_replaces_the_current_one_on_auto_and_is_kept_with_keep_current()
    {
        using var s = Make();
        s.Mod.SetRig(s.Install, Index(), s.Species, s.Reader, Carch, null, HeadTurn());
        var current = RigEdit.Hash(s.Saved()); // as mod.json keeps it (its turns normalized)
        var glb = Export(s.Prefab, R());

        var preview = s.Preview(glb);
        Assert.Equal(ModelRigQuestion.UseOrKeep, preview.Question);
        Assert.True(preview.Changes);
        Assert.Equal(RigEdit.Hash(HeadTurn()), RigEdit.Hash(preview.Current)); // this mod as it is in memory

        s.Replace(glb, ModelRigChoice.KeepCurrent);
        Assert.Equal(current, RigEdit.Hash(s.Saved()));

        s.Replace(glb);
        AssertRig(R(), s.Saved()!);
    }

    [Fact]
    public void The_game_skeleton_keeps_the_current_rig_edit_on_auto_and_removes_it_with_use_file()
    {
        using var s = Make();
        s.Mod.SetRig(s.Install, Index(), s.Species, s.Reader, Carch, null, HeadTurn());
        var current = RigEdit.Hash(s.Saved()); // as mod.json keeps it (its turns normalized)
        var glb = Export(s.Prefab, null);

        var preview = s.Preview(glb);
        Assert.Equal(RigSource.GameSkeleton, preview.Reading.Source);
        Assert.Equal(ModelRigQuestion.KeepOrRemove, preview.Question);
        Assert.False(preview.Changes);

        s.Replace(glb);
        Assert.Equal(current, RigEdit.Hash(s.Saved()));

        s.Replace(glb, ModelRigChoice.UseFile);
        Assert.Null(s.Saved());
        Assert.NotEqual("", Assert.Single(s.Mod.Manifest.Models).File); // the model stays, without a rig edit
    }

    [Fact]
    public void The_same_rig_edit_asks_nothing_and_changes_nothing()
    {
        using var s = Make();
        s.Mod.SetRig(s.Install, Index(), s.Species, s.Reader, Carch, null, R());

        var preview = s.Preview(Export(s.Prefab, R()));

        Assert.Equal(RigSource.Saved, preview.Reading.Source);
        Assert.Equal(ModelRigQuestion.None, preview.Question);
        Assert.False(preview.Changes);
    }

    [Fact]
    public void A_rig_edit_the_rules_refuse_writes_nothing()
    {
        using var s = Make();
        var glb = Export(s.Prefab, R());
        // A .rig.json that matches the bones but names a bone the species does not have.
        var rigFile = R();
        rigFile["Wing.L"] = RigOffset.Identity with { Move = new RigVector3(0, 0.1f, 0) };
        File.WriteAllText(glb + ".rig.json", Json.Write(RigEdit.ToJson(rigFile)));
        var before = File.ReadAllText(Path.Combine(s.Mod.Dir, "mod.json"));

        var preview = s.Preview(glb);
        var ex = Assert.Throws<TyrantException>(() => s.Replace(glb));

        Assert.Equal(RigSource.RigFile, preview.Reading.Source);
        Assert.Contains(preview.Errors, e => e.Contains("Wing.L", StringComparison.Ordinal));
        Assert.Equal(TyrantErrorCode.ModInvalid, ex.Code);
        Assert.Contains("Wing.L", ex.Message);
        Assert.Equal(before, File.ReadAllText(Path.Combine(s.Mod.Dir, "mod.json")));
        var models = Path.Combine(s.Mod.Dir, ModProject.ModelsFolder);
        Assert.False(Directory.Exists(models) && Directory.EnumerateFiles(models).Any());
    }

    [Fact]
    public void Sends_rig_parameter_still_wins_over_the_file()
    {
        using var s = Make();
        var sent = Rig(Move("Tail2", 0, 0, -0.2f));

        var report = s.Replace(Export(s.Prefab, R()), rig: sent);

        Assert.Equal(RigEdit.Hash(sent), report.RigHash);
        Assert.Equal(RigEdit.Hash(sent), RigEdit.Hash(s.Saved()));
    }

    [Fact]
    public void Reimporting_a_send_made_model_keeps_its_scaled_rig_edit_without_asking()
    {
        using var s = Make();
        var saved = WithScaledTail();
        // Send: the project's send.glb (no .rig.json) with the armature's rig edit, scale included.
        var project = Directory.CreateDirectory(Path.Combine(s.Ws.Dir, "blender", "carch")).FullName;
        var send = Path.Combine(project, "send.glb");
        File.Copy(Export(s.Prefab, saved), send);
        s.Replace(send, rig: saved);

        var preview = s.Preview(send);
        s.Replace(send);

        Assert.Equal(RigSource.Saved, preview.Reading.Source);
        Assert.False(preview.Changes);
        Assert.Equal(ModelRigQuestion.None, preview.Question);
        Assert.Equal(new RigVector3(1.3f, 1.3f, 1.3f), s.Saved()!["Tail1"].Scale);
        Assert.Equal(RigEdit.Hash(saved), RigEdit.Hash(s.Saved()));
    }

    [Fact]
    public void A_custom_species_reads_against_its_templates_skeleton()
    {
        using var s = Make();
        var key = s.Mod.AddSpecies(s.Species, "mirabilis", Carch, "Mirabilis", "Tyrantus", "mirabilis").Key(s.Mod.Id);
        var glb = Export(s.Prefab, R());

        var preview = s.Preview(glb, key);
        s.Replace(glb, target: key);

        Assert.Equal(RigSource.Bones, preview.Reading.Source);
        AssertRig(R(), preview.Reading.Rig);
        AssertRig(R(), s.Saved(key)!);
    }

    [Fact]
    public void An_fbx_is_converted_once_for_a_preview_and_the_add_that_follows()
    {
        using var s = Make();
        var fbx = Path.Combine(s.Ws.Dir, "Ultimasaurus.fbx");
        File.WriteAllText(fbx, "fbx");
        var converter = new FakeModelConverter { FbxGlb = Export(s.Prefab, R()) };

        var preview = s.Preview(fbx, converter: () => converter);
        var report = s.Replace(fbx, converter: () => converter);

        Assert.Equal(1, converter.FbxCalls);
        Assert.Equal(RigSource.Bones, preview.Reading.Source);
        Assert.Equal(Path.GetFullPath(fbx), report.Origin);
        AssertRig(R(), s.Saved()!);
    }

    [Fact]
    public void A_changed_fbx_is_converted_again()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"))).FullName;
        var fbx = Path.Combine(dir, "a.fbx");
        File.WriteAllText(fbx, "fbx");
        var inner = new FakeModelConverter { FbxGlb = Export(Game(), null) };
        var cache = new Blender.CachingModelConverter(inner, Path.Combine(dir, "cache"));

        cache.FbxToGlb(fbx, Path.Combine(dir, "1.glb"));
        cache.FbxToGlb(fbx, Path.Combine(dir, "2.glb"));
        File.WriteAllText(fbx, "fbx, saved again");
        cache.FbxToGlb(fbx, Path.Combine(dir, "3.glb"));

        Assert.Equal(2, inner.FbxCalls);
        Assert.Equal(File.ReadAllBytes(Path.Combine(dir, "1.glb")), File.ReadAllBytes(Path.Combine(dir, "2.glb")));
    }

    /// <summary>A skeleton far from the game's: twelve bones turned 20°.</summary>
    private static Dictionary<string, RigOffset> OtherSpecies() =>
        Rig([.. new[] { "Hip", "Neck1", "Neck2", "Neck3", "Head", "Tail1", "Tail2", "Tail3", "Leg.L", "Shin.L", "Leg.R", "Shin.R" }.Select(b => Turn(b, Vector3.UnitZ, 20))]);

    [Fact]
    public void The_other_species_warning_names_a_mods_own_species_by_its_name_and_a_game_species_by_its_display_name()
    {
        using var s = Make();
        var key = s.Mod.AddSpecies(s.Species, "mirabilis", Carch, "Mirabilis", "Tyrantus", "mirabilis").Key(s.Mod.Id);
        var glb = Export(s.Prefab, OtherSpecies());

        Assert.Contains(s.Preview(glb, key).Warnings, w => w.StartsWith("This model's skeleton is very different from Mirabilis's", StringComparison.Ordinal));
        var game = s.Species.Single(x => x.SpeciesId == Carch).Name;
        Assert.Contains(s.Preview(glb).Warnings, w => w.StartsWith($"This model's skeleton is very different from {game}'s", StringComparison.Ordinal));
    }

    [Fact]
    public void A_kept_rig_edit_that_breaks_a_rule_refuses_the_model_and_says_how_to_clear_it()
    {
        using var s = Make();
        // A rig edit saved earlier, on a bone the species no longer has (renamed by a game update).
        s.Mod.SetRig(s.Install, Index(), s.Species, s.Reader, Carch, null, Rig(Move("Wing.L", 0, 0.1f, 0)));
        var glb = Export(s.Prefab, null);
        var before = File.ReadAllText(Path.Combine(s.Mod.Dir, "mod.json"));

        var ex = Assert.Throws<TyrantException>(() => s.Replace(glb));

        Assert.Equal(TyrantErrorCode.ModInvalid, ex.Code);
        Assert.Contains("the rig edit already on Carcharodontosaurus breaks a rule, not the model", ex.Message);
        Assert.Contains("Wing.L", ex.Message);
        Assert.Contains("Clear rig edit on the model page, or 'tyrant mod rig big-carch \"Carcharodontosaurus\" --clear'", ex.Message);
        Assert.Equal(before, File.ReadAllText(Path.Combine(s.Mod.Dir, "mod.json")));
    }
}
