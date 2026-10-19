using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>
/// The species presets from the game's data, as the in-game editor reads them from the game at runtime: the vanilla species
/// whose skins shimmer or have fur, and what a preset copies from one (its skins in the dump, its material's numbers).
/// </summary>
public static class GamePresets
{
    /// <summary>The vanilla species offered for a kind of preset, by name.</summary>
    public static IReadOnlyList<(string Id, string Name)> List(IReadOnlyList<SpeciesSkins> species, PresetKind kind) =>
        SpeciesPreset.Sorted(species.Where(s => SpeciesUse.ModOf(s.SpeciesId) is null)
            .Select(s => (Species: s, Preset: Of(s, null)))
            .Where(x => kind == PresetKind.Fur ? x.Preset.Furry : x.Preset.Shimmers)
            .Select(x => (x.Species.SpeciesId, x.Species.Name)));

    /// <summary>A species' preset: its skins in the game's order (each one's male asset, then its female one) and its animal material (null: no numbers, no textures).</summary>
    public static SpeciesPreset Of(SpeciesSkins species, AnimalMaterial? material) =>
        SpeciesPreset.Of(species.SpeciesId, species.Name, species.Skins.SelectMany(v => new[] { v.MalePreset, v.FemalePreset }).OfType<PresetSkin>(),
            material?.Floats, material?.Ramp is not null, material?.Mask is not null);

    /// <summary>The block the preset alone makes, as materials.json writes one material's entry (what the app merges into its draft).</summary>
    public static string FragmentJson(SpeciesPreset preset, PresetKind kind)
    {
        const string name = "m";
        var written = MaterialOverrides.ToJson(new Dictionary<string, MaterialOverride> { [name] = LookEditing.PresetBlock(preset, kind) });
        return Json.Write(written.TryGetValue(name, out var block) ? block : new Dictionary<string, object?>());
    }

    /// <summary>"iridescence" or "fur" as a kind; null for anything else.</summary>
    public static PresetKind? KindOf(string? text) => text switch
    {
        "iridescence" => PresetKind.Iridescence,
        "fur" => PresetKind.Fur,
        _ => null,
    };
}
