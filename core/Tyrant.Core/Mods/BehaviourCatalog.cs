using System.Text.Json;
using Tyrant.Core.Data;
using Tyrant.Core.Errors;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

public sealed record BehaviourProfileInfo(string Asset, string Name, string Confidence, string Temperament, string Cannibalism, IReadOnlyList<string> Rules, IReadOnlyList<string> UsedBy);

public sealed record BehaviourRuleInfo(string Asset, string Name, bool ShownInGame);

public sealed record BehaviourCatalogData(IReadOnlyList<BehaviourProfileInfo> Profiles, IReadOnlyList<BehaviourRuleInfo> Rules);

/// <summary>Reads the game's combat profiles and aggression rules from the player's game dump.</summary>
public static class BehaviourCatalog
{
    const string ProfileType = "PrehistoricKingdom.AnimalsV2.AI.AnimalCombatProfile";
    const string RuleType = "PrehistoricKingdom.AnimalsV2.AI.AggressionProfileAsset";
    const string BrainType = "PrehistoricKingdom.AnimalsV2.AI.AnimalBrain";

    public static BehaviourCatalogData Load(Workspace ws)
    {
        try
        {
            var store = DataStore.Open(ws);
            var profileType = store.FindType(ProfileType);
            var users = UsersByProfile(store);
            var profiles = new List<BehaviourProfileInfo>();
            foreach (var known in SpeciesBehaviours.Profiles)
            {
                JsonElement root;
                try { root = store.Load(profileType, known.Asset); }
                catch (TyrantException ex) when (ex.Code is TyrantErrorCode.AssetNotFound) { continue; }
                var rules = new List<string>();
                if (root.TryGetProperty("aggressionProfileEntries", out var entries) && entries.ValueKind == JsonValueKind.Array)
                    foreach (var entry in entries.EnumerateArray())
                        if (entry.TryGetProperty("asset", out var asset) && RefName(asset) is { } rule && !rules.Contains(rule)) rules.Add(rule);
                profiles.Add(new BehaviourProfileInfo(known.Asset, known.Name,
                    Text(root, "confidenceLevel"), Text(root, "temperament"), Text(root, "cannibalism"),
                    rules, users.TryGetValue(known.Asset, out var list) ? list.OrderBy(s => s, StringComparer.Ordinal).ToList() : []));
            }
            return new BehaviourCatalogData(profiles, SpeciesBehaviours.Rules.Select(r => new BehaviourRuleInfo(r.Asset, r.Name, ShownInGame(store, r.Asset))).ToList());
        }
        catch (TyrantException ex) when (ex.Code is TyrantErrorCode.DataMissing or TyrantErrorCode.AssetNotFound)
        {
            return new BehaviourCatalogData([], []);
        }
    }

    /// <summary>The profile asset named by the template's profile reference row, or null.</summary>
    public static string? TemplateProfile(IReadOnlyList<AnimalField> templateFields)
    {
        var row = templateFields.FirstOrDefault(f => f.Path == SpeciesBehaviours.ProfilePath && f.Kind == "reference");
        if (row == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(row.Value);
            return doc.RootElement.TryGetProperty("name", out var name) ? name.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    static string Text(JsonElement root, string property) =>
        root.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    static string? RefName(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty("$ref", out var r) && r.TryGetProperty("name", out var n) ? n.GetString() : null;

    static bool ShownInGame(DataStore store, string asset)
    {
        try
        {
            var root = store.Load(store.FindType(RuleType), asset);
            return root.TryGetProperty("profile", out var p) && p.TryGetProperty("showInGUI", out var s) && s.ValueKind == JsonValueKind.True;
        }
        catch (TyrantException ex) when (ex.Code is TyrantErrorCode.DataMissing or TyrantErrorCode.AssetNotFound) { return false; }
    }

    /// <summary>Species ids by the combat profile their animal's brain links to.</summary>
    static Dictionary<string, List<string>> UsersByProfile(DataStore store)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        try
        {
            var brainProfile = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (name, root) in store.LoadAll(store.FindType(BrainType)))
                if (root.TryGetProperty("combat", out var c) && c.TryGetProperty("shared", out var s) && s.TryGetProperty("combatProfile", out var p) && RefName(p) is { } profile)
                    brainProfile[name] = profile;
            foreach (var (_, root) in store.LoadAll(store.FindType("PrehistoricKingdom.AnimalData")))
            {
                if (!root.TryGetProperty("speciesID", out var id) || id.GetString() is not { } species) continue;
                if (!root.TryGetProperty("brain", out var brain) || RefName(brain) is not { } brainName || !brainProfile.TryGetValue(brainName, out var asset)) continue;
                if (!result.TryGetValue(asset, out var list)) result[asset] = list = [];
                list.Add(species);
            }
        }
        catch (TyrantException ex) when (ex.Code is TyrantErrorCode.DataMissing or TyrantErrorCode.AssetNotFound or TyrantErrorCode.AssetAmbiguous) { }
        return result;
    }
}
