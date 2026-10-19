using StbImageSharp;
using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Mods;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class ModMaterialsTests
{
    private static readonly AssetRecord TitanPrefab = new("t.bundle", 1, "GameObject", "Titanoboa", "Assets/Titanoboa.prefab", "abc123", null);
    private static readonly AssetIndex Index = new() { Assets = [TitanPrefab] };
    private static readonly IReadOnlyList<SpeciesSkins> Species = [new SpeciesSkins("Titanoboa", false, []) { PrefabGuid = "abc123" }];

    private const string Block = """{"Titanoboa":{"iridescence":{"strength":0.3}}}""";

    [Fact]
    public void Skin_materials_are_validated_saved_and_cleared()
    {
        var (game, ws, mod) = ModEditTests.Setup();
        using var _ = game;
        mod = ModEditTests.WithSkins(ws, mod);

        mod.SetSkinMaterials("blue", Block);

        Assert.Equal(0.3f, ModProject.Open(ws, mod.Id).Skin("blue").Materials!["Titanoboa"].Iridescence!.Strength!.Normal);
        var bad = Assert.Throws<TyrantException>(() => mod.SetSkinMaterials("blue", """{"Titanoboa":{"iridescence":{"strength":2}}}"""));
        Assert.Equal(TyrantErrorCode.ModInvalid, bad.Code);
        Assert.StartsWith("The materials of 'blue' cannot be saved: ", bad.Message);
        Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() => mod.SetSkinMaterials("blue", "{not json")).Code);
        Assert.NotNull(mod.Skin("blue").Materials); // refused edits change nothing

        mod.SetSkinMaterials("blue", "{}");
        Assert.Null(ModProject.Open(ws, mod.Id).Skin("blue").Materials);
        mod.SetSkinMaterials("blue", Block);
        mod.SetSkinMaterials("blue", null);
        Assert.Null(ModProject.Open(ws, mod.Id).Skin("blue").Materials);
    }

    [Fact]
    public void Species_materials_live_in_a_models_entry_that_stays_while_it_has_them()
    {
        var (game, ws, mod) = ModEditTests.Setup();
        using var _ = game;

        mod.SetSpeciesMaterials(Index, Species, "titanoboa", Block);

        var entry = Assert.Single(ModProject.Open(ws, mod.Id).Manifest.Models);
        Assert.Equal("Titanoboa", entry.Target);
        Assert.Equal("", entry.File);
        Assert.Equal("Assets/Titanoboa.prefab", entry.Key);
        Assert.NotNull(entry.Materials);
        Assert.Equal(0.3f, mod.SpeciesMaterial("Titanoboa", "Titanoboa")!.Iridescence!.Strength!.Normal);

        Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() =>
            mod.SetSpeciesMaterials(Index, Species, "Titanoboa", """{"Titanoboa":{"iridescence":{"mask":{"from":"extra"}}}}""")).Code);

        Assert.Throws<TyrantException>(() => mod.RemoveModel("Titanoboa", null)); // no model file to remove
        Assert.Single(mod.Manifest.Models);

        mod.SetSpeciesMaterials(Index, Species, "Titanoboa", null);
        Assert.Empty(ModProject.Open(ws, mod.Id).Manifest.Models);
    }

    [Fact]
    public void Clearing_species_materials_keeps_an_entry_with_a_model()
    {
        var (game, _, mod) = ModEditTests.Setup();
        using var _1 = game;
        mod.SetSpeciesMaterials(Index, Species, "Titanoboa", Block);
        mod.Manifest.Models[0].File = "models/t.glb";

        mod.SetSpeciesMaterials(Index, Species, "Titanoboa", "{}");

        var entry = Assert.Single(mod.Manifest.Models);
        Assert.Null(entry.Materials);
        Assert.Equal("models/t.glb", entry.File);
    }

    [Fact]
    public void An_imported_ramp_or_mask_is_copied_into_the_mod_and_named_in_the_block()
    {
        var (game, ws, mod) = ModEditTests.Setup();
        using var _ = game;
        mod = ModEditTests.WithSkins(ws, mod);
        var png = ModEditTests.Png(Path.Combine(ws.Dir, "src"));
        var notPng = Path.Combine(ws.Dir, "src", "x.png");
        File.WriteAllText(notPng, "nope");

        var file = mod.SetMaterialFile("blue", null, "Titanoboa", "ramp", png);

        Assert.Equal("materials/skins/blue/iridescence-ramp.png", file);
        Assert.True(File.Exists(Path.Combine(mod.Dir, file)));
        Assert.Equal(file, ModProject.Open(ws, mod.Id).Skin("blue").Materials!["Titanoboa"].Iridescence!.Ramp!.File);

        Assert.Equal("materials/skins/blue/iridescence-mask.png", mod.SetMaterialFile("blue", null, "Titanoboa", "mask", png));
        Assert.Equal("", mod.SetMaterialFile("blue", null, "Titanoboa", "ramp", null));
        var iridescence = mod.Skin("blue").Materials!["Titanoboa"].Iridescence!;
        Assert.Null(iridescence.Ramp);
        Assert.NotNull(iridescence.Mask);
        mod.SetMaterialFile("blue", null, "Titanoboa", "mask", null);
        Assert.Null(ModProject.Open(ws, mod.Id).Skin("blue").Materials); // nothing left: null, not {}

        Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() => mod.SetMaterialFile("blue", null, "Titanoboa", "ramp", notPng)).Code);
    }

    [Fact]
    public void A_species_ramp_needs_its_materials_and_goes_in_its_entry()
    {
        var (game, ws, mod) = ModEditTests.Setup();
        using var _ = game;
        var png = ModEditTests.Png(Path.Combine(ws.Dir, "src"));
        Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() => mod.SetMaterialFile(null, "Titanoboa", "Titanoboa", "ramp", png)).Code);

        mod.SetSpeciesMaterials(Index, Species, "Titanoboa", Block);
        Assert.Equal("materials/species/Titanoboa/iridescence-ramp.png", mod.SetMaterialFile(null, "titanoboa", "Titanoboa", "ramp", png));
        mod.SetMaterialFile(null, "Titanoboa", "Titanoboa", "ramp", null);

        Assert.Equal(0.3f, mod.SpeciesMaterial("Titanoboa", "Titanoboa")!.Iridescence!.Strength!.Normal); // the strength stays
    }

    [Fact]
    public void A_skin_and_a_species_with_the_same_id_never_share_an_image()
    {
        var (game, ws, mod) = ModEditTests.Setup();
        using var _ = game;
        mod = ModEditTests.WithSkins(ws, mod);
        var png = ModEditTests.Png(Path.Combine(ws.Dir, "src"));
        mod.SetSpeciesMaterials(Index, Species, "Titanoboa", Block);
        mod.Manifest.Skins[0].Id = "titanoboa"; // a skin id equal to a species id, any case
        mod.Save();

        var skinRamp = mod.SetMaterialFile("titanoboa", null, "Titanoboa", "ramp", png);
        var speciesRamp = mod.SetMaterialFile(null, "Titanoboa", "Titanoboa", "ramp", png);

        Assert.NotEqual(skinRamp.ToLowerInvariant(), speciesRamp.ToLowerInvariant());
    }

    [Fact]
    public void Removing_a_skin_with_its_files_takes_its_material_images_and_their_folder()
    {
        var (game, ws, mod) = ModEditTests.Setup();
        using var _ = game;
        mod = ModEditTests.WithSkins(ws, mod);
        var png = ModEditTests.Png(Path.Combine(ws.Dir, "src"));
        var ramp = mod.SetMaterialFile("blue", null, "Carch", "ramp", png);
        var mask = mod.SetMaterialFile("blue", null, "Carch", "mask", png);

        var removed = mod.RemoveSkin("blue", deleteFiles: true);

        Assert.Contains(ramp, removed.Recycled);
        Assert.Contains(mask, removed.Recycled);
        Assert.False(Directory.Exists(Path.Combine(mod.Dir, "materials", "skins", "blue")));
        Assert.False(Directory.Exists(Path.Combine(mod.Dir, "materials"))); // nothing else in it
    }

    [Fact]
    public void Preview_layers_and_files()
    {
        var (game, ws, mod) = ModEditTests.Setup();
        using var _ = game;
        mod = ModEditTests.WithSkins(ws, mod);
        mod.Manifest.Models.Add(new ModelReplacement { Target = "Carcharodontosaurus", Key = "k", File = "", Materials = MaterialOverrides.Parse(
            Json.Parse("""{"Carch":{"iridescence":{"ramp":{"stops":["#ff0000","#0000ff"]}},"settings":{"_Smoothness":0.7}}}"""), "x", false) });
        var extra = Path.Combine(mod.Dir, "skins", "blue", "male_extra.png");
        var pixels = new byte[4 * 4 * 4];
        for (var p = 0; p < pixels.Length; p += 4) { pixels[p + 1] = 200; pixels[p + 3] = 255; }
        using (var stream = File.Create(extra))
            new StbImageWriteSharp.ImageWriter().WritePng(pixels, 4, 4, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        mod.Skin("blue").Male!["extra"] = "skins/blue/male_extra.png";
        var draft = MaterialOverrides.Parse(Json.Parse("""{"Carch":{"iridescence":{"strength":0.4,"mask":{"from":"extra","channel":"g"}}}}"""), "x", true)!["Carch"];
        var outDir = Path.Combine(ws.Dir, "preview");

        var (below, sources) = MaterialPreview.Inherited(mod, "blue", null, "Carch");
        Assert.Equal("species", sources["iridescence.ramp"]);
        Assert.Equal("species", sources["_Smoothness"]);
        Assert.NotNull(below);

        var result = MaterialPreview.Build(mod, "blue", null, "Carch", draft, "male", null, outDir, _ => null);

        Assert.True(result.On);
        Assert.Equal(0.4f, result.Strength);
        Assert.Equal(0.7f, result.Numbers["_Smoothness"]);
        var ramp = ImageResult.FromMemory(File.ReadAllBytes(result.RampFile!), ColorComponents.RedGreenBlueAlpha);
        Assert.Equal((32, 32), (ramp.Width, ramp.Height));
        Assert.Equal((255, 0, 0), (ramp.Data[0], ramp.Data[1], ramp.Data[2]));
        var last = 31 * 4;
        Assert.Equal((0, 0, 255), (ramp.Data[last], ramp.Data[last + 1], ramp.Data[last + 2]));
        var mask = ImageResult.FromMemory(File.ReadAllBytes(result.MaskFile!), ColorComponents.RedGreenBlueAlpha);
        Assert.Equal((200, 200, 200), (mask.Data[0], mask.Data[1], mask.Data[2]));

        var off = MaterialPreview.Build(mod, "green", null, "Other", null, "male", null, outDir, _ => null);
        Assert.False(off.On);
        Assert.Null(off.RampFile);
        Assert.False(MaterialPreview.Build(mod, "blue", null, "Carch", null, "male", null, outDir, _ => null).On); // no strength
    }

    [Fact]
    public void Preview_smoothness_is_a_multiplier_on_the_extra_map_only_when_the_mod_sets_it()
    {
        var (game, ws, mod) = ModEditTests.Setup();
        using var _ = game;
        mod = ModEditTests.WithSkins(ws, mod);
        var outDir = Path.Combine(ws.Dir, "preview");
        var gameMaterial = new AnimalMaterial("Carch", new Dictionary<string, float> { ["_Smoothness"] = 1.5f }, new Dictionary<string, float[]>(), null, null);

        // Nothing set: the extra map's smoothness is left alone, whatever the game material says.
        var none = MaterialPreview.Build(mod, "blue", null, "Carch", null, "male", gameMaterial, outDir, _ => null);
        Assert.False(none.Numbers.ContainsKey("_Smoothness"));

        var draft = MaterialOverrides.Parse(Json.Parse("""{"Carch":{"settings":{"_Smoothness":0.6}}}"""), "x", true)!["Carch"];
        var set = MaterialPreview.Build(mod, "blue", null, "Carch", draft, "male", gameMaterial, outDir, _ => null);
        Assert.Equal(0.4f, set.Numbers["_Smoothness"], 5);

        // No usable game value: the override itself.
        var zero = gameMaterial with { Floats = new Dictionary<string, float> { ["_Smoothness"] = 0f } };
        Assert.Equal(0.6f, MaterialPreview.Build(mod, "blue", null, "Carch", draft, "male", zero, outDir, _ => null).Numbers["_Smoothness"], 5);
    }
}
