using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class SpeciesPresetsTests
{
    /// <summary>Presets by species id, as the game (once a material is loaded) or the dump gives them; a missing one is still loading.</summary>
    private sealed class FakePresets(params SpeciesPreset[] presets) : ISpeciesPresets
    {
        public SpeciesPreset? Preset(string species) => presets.FirstOrDefault(p => p.Species == species);
    }

    private static SpeciesPreset Microraptor() =>
        SpeciesPreset.Of("Microraptor", "Microraptor",
            [
                new PresetSkin(), // a skin that does not shimmer comes first
                new PresetSkin { Shimmers = true, Strength = new VariantNumbers { Normal = 0.25f, Albino = 0.1f } },
                new PresetSkin { Shimmers = true, Strength = new VariantNumbers { Normal = 0.9f } },
            ],
            new Dictionary<string, float> { ["_IridescenceScale"] = -0.25f, ["_IridescencePower"] = -0.3f, ["_IridescenceBias"] = 1.3f, ["_Length"] = 0 },
            hasRamp: true, hasMask: true);

    private static SpeciesPreset Woolly() =>
        SpeciesPreset.Of("Mammoth", "Woolly mammoth",
            [new PresetSkin(), new PresetSkin { HasFur = true, Droop = [0f, -30f, 8.11f] }, new PresetSkin { HasFur = true, Droop = [1f, 1f, 1f] }],
            new Dictionary<string, float> { ["_Length"] = 1.5f, ["_Droopage"] = 3f, ["_FurWindStrength"] = 0.4f, ["_FurWindSpeed"] = 2f, ["_FurWindScale"] = 1f, ["_IridescenceScale"] = 5f },
            hasRamp: false, hasMask: false);

    [Fact]
    public void A_preset_reads_the_first_shimmering_and_the_first_furred_skin()
    {
        var micro = Microraptor();
        Assert.True(micro.Shimmers);
        Assert.False(micro.Furry);
        Assert.Equal(0.25f, micro.Strength!.Normal);
        Assert.Equal(0.1f, micro.Strength.Albino);
        Assert.Null(micro.Droop);

        var mammoth = Woolly();
        Assert.True(mammoth.Furry);
        Assert.False(mammoth.Shimmers);
        Assert.Null(mammoth.Strength);
        Assert.Equal([0f, -30f, 8.11f], mammoth.Droop!);
    }

    [Fact]
    public void Presets_are_listed_by_name_each_species_once()
    {
        var sorted = SpeciesPreset.Sorted([("Velociraptor", "velociraptor"), ("Microraptor", "Microraptor"), ("Oviraptor", "Oviraptor"), ("Microraptor", "Microraptor")]);

        Assert.Equal(["Microraptor", "Oviraptor", "Velociraptor"], sorted.Select(s => s.Id));
    }

    [Fact]
    public void The_iridescence_preset_names_the_species_textures_and_copies_its_numbers()
    {
        var draft = new LookDraft(LookScope.Skin, new MaterialOverride
        {
            Iridescence = new IridescenceSettings { Ramp = new RampSource { ThinFilm = new ThinFilmSettings { Thickness = 300 } }, Variation = 0.4f, Strength = new VariantNumbers { Normal = 0 } },
            Settings = { ["_Smoothness"] = MaterialValue.OfNumber(0.3f) },
        });

        Assert.True(LookEditing.ApplyPreset(draft, PresetKind.Iridescence, new FakePresets(Microraptor()), "Microraptor", targetSpecies: "Microraptor"));

        var i = draft.Current.Iridescence!;
        Assert.Equal("Microraptor", i.Ramp!.Species);
        Assert.Equal("Microraptor", i.Mask!.Species);
        Assert.Equal((-0.25f, -0.3f, 1.3f), (i.Scale, i.Power, i.Bias));
        Assert.Equal((0.25f, 0.1f), (i.Strength!.Normal, i.Strength.Albino)); // on, with the species' own strengths
        Assert.Equal(0.4f, i.Variation); // what the preset does not copy stays
        Assert.Equal(0.3f, draft.Current.Settings["_Smoothness"].Number);
        Assert.False(draft.Current.Settings.ContainsKey("_Length")); // the iridescence preset leaves the fur alone
        Assert.True(draft.IsDirty);
        draft.SetStrength("normal", 0.5f); // every value stays editable afterwards
        Assert.Equal(0.5f, draft.Current.Iridescence!.Strength!.Normal);
    }

    [Fact]
    public void Without_a_mask_texture_it_shimmers_everywhere_and_without_a_ramp_keeps_the_draft_s()
    {
        var bare = SpeciesPreset.Of("Edmontosaurus", "Edmontosaurus", [new PresetSkin { Shimmers = true }], null, hasRamp: false, hasMask: false);
        var draft = new LookDraft(LookScope.Animal, new MaterialOverride { Iridescence = new IridescenceSettings { Ramp = new RampSource { File = "r.png" } } });

        LookEditing.ApplyPreset(draft, PresetKind.Iridescence, bare, targetSpecies: "Edmontosaurus");

        var i = draft.Current.Iridescence!;
        Assert.Equal("r.png", i.Ramp!.File);
        Assert.True(i.Mask!.Everywhere);
        Assert.Equal(1f, i.Strength!.Normal); // a shimmering skin without a strength still turns iridescence on
        Assert.Null(i.Scale);
    }

    [Fact]
    public void The_fur_preset_grows_fur_with_the_species_numbers_and_keeps_the_mask()
    {
        var draft = new LookDraft(LookScope.Species, new MaterialOverride
        {
            Fur = new FurMasks { Mask = new MaskSource { File = "fur.png" } },
            Iridescence = new IridescenceSettings { Scale = 2 },
        });

        Assert.True(LookEditing.ApplyPreset(draft, PresetKind.Fur, new FakePresets(Woolly()), "Mammoth", targetSpecies: "Allosaurus"));

        var c = draft.Current;
        Assert.True(c.Fur!.Grow);
        Assert.Equal("fur.png", c.Fur.Mask!.File);
        Assert.Equal(1.5f, c.Settings["_Length"].Number);
        Assert.Equal(2f, c.Settings["_Droopage"].Number); // kept within the setting's range (0 to 2)
        Assert.Equal(0.4f, c.Settings["_FurWindStrength"].Number);
        Assert.Equal(2f, c.Settings["_FurWindSpeed"].Number);
        Assert.Equal(1f, c.Settings["_FurWindScale"].Number);
        Assert.Equal([0f, -20f, 8.11f], c.Settings["_DroopDirection"].Vector.Take(3)); // within ±20
        Assert.Equal(2f, c.Iridescence!.Scale); // the fur preset leaves the iridescence alone
        Assert.True(MaterialOverrides.Problem(new Dictionary<string, MaterialOverride> { ["m"] = c }, "Look", false) == null); // it can be saved
    }

    [Fact]
    public void On_another_species_the_mask_is_everywhere_and_the_ramp_still_a_reference()
    {
        // An iridescence mask is painted for its own species' UV layout (Ruling 15): on another species it would land anywhere.
        var draft = new LookDraft(LookScope.Species, null);

        LookEditing.ApplyPreset(draft, PresetKind.Iridescence, Microraptor(), targetSpecies: "Ouranosaurus");

        Assert.True(draft.Current.Iridescence!.Mask!.Everywhere);
        Assert.Equal("Microraptor", draft.Current.Iridescence.Ramp!.Species);
        Assert.True(LookEditing.PresetBlock(Microraptor(), PresetKind.Iridescence, targetSpecies: null).Iridescence!.Mask!.Everywhere); // unknown: another
        Assert.Equal("Microraptor", LookEditing.PresetBlock(Microraptor(), PresetKind.Iridescence, targetSpecies: "Microraptor").Iridescence!.Mask!.Species);
    }

    [Fact]
    public void A_preset_without_a_known_strength_leaves_a_strength_alone_and_its_block_has_none()
    {
        var unknown = SpeciesPreset.Of("Edmontosaurus", "Edmontosaurus", [new PresetSkin { Shimmers = true }], null, hasRamp: true, hasMask: false);
        var draft = new LookDraft(LookScope.Skin, new MaterialOverride { Iridescence = new IridescenceSettings { Strength = new VariantNumbers { Normal = 0.6f } } });

        LookEditing.ApplyPreset(draft, PresetKind.Iridescence, unknown, targetSpecies: "Ouranosaurus");

        Assert.Equal(0.6f, draft.Current.Iridescence!.Strength!.Normal); // kept: it is on already
        Assert.Null(LookEditing.PresetBlock(unknown, PresetKind.Iridescence, targetSpecies: "Ouranosaurus").Iridescence!.Strength); // the merge decides
    }

    [Fact]
    public void A_preset_still_loading_changes_nothing()
    {
        var draft = new LookDraft(LookScope.Skin, null);

        Assert.False(LookEditing.ApplyPreset(draft, PresetKind.Iridescence, new FakePresets(), "Microraptor", targetSpecies: "Microraptor"));
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void A_preset_block_holds_only_what_the_preset_sets()
    {
        var iridescence = LookEditing.PresetBlock(Microraptor(), PresetKind.Iridescence, targetSpecies: "Microraptor");
        Assert.Null(iridescence.Fur);
        Assert.Empty(iridescence.Settings);
        Assert.Equal("Microraptor", iridescence.Iridescence!.Ramp!.Species);

        var fur = LookEditing.PresetBlock(Woolly(), PresetKind.Fur, targetSpecies: "Allosaurus");
        Assert.Null(fur.Iridescence);
        Assert.True(fur.Fur!.Grow);
        Assert.Null(fur.Fur.Mask);
        Assert.Equal(["_DroopDirection", "_Droopage", "_FurWindScale", "_FurWindSpeed", "_FurWindStrength", "_Length"], fur.Settings.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }
}
