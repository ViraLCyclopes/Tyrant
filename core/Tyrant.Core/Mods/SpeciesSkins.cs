using System.Text.Json;
using Tyrant.Core.Data;
using Tyrant.Core.Errors;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>A vanilla skin of a species: its index and name, and per sex the Addressables GUID of each texture slot it uses.</summary>
public sealed record VanillaSkin(int Index, string Name, IReadOnlyDictionary<string, string> Male, IReadOnlyDictionary<string, string> Female)
{
    /// <summary>Portrait slot to the Addressables GUID of its texture; empty when the dump has none.</summary>
    public IReadOnlyDictionary<string, string> Portraits { get; init; } = new Dictionary<string, string>();

    /// <summary>Mutation (albino, melanistic, leucistic) to the male variation asset's colours for it; empty when the dump has none.</summary>
    public IReadOnlyDictionary<string, SkinColorSet> MaleMorphs { get; init; } = new Dictionary<string, SkinColorSet>();

    /// <summary>The same from the female variation asset.</summary>
    public IReadOnlyDictionary<string, SkinColorSet> FemaleMorphs { get; init; } = new Dictionary<string, SkinColorSet>();

    /// <summary>
    /// The male variation asset's own iridescence strength (defaultVariationData: its strength when "iridescence" is on, else 0):
    /// what a normal animal of the skin shimmers with in the game. Null when the dump does not say.
    /// </summary>
    public float? MaleIridescence { get; init; }

    /// <summary>The same from the female variation asset.</summary>
    public float? FemaleIridescence { get; init; }

    /// <summary>The skin's own iridescence for a sex (infants wear the male asset); null sex: the stronger of the two known.</summary>
    public float? Iridescence(string? sex) => sex switch
    {
        "female" => FemaleIridescence,
        "male" or "infant" => MaleIridescence,
        _ => MaleIridescence is null ? FemaleIridescence : FemaleIridescence is null ? MaleIridescence : Math.Max(MaleIridescence.Value, FemaleIridescence.Value),
    };
}

public sealed record SpeciesSkins(string SpeciesId, bool Vivarium, IReadOnlyList<VanillaSkin> Skins)
{
    /// <summary>The species prefab's Addressables GUID (AnimalData.animalRef); null when the dump has none.</summary>
    public string? PrefabGuid { get; init; }
}

/// <summary>Reads species and their skins from the data dump (AnimalData / VivariumAnimalData and their skin variation assets).</summary>
public static class SpeciesSkinsReader
{
    private const string AnimalType = "PrehistoricKingdom.AnimalData";
    private const string VivariumType = "PrehistoricKingdom.VivariumAnimalData";
    private const string VariationType = "PrehistoricKingdom.AnimalSkinVariationAsset";

    /// <summary>mod.json slot → AnimalSkinVariationAsset field.</summary>
    private static readonly (string Slot, string Field)[] Fields =
    [
        ("diffuse", "adultDiffuseMap"), ("normal", "adultNormalMap"), ("extra", "adultExtraMap"), ("pattern", "adultPatternMap"), ("fur", "adultFurMap"),
        ("infantDiffuse", "infantDiffuseMap"), ("infantNormal", "infantNormalMap"), ("infantExtra", "infantExtraMap"),
        ("infantPattern", "infantPatternMap"), ("infantFur", "infantFurMap"),
    ];

    public static IReadOnlyList<SpeciesSkins> Load(Workspace ws)
    {
        DataStore store;
        try
        {
            store = DataStore.Open(ws);
        }
        catch (TyrantException ex) when (ex.Code == TyrantErrorCode.DataMissing)
        {
            throw new TyrantException(TyrantErrorCode.DataMissing,
                "Adding a skin needs the game's data (species and their skins): on the Workspace tab click Run data dump (or run 'tyrant dump run'), then try again.",
                FixAction.RefreshWorkspace, ex);
        }
        return Read(store);
    }

