using System.Text.Json;
using Tyrant.Core.Data;
using Tyrant.Core.Errors;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Mods;

/// <summary>A vanilla skin of a species: its index and name, and per sex the Addressables GUID of each texture slot it uses.</summary>
public sealed record VanillaSkin(int Index, string Name, IReadOnlyDictionary<string, string> Male, IReadOnlyDictionary<string, string> Female);

public sealed record SpeciesSkins(string SpeciesId, bool Vivarium, IReadOnlyList<VanillaSkin> Skins);

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
                "Adding a skin needs the game's data (species and their skins): on the Home tab click Run data dump (or run 'tyrant dump run'), then try again.",
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
                        skins.Add(new VanillaSkin(index, name, Textures(skin, "maleVariationData", variations), Textures(skin, "femaleVariationData", variations)));
                        index++;
                    }
                }
                result.Add(new SpeciesSkins(speciesId, type.FullName == VivariumType, skins));
            }
        }
        return result.OrderBy(s => s.SpeciesId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyDictionary<string, string> Textures(JsonElement skin, string field, Dictionary<long, JsonElement> variations)
    {
        var textures = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!skin.TryGetProperty(field, out var reference) || reference.ValueKind != JsonValueKind.Object
            || !reference.TryGetProperty("$ref", out var target) || !target.TryGetProperty("id", out var id) || !id.TryGetInt64(out var key)
            || !variations.TryGetValue(key, out var variation))
            return textures;
        foreach (var (slot, name) in Fields)
            if (variation.TryGetProperty(name, out var assetReference) && assetReference.ValueKind == JsonValueKind.Object
                && assetReference.TryGetProperty("m_AssetGUID", out var guid) && guid.GetString() is { Length: > 0 } text)
                textures[slot] = text;
        return textures;
    }
}
