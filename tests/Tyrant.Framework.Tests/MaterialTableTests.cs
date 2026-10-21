using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class MaterialTableTests
{
    private static LoadedMod Mod(string id, string json, params (string Path, string Json)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), id);
        Directory.CreateDirectory(dir);
        foreach (var (path, content) in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dir, path))!);
            File.WriteAllText(Path.Combine(dir, path), content);
        }
        var manifest = ModManifest.Parse(json);
        MaterialFolders.Load(manifest, dir, new List<string>());
        return new LoadedMod(manifest, dir);
    }

    private static string Skin(string id) =>
        "{\"id\":\"" + id + "\",\"species\":\"Titanoboa\",\"name\":\"S\",\"base\":\"0\",\"male\":{\"diffuse\":\"a.png\"}}";

    private static (string, string) SkinFile(string id, string materials) =>
        ($"materials/skins/{id}/materials.json", "{\"format\":1,\"materials\":" + materials + "}");

    private static (string, string) SpeciesFile(string species, string materials) =>
        ($"materials/species/{species}/materials.json", "{\"format\":1,\"species\":\"" + species + "\",\"materials\":" + materials + "}");

    private static (string, string) VanillaFile(string species, string skin, string materials) =>
        ($"materials/vanilla-skins/{species}-{skin}/materials.json", "{\"format\":1,\"species\":\"" + species + "\",\"skin\":\"" + skin + "\",\"materials\":" + materials + "}");

    private static LoadedMod Species(string mod, string material, string smoothness, string target = "Titanoboa") =>
        Mod(mod, "{\"format\":1,\"id\":\"" + mod + "\"}",
            SpeciesFile(target, "{\"" + material + "\":{\"settings\":{\"_Smoothness\":" + smoothness + "}}}"));

    [Fact]
    public void Skin_beats_base_skin_beats_species_per_field()
    {
        var a = Mod("mod-a", "{\"format\":1,\"id\":\"mod-a\",\"skins\":[" + Skin("start") + "," + Skin("mine") + "]}",
            SpeciesFile("Titanoboa", """{"Titanoboa":{"iridescence":{"ramp":{"stops":["#ff0000","#0000ff"]},"scale":-0.25},"settings":{"_Smoothness":0.3}}}"""),
            SkinFile("start", """{"Titanoboa":{"iridescence":{"power":-0.3}}}"""),
            SkinFile("mine", """{"Titanoboa":{"iridescence":{"strength":0.35},"settings":{"_RimIntensity":1}}}"""));
        var table = MaterialTable.Build([a], out var messages);

        Assert.Empty(messages);
        Assert.True(table.HasAny);
        var merged = table.For("Titanoboa", "mod-a/mine", "mod-a/start", null, "Titanoboa")!;
        Assert.Equal(0.35f, merged.Iridescence!.Strength!.Normal);
        Assert.Equal(2, merged.Iridescence.Ramp!.Stops!.Count);
        Assert.Equal(-0.3f, merged.Iridescence.Power);
        Assert.Equal(-0.25f, merged.Iridescence.Scale);
        Assert.Equal(0.3f, merged.Settings["_Smoothness"].Number);
        Assert.Equal(1f, merged.Settings["_RimIntensity"].Number);
        Assert.Null(table.For("Titanoboa", "mod-a/mine", "mod-a/start", null, "Other"));
        Assert.Null(table.For("Other", null, null, null, "Titanoboa"));
    }

    [Fact]
    public void Files_resolve_against_their_own_mod()
    {
        var a = Mod("mod-a", """{"format":1,"id":"mod-a"}""", SpeciesFile("Titanoboa", """{"M":{"iridescence":{"mask":"m.png"}}}"""));
        var b = Mod("mod-b", "{\"format\":1,\"id\":\"mod-b\",\"skins\":[" + Skin("sss") + "]}",
            SkinFile("sss", """{"M":{"iridescence":{"ramp":"r.png"}},"N":{"iridescence":{"ramp":"../x.png"}}}"""));
        var table = MaterialTable.Build([a, b], out var messages);

        var merged = table.For("Titanoboa", "mod-b/sss", null, null, "M")!;
        Assert.Equal(Path.GetFullPath(Path.Combine(a.Directory, "m.png")), merged.Iridescence!.Mask!.File);
        Assert.Equal(Path.GetFullPath(Path.Combine(b.Directory, "r.png")), merged.Iridescence.Ramp!.File);
        Assert.Null(table.For("Titanoboa", "mod-b/sss", null, null, "N")?.Iridescence?.Ramp);
        Assert.Contains("mod-b: ../x.png points outside the mod folder; it is ignored.", messages);
        Assert.Equal("r.png", b.Manifest.Skins[0].Materials!["M"].Iridescence!.Ramp!.File); // the manifest is untouched
    }

    [Fact]
    public void The_later_mod_wins_a_species_material_and_the_clash_is_reported()
    {
        var a = Species("mod-a", "Titanoboa", "0.1");
        var b = Species("mod-b", "Titanoboa", "0.9");
        var table = MaterialTable.Build([a, b], out var messages);

        Assert.Equal(0.9f, table.For("Titanoboa", null, null, null, "Titanoboa")!.Settings["_Smoothness"].Number);
        var message = Assert.Single(messages);
        Assert.Contains("'mod-a'", message);
        Assert.Contains("'mod-b'", message);
        var clash = Assert.Single(MaterialTable.Clashes([("mod-a", a.Manifest), ("mod-b", b.Manifest)]));
        Assert.Equal(("mod-a", "mod-b", "Titanoboa", "Titanoboa"), (clash.Loser, clash.Winner, clash.Species, clash.Material));
        Assert.Equal("Titanoboa: its Titanoboa material settings are overridden by 'mod-b' (later in the load order).", clash.Message);

        var otherMaterial = Species("mod-c", "Eyes", "0.5");
        Assert.Empty(MaterialTable.Clashes([("mod-a", a.Manifest), ("mod-c", otherMaterial.Manifest)]));
        var otherSpecies = Species("mod-d", "Titanoboa", "0.5", "Other");
        Assert.Empty(MaterialTable.Clashes([("mod-a", a.Manifest), ("mod-d", otherSpecies.Manifest)]));
    }

    [Fact]
    public void A_vanilla_skin_block_beats_species_per_field()
    {
        var a = Mod("mod-a", """{"format":1,"id":"mod-a"}""",
            SpeciesFile("Titanoboa", """{"Titanoboa":{"iridescence":{"scale":-0.25,"power":-0.1},"settings":{"_Smoothness":0.3}}}"""),
            VanillaFile("Titanoboa", "Skin A", """{"Titanoboa":{"iridescence":{"power":-0.6},"settings":{"_RimIntensity":1}}}"""));
        var table = MaterialTable.Build([a], out var messages);

        Assert.Empty(messages);
        Assert.True(table.CoversVanillaSkin("Titanoboa/Skin A"));
        Assert.False(table.CoversVanillaSkin("Titanoboa/Skin B"));
        var merged = table.For("Titanoboa", null, null, "Titanoboa/Skin A", "Titanoboa")!;
        Assert.Equal(-0.6f, merged.Iridescence!.Power);
        Assert.Equal(-0.25f, merged.Iridescence.Scale);
        Assert.Equal(0.3f, merged.Settings["_Smoothness"].Number);
        Assert.Equal(1f, merged.Settings["_RimIntensity"].Number);
        Assert.Equal(-0.1f, table.For("Titanoboa", null, null, "Titanoboa/Skin B", "Titanoboa")!.Iridescence!.Power);
        Assert.Null(table.For("Titanoboa", null, null, null, "Eyes"));
    }

    [Fact]
    public void A_vanilla_skin_block_does_not_reach_mod_skins_based_on_it()
    {
        var a = Mod("mod-a", "{\"format\":1,\"id\":\"mod-a\",\"skins\":[" + Skin("mine") + "]}",
            SpeciesFile("Titanoboa", """{"Titanoboa":{"settings":{"_Smoothness":0.3}}}"""),
            VanillaFile("Titanoboa", "Skin A", """{"Titanoboa":{"settings":{"_Smoothness":0.9}}}"""));
        var table = MaterialTable.Build([a], out _);

        // a mod skin based on "Skin A" passes its own key and the base skin's key, never the vanilla key
        var merged = table.For("Titanoboa", "mod-a/mine", null, null, "Titanoboa")!;
        Assert.Equal(0.3f, merged.Settings["_Smoothness"].Number);
    }

    [Fact]
    public void Owner_of_is_the_winning_mod()
    {
        var a = Species("mod-a", "Titanoboa", "0.1");
        var b = Species("mod-b", "Titanoboa", "0.9");
        var c = Mod("mod-c", "{\"format\":1,\"id\":\"mod-c\",\"skins\":[" + Skin("mine") + "]}", SkinFile("mine", """{"Titanoboa":{"settings":{"_Smoothness":0.5}}}"""));
        var table = MaterialTable.Build([a, b, c], out _);

        var owner = table.OwnerOf(MaterialOwnerKind.Species, "Titanoboa", "Titanoboa")!;
        Assert.Equal(("mod-b", b.Directory), (owner.ModId, owner.ModDirectory));
        var skinOwner = table.OwnerOf(MaterialOwnerKind.Skin, "mod-c/mine", "Titanoboa")!;
        Assert.Equal(("mod-c", c.Directory), (skinOwner.ModId, skinOwner.ModDirectory));
        Assert.Null(table.OwnerOf(MaterialOwnerKind.Species, "Titanoboa", "Eyes"));
        Assert.Null(table.OwnerOf(MaterialOwnerKind.VanillaSkin, "Titanoboa/Skin A", "Titanoboa"));
    }

    [Fact]
    public void Vanilla_skin_clashes_are_reported()
    {
        var a = Mod("mod-a", """{"format":1,"id":"mod-a"}""", VanillaFile("Titanoboa", "Skin A", """{"Titanoboa":{"settings":{"_Smoothness":0.1}}}"""));
        var b = Mod("mod-b", """{"format":1,"id":"mod-b"}""", VanillaFile("Titanoboa", "Skin A", """{"Titanoboa":{"settings":{"_Smoothness":0.9}}}"""));
        var table = MaterialTable.Build([a, b], out var messages);

        Assert.Equal(0.9f, table.For("Titanoboa", null, null, "Titanoboa/Skin A", "Titanoboa")!.Settings["_Smoothness"].Number);
        Assert.Equal("mod-b", table.OwnerOf(MaterialOwnerKind.VanillaSkin, "Titanoboa/Skin A", "Titanoboa")!.ModId);
        Assert.Single(messages);
        var clash = Assert.Single(MaterialTable.Clashes([("mod-a", a.Manifest), ("mod-b", b.Manifest)]));
        Assert.Equal("Titanoboa/Skin A: its Titanoboa material settings are overridden by 'mod-b' (later in the load order).", clash.Message);
    }

    [Fact]
    public void Vanilla_skin_keys_match_in_any_case_for_lookup_owner_and_clashes()
    {
        var a = Mod("mod-a", """{"format":1,"id":"mod-a"}""", VanillaFile("Titanoboa", "Skin A", """{"Titanoboa":{"settings":{"_Smoothness":0.1}}}"""));
        var b = Mod("mod-b", """{"format":1,"id":"mod-b"}""", VanillaFile("Titanoboa", "skin a", """{"Titanoboa":{"settings":{"_Smoothness":0.9}}}"""));
        var table = MaterialTable.Build([a, b], out var messages);

        Assert.Equal(0.9f, table.For("Titanoboa", null, null, "Titanoboa/Skin A", "Titanoboa")!.Settings["_Smoothness"].Number);
        Assert.Equal("mod-b", table.OwnerOf(MaterialOwnerKind.VanillaSkin, "Titanoboa/Skin A", "Titanoboa")!.ModId);
        Assert.Equal("mod-b", table.OwnerOf(MaterialOwnerKind.VanillaSkin, "Titanoboa/skin a", "Titanoboa")!.ModId);
        Assert.Single(messages);
        Assert.Single(MaterialTable.Clashes([("mod-a", a.Manifest), ("mod-b", b.Manifest)]));
    }

    [Fact]
    public void Fur_masks_merge_per_field_and_resolve_against_their_mod()
    {
        var a = Mod("mod-a", """{"format":1,"id":"mod-a"}""", SpeciesFile("Titanoboa", """{"Fur":{"fur":{"mask":"low.png","infantMask":"infant.png"}}}"""));
        var b = Mod("mod-b", """{"format":1,"id":"mod-b"}""", VanillaFile("Titanoboa", "Skin A", """{"Fur":{"fur":{"mask":"top.png"}},"Bad":{"fur":{"mask":"../x.png"}}}"""));
        var table = MaterialTable.Build([a, b], out var messages);

        var fur = table.For("Titanoboa", null, null, "Titanoboa/Skin A", "Fur")!.Fur!;
        Assert.Equal(Path.GetFullPath(Path.Combine(b.Directory, "top.png")), fur.Mask!.File);
        Assert.Equal(Path.GetFullPath(Path.Combine(a.Directory, "infant.png")), fur.InfantMask!.File);
        Assert.Null(table.For("Titanoboa", null, null, "Titanoboa/Skin A", "Bad"));
        Assert.Contains("mod-b: ../x.png points outside the mod folder; it is ignored.", messages);
        Assert.Equal("top.png", b.Manifest.VanillaSkinMaterials["Titanoboa/Skin A"]["Fur"].Fur!.Mask!.File); // the manifest is untouched
    }

    [Fact]
    public void Fur_grow_merges_per_field_and_a_grow_only_block_covers_the_animal()
    {
        var a = Mod("mod-a", "{\"format\":1,\"id\":\"mod-a\",\"skins\":[" + Skin("bare") + "]}",
            SpeciesFile("Titanoboa", """{"Titanoboa":{"fur":{"grow":true}}}"""),
            SkinFile("bare", """{"Titanoboa":{"fur":{"grow":false}}}"""),
            VanillaFile("Titanoboa", "Skin A", """{"Titanoboa":{"fur":{"mask":"top.png"}}}"""));
        var table = MaterialTable.Build([a], out var messages);

        Assert.Empty(messages);
        Assert.True(table.CoversSpecies("Titanoboa")); // a block holding only grow is not empty
        Assert.True(table.CoversSkin("mod-a", "bare"));
        var species = table.For("Titanoboa", null, null, null, "Titanoboa")!.Fur!;
        Assert.True(species.Grow);
        var vanilla = table.For("Titanoboa", null, null, "Titanoboa/Skin A", "Titanoboa")!.Fur!;
        Assert.True(vanilla.Grow); // from the species, beside the vanilla skin's own mask
        Assert.Equal(Path.GetFullPath(Path.Combine(a.Directory, "top.png")), vanilla.Mask!.File);
        Assert.False(table.For("Titanoboa", "mod-a/bare", null, null, "Titanoboa")!.Fur!.Grow); // the skin's false wins

        var live = new MaterialOverride { Fur = new FurMasks { Grow = true } };
        Assert.True(MaterialTable.MergeLayers(live, null, null, null, null, null, null)!.Fur!.Grow);
        Assert.True(MaterialTable.MergeBelow(LookScope.Animal, null, null, live, null, null, null, null)!.Fur!.Grow);
        Assert.True(MaterialTable.WithFullPaths(live, Path.GetTempPath()).Fur!.Grow);
        Assert.False(MaterialTable.WithFullPaths(live, Path.GetTempPath()).IsEmpty);
    }

    [Fact]
    public void A_game_ramp_or_mask_beats_lower_layers_and_survives_copies()
    {
        var a = Mod("mod-a", "{\"format\":1,\"id\":\"mod-a\",\"skins\":[" + Skin("mine") + "]}",
            SpeciesFile("Titanoboa", """{"Titanoboa":{"iridescence":{"ramp":"r.png","mask":"everywhere"},"fur":{"mask":"f.png"}}}"""),
            SkinFile("mine", """{"Titanoboa":{"iridescence":{"ramp":"game","mask":"game"},"fur":{"mask":"game"}}}"""));
        var table = MaterialTable.Build([a], out var messages);

        Assert.Empty(messages);
        var merged = table.For("Titanoboa", "mod-a/mine", null, null, "Titanoboa")!;
        Assert.True(merged.Iridescence!.Ramp!.Game);
        Assert.True(merged.Iridescence.Mask!.Game);
        Assert.True(merged.Fur!.Mask!.Game);
        var species = table.For("Titanoboa", null, null, null, "Titanoboa")!; // the species alone keeps its own
        Assert.EndsWith("r.png", species.Iridescence!.Ramp!.File);

        var live = new MaterialOverride { Iridescence = new IridescenceSettings { Ramp = new RampSource { Game = true }, Mask = new MaskSource { Game = true } } };
        var below = new MaterialOverride { Iridescence = new IridescenceSettings { Ramp = new RampSource { File = "x.png" }, Mask = new MaskSource { Everywhere = true } } };
        var layered = MaterialTable.MergeLayers(live, null, null, below, null, null, null)!;
        Assert.True(layered.Iridescence!.Ramp!.Game);
        Assert.True(layered.Iridescence.Mask!.Game);
        var copy = MaterialTable.WithFullPaths(live, Path.GetTempPath());
        Assert.True(copy.Iridescence!.Ramp!.Game);
        Assert.True(copy.Iridescence.Mask!.Game);
    }

    [Fact]
    public void A_species_ramp_or_mask_merges_per_field_and_survives_copies()
    {
        var a = Mod("mod-a", "{\"format\":1,\"id\":\"mod-a\",\"skins\":[" + Skin("mine") + "]}",
            SpeciesFile("Titanoboa", """{"Titanoboa":{"iridescence":{"ramp":{"species":"Microraptor"},"mask":"m.png","scale":-0.25}}}"""),
            SkinFile("mine", """{"Titanoboa":{"iridescence":{"mask":{"species":"Oviraptor"}}}}"""));
        var table = MaterialTable.Build([a], out var messages);

        Assert.Empty(messages);
        var merged = table.For("Titanoboa", "mod-a/mine", null, null, "Titanoboa")!;
        Assert.Equal("Microraptor", merged.Iridescence!.Ramp!.Species); // from the species: the skin sets no ramp
        Assert.Equal("Oviraptor", merged.Iridescence.Mask!.Species); // the skin's mask beats the species' file
        Assert.Equal(-0.25f, merged.Iridescence.Scale);
        Assert.EndsWith("m.png", table.For("Titanoboa", null, null, null, "Titanoboa")!.Iridescence!.Mask!.File);

        var live = new MaterialOverride { Iridescence = new IridescenceSettings { Ramp = new RampSource { Species = "Microraptor" }, Mask = new MaskSource { Species = "Microraptor" } } };
        var copy = MaterialTable.WithFullPaths(live, Path.GetTempPath());
        Assert.Equal("Microraptor", copy.Iridescence!.Ramp!.Species);
        Assert.Equal("Microraptor", copy.Iridescence.Mask!.Species);
        Assert.NotSame(live.Iridescence.Ramp, copy.Iridescence.Ramp);
    }

    [Fact]
    public void Merge_does_not_change_its_inputs_and_variation_defaults()
    {
        var top = new MaterialOverride { Iridescence = new IridescenceSettings { Scale = 1f } };
        var low = new MaterialOverride { Iridescence = new IridescenceSettings { Strength = new VariantNumbers { Normal = 0.5f } } };
        var merged = MaterialTable.Merge(top, null, low)!;
        Assert.Equal(1f, merged.Iridescence!.Scale);
        Assert.Equal(0.5f, merged.Iridescence.Strength!.Normal);
        Assert.Null(top.Iridescence.Strength);
        Assert.Equal(IridescenceSettings.DefaultVariation, MaterialTable.EffectiveVariation(merged.Iridescence));
        Assert.Equal(0f, MaterialTable.EffectiveVariation(top.Iridescence));
        Assert.Null(MaterialTable.Merge(null, null));
    }

    [Fact]
    public void Merging_nothing_and_looking_up_an_animal_no_mod_covers_allocate_nothing()
    {
        // Every growth tick of every animal asks: the answer for the animals no mod covers must cost no garbage.
        var table = MaterialTable.Build([Species("mod-a", "Titanoboa", "0.1")], out _);
        var none = new MaterialOverride?[3];
        MaterialTable.Merge(none);
        table.For("Other", null, null, null, "Titanoboa");
        table.For("Titanoboa", null, null, null, "Eyes");

        var before = GC.GetAllocatedBytesForCurrentThread();
        var merged = MaterialTable.Merge(none);
        var other = table.For("Other", null, null, null, "Titanoboa");
        var otherMaterial = table.For("Titanoboa", null, null, null, "Eyes");
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Null(merged);
        Assert.Null(other);
        Assert.Null(otherMaterial);
        Assert.Equal(0, allocated);
    }

    private static MaterialOverride Smooth(float value) => new() { Settings = { ["_Smoothness"] = MaterialValue.OfNumber(value) } };

    [Fact]
    public void Merge_order_puts_the_animal_layer_first()
    {
        // live animal > saved animal > live skin > table skin > base skin > live species > table species
        var layers = Enumerable.Range(0, 7).Select(i => Smooth(i)).ToArray<MaterialOverride?>();
        layers[6]!.Settings["_Length"] = MaterialValue.OfNumber(2);
        for (var top = 0; top < 7; top++)
        {
            var merged = MaterialTable.MergeLayers(layers[0], layers[1], layers[2], layers[3], layers[4], layers[5], layers[6])!;
            Assert.Equal(top, merged.Settings["_Smoothness"].Number);
            Assert.Equal(2f, merged.Settings["_Length"].Number); // the lower layers still fill what the top one leaves out
            layers[top] = null;
        }
        Assert.Null(MaterialTable.MergeLayers(null, null, null, null, null, null, null));
    }

    [Fact]
    public void The_layers_below_a_scope_leave_out_that_scope_and_everything_above_it()
    {
        // live animal > saved animal > live skin > table skin > base skin > live species > table species
        var layers = Enumerable.Range(0, 7).Select(i => Smooth(i)).ToArray<MaterialOverride?>();
        layers[6]!.Settings["_Length"] = MaterialValue.OfNumber(2);
        MaterialOverride? Below(LookScope scope) =>
            MaterialTable.MergeBelow(scope, layers[0], layers[1], layers[2], layers[3], layers[4], layers[5], layers[6]);

        Assert.Equal(2f, Below(LookScope.Animal)!.Settings["_Smoothness"].Number); // from the live skin down
        Assert.Equal(4f, Below(LookScope.Skin)!.Settings["_Smoothness"].Number); // from the base skin down
        Assert.Null(Below(LookScope.Species)); // only the game lies below a species
        Assert.Equal(2f, Below(LookScope.Skin)!.Settings["_Length"].Number);

        layers[4] = layers[5] = layers[6] = null;
        Assert.Null(Below(LookScope.Skin));
    }

    [Fact]
    public void Merging_no_layers_allocates_nothing()
    {
        MaterialTable.MergeLayers(null, null, null, null, null, null, null);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var merged = MaterialTable.MergeLayers(null, null, null, null, null, null, null);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Null(merged);
    }

    [Fact]
    public void Block_is_one_owners_own_entry_and_blocks_lists_them_all()
    {
        var a = Mod("mod-a", "{\"format\":1,\"id\":\"mod-a\",\"skins\":[" + Skin("mine") + "]}",
            SpeciesFile("Titanoboa", """{"Titanoboa":{"settings":{"_Smoothness":0.2}}}"""),
            SkinFile("mine", """{"Titanoboa":{"settings":{"_Smoothness":0.4}}}"""),
            VanillaFile("Titanoboa", "Skin A", """{"Titanoboa":{"settings":{"_Smoothness":0.6}}}"""));
        var table = MaterialTable.Build([a], out _);

        Assert.Equal(0.2f, table.Block(MaterialOwnerKind.Species, "Titanoboa", "Titanoboa")!.Settings["_Smoothness"].Number);
        Assert.Equal(0.4f, table.Block(MaterialOwnerKind.Skin, "mod-a/mine", "Titanoboa")!.Settings["_Smoothness"].Number);
        Assert.Equal(0.6f, table.Block(MaterialOwnerKind.VanillaSkin, "titanoboa/skin a", "Titanoboa")!.Settings["_Smoothness"].Number);
        Assert.Null(table.Block(MaterialOwnerKind.Skin, null, "Titanoboa"));
        Assert.Null(table.Block(MaterialOwnerKind.Species, "Titanoboa", "Other"));
        Assert.Equal(3, table.Blocks.Count());
        Assert.True(table.CoversVanillaSkinsOf("Titanoboa"));
        Assert.True(table.CoversVanillaSkinsOf("TITANOBOA"));
        Assert.False(table.CoversVanillaSkinsOf("Other"));
        Assert.False(table.CoversVanillaSkinsOf(null));
    }

    [Fact]
    public void With_full_paths_resolves_relative_files_against_a_folder_and_keeps_the_input()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tyrant-tests", "looks");
        var absolute = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "elsewhere", "m.png"));
        var block = new MaterialOverride
        {
            Iridescence = new IridescenceSettings { Ramp = new RampSource { File = "Masks/r.png" }, Mask = new MaskSource { File = absolute }, Scale = 1 },
            Fur = new FurMasks { InfantMask = new MaskSource { File = "f.png" } },
            Settings = { ["_Length"] = MaterialValue.OfNumber(1) },
        };

        var copy = MaterialTable.WithFullPaths(block, folder);

        Assert.Equal(Path.GetFullPath(Path.Combine(folder, "Masks/r.png")), copy.Iridescence!.Ramp!.File);
        Assert.Equal(absolute, copy.Iridescence.Mask!.File);
        Assert.Equal(Path.GetFullPath(Path.Combine(folder, "f.png")), copy.Fur!.InfantMask!.File);
        Assert.Equal(1f, copy.Iridescence.Scale);
        Assert.Equal(1f, copy.Settings["_Length"].Number);
        Assert.Equal("Masks/r.png", block.Iridescence.Ramp.File);
        Assert.NotSame(block.Iridescence, copy.Iridescence);
        var bad = new MaterialOverride { Fur = new FurMasks { Mask = new MaskSource { File = "a\0b.png" } }, Settings = { ["_Length"] = MaterialValue.OfNumber(1) } };
        Assert.Null(MaterialTable.WithFullPaths(bad, folder).Fur); // not a path: that mask is dropped, the rest stays
    }

    [Fact]
    public void The_table_says_which_species_and_skins_it_covers_before_any_material_is_read()
    {
        var a = Mod("mod-a", "{\"format\":1,\"id\":\"mod-a\",\"skins\":[" + Skin("mine") + "," + Skin("bare") + "]}",
            SpeciesFile("Titanoboa", """{"Titanoboa":{"settings":{"_Smoothness":0.2}}}"""),
            SkinFile("mine", """{"Titanoboa":{"iridescence":{"strength":0.35}}}"""), SkinFile("bare", "{}"));
        var table = MaterialTable.Build([a], out _);

        Assert.True(table.CoversSpecies("Titanoboa"));
        Assert.False(table.CoversSpecies("Other"));
        Assert.False(table.CoversSpecies(null));
        Assert.True(table.CoversSkin("mod-a", "mine"));
        Assert.False(table.CoversSkin("mod-a", "bare"));
        Assert.False(table.CoversSkin("mod-b", "mine"));
    }

    [Fact]
    public void Baby_strength_and_fur_age_merge_per_field_and_are_copied()
    {
        var a = Mod("mod-a", "{\"format\":1,\"id\":\"mod-a\",\"skins\":[" + Skin("young") + "]}",
            SpeciesFile("Titanoboa", """{"Titanoboa":{"iridescence":{"strength":0.6,"babyStrength":0.2},"fur":{"age":"babies"}}}"""),
            SkinFile("young", """{"Titanoboa":{"iridescence":{"babyStrength":0.4},"fur":{"grow":true}}}"""));
        var table = MaterialTable.Build([a], out var messages);

        Assert.Empty(messages);
        var species = table.For("Titanoboa", null, null, null, "Titanoboa")!;
        Assert.Equal(0.2f, species.Iridescence!.BabyStrength);
        Assert.Equal(FurAge.Babies, species.Fur!.Age);
        var skin = table.For("Titanoboa", "mod-a/young", null, null, "Titanoboa")!;
        Assert.Equal(0.4f, skin.Iridescence!.BabyStrength); // the skin's own
        Assert.Equal(0.6f, skin.Iridescence.Strength!.Normal); // the species' adult strength beside it
        Assert.Equal(FurAge.Babies, skin.Fur!.Age); // from the species, beside the skin's grow
        Assert.True(skin.Fur.Grow);

        var live = new MaterialOverride { Iridescence = new IridescenceSettings { BabyStrength = 0.1f }, Fur = new FurMasks { Age = FurAge.Both } };
        var copy = MaterialTable.WithFullPaths(live, Path.GetTempPath());
        Assert.Equal(0.1f, copy.Iridescence!.BabyStrength);
        Assert.Equal(FurAge.Both, copy.Fur!.Age);
        Assert.Equal(0.1f, MaterialTable.MergeBelow(LookScope.Animal, null, null, live, null, null, null, null)!.Iridescence!.BabyStrength);
        Assert.Equal(IridescenceSettings.DefaultVariation, MaterialTable.EffectiveVariation(live.Iridescence)); // a baby strength varies like a strength
    }
}
