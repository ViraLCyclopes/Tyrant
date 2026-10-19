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
    public void Fur_grow_is_saved_and_survives_removing_the_fur_mask()
    {
        var (game, ws, mod) = ModEditTests.Setup();
        using var _ = game;
        mod = ModEditTests.WithSkins(ws, mod);
        var png = ModEditTests.Png(Path.Combine(ws.Dir, "src"));

        mod.SetSkinMaterials("blue", """{"Titanoboa":{"fur":{"grow":true}}}""");
        Assert.True(ModProject.Open(ws, mod.Id).SkinMaterial("blue", "Titanoboa")!.Fur!.Grow);
        Assert.Contains("\"grow\": true", File.ReadAllText(Path.Combine(mod.Dir, "materials", "skins", "blue", MaterialFolders.FileName)));

        mod.SetMaterialFile("blue", null, "Titanoboa", "furMask", png);
        mod.SetMaterialFile("blue", null, "Titanoboa", "furMask", null);
        var fur = ModProject.Open(ws, mod.Id).SkinMaterial("blue", "Titanoboa")!.Fur!;
        Assert.True(fur.Grow); // the block is not empty: grow stays
        Assert.Null(fur.Mask);
        Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() => mod.SetSkinMaterials("blue", """{"Titanoboa":{"fur":{"grow":1}}}""")).Code);
    }

    [Fact]
    public void Skin_materials_are_saved_to_their_folder_not_mod_json()
    {
        var (game, ws, mod) = ModEditTests.Setup();
        using var _ = game;
        mod = ModEditTests.WithSkins(ws, mod);

        mod.SetSkinMaterials("blue", Block);

        Assert.DoesNotContain("\"materials\"", File.ReadAllText(Path.Combine(mod.Dir, ModManifest.FileName)));
        var file = Path.Combine(mod.Dir, "materials", "skins", "blue", MaterialFolders.FileName);
        Assert.True(File.Exists(file));
        Assert.Contains("\"strength\"", File.ReadAllText(file));

        mod.SetSkinMaterials("blue", null);
        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(Path.Combine(mod.Dir, "materials")));
    }

    [Fact]
    public void A_bad_material_file_is_a_load_error_like_a_bad_mod_json()
    {
        var (game, ws, mod) = ModEditTests.Setup();
        using var _ = game;
        mod = ModEditTests.WithSkins(ws, mod);
        mod.SetSkinMaterials("blue", Block);
        File.WriteAllText(Path.Combine(mod.Dir, "materials", "skins", "blue", MaterialFolders.FileName),
            """{"format":1,"materials":{"Titanoboa":{"iridescence":{"strength":5}}}}""");

        var ex = Assert.Throws<TyrantException>(() => ModProject.Open(ws, mod.Id));

        Assert.Equal(TyrantErrorCode.ModInvalid, ex.Code);
        Assert.StartsWith("Mod 'red-spot' cannot be read: materials/skins/blue/materials.json: ", ex.Message);
    }

    [Fact]
    public void Species_materials_need_no_models_entry()
    {
        var (game, ws, mod) = ModEditTests.Setup();
        using var _ = game;

        mod.SetSpeciesMaterials(Index, Species, "titanoboa", Block);

        var reopened = ModProject.Open(ws, mod.Id);
        Assert.Empty(reopened.Manifest.Models);
        Assert.Equal("Titanoboa", Assert.Single(reopened.Manifest.SpeciesMaterials).Key);
        Assert.True(File.Exists(Path.Combine(mod.Dir, "materials", "species", "Titanoboa", MaterialFolders.FileName)));
        Assert.Equal(0.3f, reopened.SpeciesMaterial("Titanoboa", "Titanoboa")!.Iridescence!.Strength!.Normal);

        Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() =>
            mod.SetSpeciesMaterials(Index, Species, "Titanoboa", """{"Titanoboa":{"iridescence":{"mask":{"from":"extra"}}}}""")).Code);

        Assert.Throws<TyrantException>(() => mod.RemoveModel("Titanoboa", null)); // no model file to remove

        mod.SetSpeciesMaterials(Index, Species, "Titanoboa", null);
        Assert.Empty(ModProject.Open(ws, mod.Id).Manifest.SpeciesMaterials);
        Assert.False(Directory.Exists(Path.Combine(mod.Dir, "materials")));
    }

    [Fact]
    public void Vanilla_skin_materials_are_saved_and_cleared()
    {
        var (game, ws, mod) = ModEditTests.Setup();
        using var _ = game;
        var png = ModEditTests.Png(Path.Combine(ws.Dir, "src"));
        var key = SkinMaps.VanillaKey("Titanoboa", new VanillaSkin(1, "Alt 1/2", new Dictionary<string, string>(), new Dictionary<string, string>()));
        Assert.Equal("Titanoboa/Alt 1/2", key);

        // A vanilla skin may take its mask from one of its own maps: the game has them.
        mod.SetVanillaSkinMaterials("Titanoboa", "Alt 1/2", """{"Titanoboa":{"iridescence":{"strength":0.3,"mask":{"from":"extra"}}}}""");

        var folder = Path.Combine(mod.Dir, "materials", "vanilla-skins", "Titanoboa-Alt 1_2");
        Assert.True(File.Exists(Path.Combine(folder, MaterialFolders.FileName)));
        var reopened = ModProject.Open(ws, mod.Id);
        Assert.Equal("extra", reopened.VanillaSkinMaterials("titanoboa/alt 1/2")!["Titanoboa"].Iridescence!.Mask!.From);

        var owner = new MaterialOwner(MaterialOwnerKind.VanillaSkin, null, "Titanoboa", "Alt 1/2");
        Assert.Equal("materials/vanilla-skins/Titanoboa-Alt 1_2/fur-mask.png", reopened.SetMaterialFile(owner, "Titanoboa", "furMask", png));
        Assert.Equal("materials/vanilla-skins/Titanoboa-Alt 1_2/fur-infant-mask.png", reopened.SetMaterialFile(owner, "Titanoboa", "furInfantMask", png));
        var fur = ModProject.Open(ws, mod.Id).VanillaSkinMaterials(key)!["Titanoboa"].Fur!;
        Assert.Equal("materials/vanilla-skins/Titanoboa-Alt 1_2/fur-mask.png", fur.Mask!.File);
        Assert.Equal("materials/vanilla-skins/Titanoboa-Alt 1_2/fur-infant-mask.png", fur.InfantMask!.File);

        // A name that sanitizes alike never shares those images.
        var alike = new MaterialOwner(MaterialOwnerKind.VanillaSkin, null, "Titanoboa", "Alt 1_2");
        Assert.Equal("materials/vanilla-skins/Titanoboa-Alt 1_2-2/fur-mask.png", reopened.SetMaterialFile(alike, "Titanoboa", "furMask", png));
        reopened.SetVanillaSkinMaterials("Titanoboa", "Alt 1_2", null);

        Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() => reopened.SetVanillaSkinMaterials("Titano/boa", "Alt 1", Block)).Code);
        Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() => reopened.SetVanillaSkinMaterials("Titanoboa", " ", Block)).Code);
        Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() => reopened.SetMaterialFile(owner, "Titanoboa", "fur", png)).Code);

        reopened.SetVanillaSkinMaterials("Titanoboa", "Alt 1/2", null);
        Assert.Null(ModProject.Open(ws, mod.Id).VanillaSkinMaterials(key));
        Assert.False(File.Exists(Path.Combine(folder, MaterialFolders.FileName)));
        Assert.True(File.Exists(Path.Combine(folder, "fur-mask.png"))); // images stay, for undo
    }

    [Fact]
    public void Undo_snapshot_restores_material_files()
    {
        var (game, ws, mod) = ModEditTests.Setup();
        using var _ = game;
        mod = ModEditTests.WithSkins(ws, mod);
        mod.SetSkinMaterials("blue", Block);
        var manifest = File.ReadAllText(Path.Combine(mod.Dir, ModManifest.FileName));
        var materials = mod.MaterialFileTexts();
        Assert.Equal(["materials/skins/blue/materials.json"], materials.Keys.ToArray());
        var before = mod.Revision();

        mod.SetSkinMaterials("blue", """{"Titanoboa":{"iridescence":{"strength":0.9}}}""");
        mod.SetVanillaSkinMaterials("Titanoboa", "Alt 1", Block);
        Assert.NotEqual(before, mod.Revision()); // material files are part of the revision

        var restored = ModProject.RestoreSnapshot(ws, mod.Id, manifest, new Dictionary<string, string>(), mod.Revision(), materials);

        Assert.Equal(before, restored.Revision());
        Assert.Equal(0.3f, restored.SkinMaterial("blue", "Titanoboa")!.Iridescence!.Strength!.Normal);
        Assert.Null(restored.VanillaSkinMaterials("Titanoboa/Alt 1"));
        Assert.False(Directory.Exists(Path.Combine(mod.Dir, "materials", "vanilla-skins")));
        Assert.Equal(0.3f, ModProject.Open(ws, mod.Id).SkinMaterial("blue", "Titanoboa")!.Iridescence!.Strength!.Normal);

        foreach (var name in new[] { "materials/skins/../x/materials.json", "materials/skins/blue/other.json", "materials/blue/materials.json", "prefabs/a.json" })
            Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() => ModProject.RestoreSnapshot(ws, mod.Id, manifest,
                new Dictionary<string, string>(), restored.Revision(), new Dictionary<string, string> { [name] = "{}" })).Code);
    }

    [Fact]
    public void Clearing_species_materials_keeps_the_species_model()
    {
        var (game, _, mod) = ModEditTests.Setup();
        using var _1 = game;
        mod.SetSpeciesMaterials(Index, Species, "Titanoboa", Block);
        mod.Manifest.Models.Add(new ModelReplacement { Target = "Titanoboa", File = "models/t.glb" });

        mod.SetSpeciesMaterials(Index, Species, "Titanoboa", "{}");

        var entry = Assert.Single(mod.Manifest.Models);
        Assert.Empty(mod.Manifest.SpeciesMaterials);
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

        Assert.Equal("materials/skins/blue/fur-mask.png", mod.SetMaterialFile("blue", null, "Titanoboa", "furMask", png));
        Assert.Equal("materials/skins/blue/fur-mask.png", ModProject.Open(ws, mod.Id).SkinMaterial("blue", "Titanoboa")!.Fur!.Mask!.File);
        mod.SetMaterialFile("blue", null, "Titanoboa", "furMask", null);
        Assert.Null(ModProject.Open(ws, mod.Id).Skin("blue").Materials);

        Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() => mod.SetMaterialFile("blue", null, "Titanoboa", "ramp", notPng)).Code);
    }

    [Fact]
    public void A_species_ramp_needs_its_materials_and_goes_in_its_folder()
    {
        var (game, ws, mod) = ModEditTests.Setup();
        using var _ = game;
        var png = ModEditTests.Png(Path.Combine(ws.Dir, "src"));
        Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() => mod.SetMaterialFile(null, "Titanoboa", "Titanoboa", "ramp", png)).Code);

        mod.SetSpeciesMaterials(Index, Species, "Titanoboa", Block);
        Assert.Equal("materials/species/Titanoboa/iridescence-ramp.png", mod.SetMaterialFile(null, "titanoboa", "Titanoboa", "ramp", png));
        Assert.Equal("materials/species/Titanoboa/iridescence-ramp.png",
            ModProject.Open(ws, mod.Id).SpeciesMaterial("Titanoboa", "Titanoboa")!.Iridescence!.Ramp!.File);
        mod.SetMaterialFile(null, "Titanoboa", "Titanoboa", "ramp", null);

        var reopened = ModProject.Open(ws, mod.Id);
        Assert.Null(reopened.SpeciesMaterial("Titanoboa", "Titanoboa")!.Iridescence!.Ramp);
        Assert.Equal(0.3f, reopened.SpeciesMaterial("Titanoboa", "Titanoboa")!.Iridescence!.Strength!.Normal); // the strength stays
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
        ModEditTests.Png(Path.Combine(mod.Dir, "materials", "skins", "blue"), "exported.png"); // in its folder, named by nothing

        var removed = mod.RemoveSkin("blue", deleteFiles: true);

        Assert.Contains(ramp, removed.Recycled);
        Assert.Contains(mask, removed.Recycled);
        Assert.Contains("materials/skins/blue/exported.png", removed.Recycled);
        Assert.False(Directory.Exists(Path.Combine(mod.Dir, "materials", "skins", "blue")));
        Assert.False(Directory.Exists(Path.Combine(mod.Dir, "materials"))); // nothing else in it
    }

    [Fact]
    public void Preview_layers_and_files()
    {
        var (game, ws, mod) = ModEditTests.Setup();
        using var _ = game;
        mod = ModEditTests.WithSkins(ws, mod);
        mod.Manifest.SpeciesMaterials["Carcharodontosaurus"] = MaterialOverrides.Parse(
            Json.Parse("""{"Carch":{"iridescence":{"ramp":{"stops":["#ff0000","#0000ff"]}},"settings":{"_Smoothness":0.7}}}"""), "x", false)!;
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
    public void A_vanilla_skins_preview_sits_on_its_species_and_takes_masks_from_its_own_maps()
    {
        var (game, ws, mod) = ModEditTests.Setup();
        using var _ = game;
        mod.Manifest.SpeciesMaterials["Carcharodontosaurus"] = MaterialOverrides.Parse(
            Json.Parse("""{"Carch":{"iridescence":{"ramp":{"stops":["#ff0000","#0000ff"]}}}}"""), "x", false)!;
        var extra = Path.Combine(ws.Dir, "game", "extra.png");
        Directory.CreateDirectory(Path.GetDirectoryName(extra)!);
        var pixels = new byte[4 * 4 * 4];
        for (var p = 0; p < pixels.Length; p += 4) { pixels[p + 1] = 120; pixels[p + 3] = 255; }
        using (var stream = File.Create(extra))
            new StbImageWriteSharp.ImageWriter().WritePng(pixels, 4, 4, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        var draft = MaterialOverrides.Parse(Json.Parse("""{"Carch":{"iridescence":{"strength":0.4,"mask":{"from":"extra","channel":"g"}}}}"""), "x", true)!["Carch"];

        var (below, sources) = MaterialPreview.Inherited(mod, null, null, "Carch", vanillaSkin: "Carcharodontosaurus/Alt 1");
        Assert.NotNull(below?.Iridescence?.Ramp);
        Assert.Equal("species", sources["iridescence.ramp"]);

        var result = MaterialPreview.Build(mod, null, null, "Carch", draft, "male", null, Path.Combine(ws.Dir, "preview"), _ => null,
            vanillaSkin: "Carcharodontosaurus/Alt 1", vanillaMap: slot => slot == "extra" ? extra : null);

        Assert.True(result.On);
        var mask = ImageResult.FromMemory(File.ReadAllBytes(result.MaskFile!), ColorComponents.RedGreenBlueAlpha);
        Assert.Equal((120, 120, 120), (mask.Data[0], mask.Data[1], mask.Data[2]));
    }

    [Fact]
    public void A_mod_skin_without_its_own_map_takes_the_mask_from_its_base_skins_map_like_the_game()
    {
        var (game, ws, mod) = ModEditTests.Setup();
        using var _ = game;
        mod = ModEditTests.WithSkins(ws, mod);
        mod.Manifest.SpeciesMaterials["Carcharodontosaurus"] = MaterialOverrides.Parse(
            Json.Parse("""{"Carch":{"iridescence":{"ramp":{"stops":["#ff0000","#0000ff"]}}}}"""), "x", false)!;
        var extra = Path.Combine(ws.Dir, "game", "base-extra.png");
        Directory.CreateDirectory(Path.GetDirectoryName(extra)!);
        var pixels = new byte[4 * 4 * 4];
        for (var p = 0; p < pixels.Length; p += 4) { pixels[p + 1] = 90; pixels[p + 3] = 255; }
        using (var stream = File.Create(extra))
            new StbImageWriteSharp.ImageWriter().WritePng(pixels, 4, 4, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        Assert.False(mod.Skin("blue").Male?.ContainsKey("extra") == true); // the mod has no extra map for this skin
        var draft = MaterialOverrides.Parse(Json.Parse("""{"Carch":{"iridescence":{"strength":0.4,"mask":{"from":"extra","channel":"g"}}}}"""), "x", true)!["Carch"];

        var result = MaterialPreview.Build(mod, "blue", null, "Carch", draft, "male", null, Path.Combine(ws.Dir, "preview"), _ => null,
            vanillaMap: slot => slot == "extra" ? extra : null);

        Assert.True(result.On);
        var mask = ImageResult.FromMemory(File.ReadAllBytes(result.MaskFile!), ColorComponents.RedGreenBlueAlpha);
        Assert.Equal((90, 90, 90), (mask.Data[0], mask.Data[1], mask.Data[2]));
    }

    [Fact]
    public void A_game_ramp_or_mask_previews_the_game_material_s_own_over_a_lower_layer()
    {
        var (game, ws, mod) = ModEditTests.Setup();
        using var _ = game;
        mod = ModEditTests.WithSkins(ws, mod);
        mod.Manifest.SpeciesMaterials["Carcharodontosaurus"] = MaterialOverrides.Parse(
            Json.Parse("""{"Carch":{"iridescence":{"ramp":{"stops":["#ff0000","#0000ff"]},"mask":"everywhere"}}}"""), "x", false)!;
        var outDir = Path.Combine(ws.Dir, "preview");
        var rampPng = ModEditTests.Png(Path.Combine(ws.Dir, "game"), "ramp.png");
        var maskPng = ModEditTests.Png(Path.Combine(ws.Dir, "game"), "mask.png");
        var rampAsset = new AssetRecord("m.bundle", 7, "Texture2D", "Ramp", null, "feed", null);
        var maskAsset = new AssetRecord("m.bundle", 8, "Texture2D", "Mask", null, "beef", null);
        var gameMaterial = new AnimalMaterial("Carch", new Dictionary<string, float>(), new Dictionary<string, float[]>(), rampAsset, maskAsset);
        var draft = MaterialOverrides.Parse(Json.Parse("""{"Carch":{"iridescence":{"strength":0.4,"ramp":"game","mask":"game"}}}"""), "x", true)!["Carch"];

        var result = MaterialPreview.Build(mod, "blue", null, "Carch", draft, "male", gameMaterial, outDir, a => a == rampAsset ? rampPng : maskPng);

        Assert.True(result.On);
        Assert.Equal(rampPng, result.RampFile);
        Assert.Equal(maskPng, result.MaskFile);
        mod.SetSkinMaterials("blue", """{"Carch":{"iridescence":{"ramp":"game"},"fur":{"mask":"game","infantMask":"game"}}}"""); // saved and read back
        Assert.True(ModProject.Open(ws, mod.Id).SkinMaterial("blue", "Carch")!.Fur!.InfantMask!.Game);
    }

    [Fact]
    public void A_species_ramp_or_mask_previews_that_species_material_s_own()
    {
        var (game, ws, mod) = ModEditTests.Setup();
        using var _ = game;
        mod = ModEditTests.WithSkins(ws, mod);
        var outDir = Path.Combine(ws.Dir, "preview");
        var ownRamp = ModEditTests.Png(Path.Combine(ws.Dir, "game"), "own-ramp.png");
        var microRamp = ModEditTests.Png(Path.Combine(ws.Dir, "game"), "micro-ramp.png");
        var microMask = ModEditTests.Png(Path.Combine(ws.Dir, "game"), "micro-mask.png");
        var ownAsset = new AssetRecord("m.bundle", 7, "Texture2D", "Ramp", null, "feed", null);
        var rampAsset = new AssetRecord("micro.bundle", 1, "Texture2D", "MicroRamp", null, "aaaa", null);
        var maskAsset = new AssetRecord("micro.bundle", 2, "Texture2D", "MicroMask", null, "bbbb", null);
        var gameMaterial = new AnimalMaterial("Carch", new Dictionary<string, float>(), new Dictionary<string, float[]>(), ownAsset, null);
        var micro = new AnimalMaterial("Microraptor", new Dictionary<string, float>(), new Dictionary<string, float[]>(), rampAsset, maskAsset);
        var draft = MaterialOverrides.Parse(Json.Parse("""{"Carch":{"iridescence":{"strength":0.4,"ramp":{"species":"Microraptor"},"mask":{"species":"Microraptor"}}}}"""), "x", true)!["Carch"];
        string? Export(AssetRecord a) => a == rampAsset ? microRamp : a == maskAsset ? microMask : a == ownAsset ? ownRamp : null;

        var result = MaterialPreview.Build(mod, "blue", null, "Carch", draft, "male", gameMaterial, outDir, Export,
            speciesMaterial: id => id == "Microraptor" ? micro : null);

        Assert.True(result.On);
        Assert.Equal(microRamp, result.RampFile);
        Assert.Equal(microMask, result.MaskFile);
        var unknown = MaterialOverrides.Parse(Json.Parse("""{"Carch":{"iridescence":{"strength":0.4,"ramp":{"species":"Nope"}}}}"""), "x", true)!["Carch"];
        var fallback = MaterialPreview.Build(mod, "blue", null, "Carch", unknown, "male", gameMaterial, outDir, Export, speciesMaterial: _ => null);
        Assert.Equal(ownRamp, fallback.RampFile); // as in the game: a species that cannot be read leaves the material's own
    }

    [Fact]
    public void Preview_without_a_mod_strength_shows_the_vanilla_skins_own_iridescence()
    {
        var (game, ws, mod) = ModEditTests.Setup();
        using var _ = game;
        mod = ModEditTests.WithSkins(ws, mod);
        var outDir = Path.Combine(ws.Dir, "preview");
        var rampPng = ModEditTests.Png(Path.Combine(ws.Dir, "game"), "ramp.png");
        var rampAsset = new AssetRecord("m.bundle", 7, "Texture2D", "MicroraptorRamp", null, "feed", null);
        var gameMaterial = new AnimalMaterial("Carch", new Dictionary<string, float>(), new Dictionary<string, float[]>(), rampAsset, null);

        var vanilla = MaterialPreview.Build(mod, "blue", null, "Carch", null, "male", gameMaterial, outDir, _ => rampPng, gameStrength: 0.25f);
        Assert.True(vanilla.On);
        Assert.Equal(0.25f, vanilla.Strength);
        Assert.Equal(rampPng, vanilla.RampFile);

        var off = MaterialOverrides.Parse(Json.Parse("""{"Carch":{"iridescence":{"strength":0}}}"""), "x", true)!["Carch"];
        Assert.False(MaterialPreview.Build(mod, "blue", null, "Carch", off, "male", gameMaterial, outDir, _ => rampPng, gameStrength: 0.25f).On);
        Assert.False(MaterialPreview.Build(mod, "blue", null, "Carch", null, "male", gameMaterial, outDir, _ => rampPng, gameStrength: null).On);
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