    public static IReadOnlyList<SpeciesSkins> Read(DataStore store)
    {
        var types = store.Types();
        var variations = new Dictionary<long, JsonElement>();
        if (types.FirstOrDefault(t => t.FullName == VariationType) is { } variationType)
            foreach (var (_, root) in store.LoadAll(variationType))
                if (root.TryGetProperty("$id", out var id) && id.TryGetInt64(out var value)) variations[value] = root;

        var result = new List<SpeciesSkins>();
        foreach (var type in types.Where(t => t.FullName is AnimalType or VivariumType))
        {
            foreach (var (_, root) in store.LoadAll(type))
            {
                if (!root.TryGetProperty("speciesID", out var idElement) || idElement.GetString() is not { Length: > 0 } speciesId) continue;
                var skins = new List<VanillaSkin>();
                if (root.TryGetProperty("skinsData", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    var index = 0;
                    foreach (var skin in list.EnumerateArray())
                    {
                        var name = skin.TryGetProperty("skinName", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : $"Skin {index}";
                        skins.Add(new VanillaSkin(index, name, Textures(skin, "maleVariationData", variations), Textures(skin, "femaleVariationData", variations))
                        {
                            Portraits = PortraitGuids(skin),
                            MaleMorphs = Morphs(skin, "maleVariationData", variations),
                            FemaleMorphs = Morphs(skin, "femaleVariationData", variations),
                            MaleIridescence = IridescenceOf(Variation(skin, "maleVariationData", variations)),
                            FemaleIridescence = IridescenceOf(Variation(skin, "femaleVariationData", variations)),
                        });
                        index++;
                    }
                }
                result.Add(new SpeciesSkins(speciesId, type.FullName == VivariumType, skins) { PrefabGuid = PrefabGuidOf(root) });
            }
        }
        return result.OrderBy(s => s.SpeciesId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? PrefabGuidOf(JsonElement root) =>
        root.TryGetProperty("animalRef", out var reference) && reference.ValueKind == JsonValueKind.Object
        && reference.TryGetProperty("m_AssetGUID", out var guid) && guid.ValueKind == JsonValueKind.String && guid.GetString() is { Length: > 0 } value
            ? value
            : null;

    private static IReadOnlyDictionary<string, string> PortraitGuids(JsonElement skin)
    {
        var portraits = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!skin.TryGetProperty("portraits", out var block) || block.ValueKind != JsonValueKind.Object) return portraits;
        foreach (var slot in PortraitSlots.All)
            if (block.TryGetProperty(slot, out var reference) && reference.ValueKind == JsonValueKind.Object
                && reference.TryGetProperty("m_AssetGUID", out var guid) && guid.GetString() is { Length: > 0 } text)
                portraits[slot] = text;
        return portraits;
    }

    private static JsonElement? Variation(JsonElement skin, string field, Dictionary<long, JsonElement> variations) =>
        skin.TryGetProperty(field, out var reference) && reference.ValueKind == JsonValueKind.Object
        && reference.TryGetProperty("$ref", out var target) && target.TryGetProperty("id", out var id) && id.TryGetInt64(out var key)
        && variations.TryGetValue(key, out var variation)
            ? variation
            : null;

    /// <summary>A variation asset's defaultVariationData: its strength when "iridescence" is on, 0 when off, null when the dump lacks it.</summary>
    private static float? IridescenceOf(JsonElement? variation)
    {
        if (variation is not { } v || !v.TryGetProperty("defaultVariationData", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("iridescence", out var on) || on.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
        if (on.ValueKind == JsonValueKind.False) return 0f;
        return data.TryGetProperty("iridescenceStrength", out var strength) && strength.ValueKind == JsonValueKind.Number && strength.TryGetSingle(out var value)
            ? Math.Clamp(value, 0f, 1f)
            : null;
    }

    private static IReadOnlyDictionary<string, SkinColorSet> Morphs(JsonElement skin, string field, Dictionary<long, JsonElement> variations) =>
        Variation(skin, field, variations) is { } variation ? MorphColors.Read(variation) : new Dictionary<string, SkinColorSet>();

    private static IReadOnlyDictionary<string, string> Textures(JsonElement skin, string field, Dictionary<long, JsonElement> variations)
    {
        var textures = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Variation(skin, field, variations) is not { } variation) return textures;
        foreach (var (slot, name) in Fields)
            if (variation.TryGetProperty(name, out var assetReference) && assetReference.ValueKind == JsonValueKind.Object
                && assetReference.TryGetProperty("m_AssetGUID", out var guid) && guid.GetString() is { Length: > 0 } text)
                textures[slot] = text;
        return textures;
    }
}
