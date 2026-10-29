using Tyrant.Core.Errors;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

public sealed partial class ModProject
{
    /// <summary>
    /// The order of a custom species' skins in the Nursery picker (the saved one, else starting skin first then by skin number),
    /// or null when <paramref name="speciesKey"/> is not one of this mod's species or it has no starting skin.
    /// <paramref name="numbers"/> are skin-slots keys ("&lt;mod&gt;/&lt;skin&gt;") to numbers; only this mod's are used.
    /// </summary>
    public List<string>? ShownSkinOrder(string speciesKey, IReadOnlyDictionary<string, int>? numbers)
    {
        if (!Manifest.Species.Any(s => s.Key(Id) == speciesKey) || StartingSkins.Of(Manifest, speciesKey) is not { } starting) return null;
        var others = Manifest.Skins.Where(s => s.Species == speciesKey && s != starting).Select(s => s.Id).ToList();
        Dictionary<string, int>? byId = null;
        if (numbers is not null)
        {
            byId = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var id in others)
                if (numbers.TryGetValue(Id + "/" + id, out var n)) byId[id] = n;
        }
        return SkinPickerOrder.Shown(Manifest.SkinOrders.GetValueOrDefault(speciesKey), starting.Id, others, byId);
    }

    /// <summary>Saves the picker order of one of this mod's species: exactly its own skins (the starting skin included), each once.</summary>
    public void SetSkinOrder(string speciesKey, IReadOnlyList<string> skinIds)
    {
        if (!Manifest.Species.Any(s => s.Key(Id) == speciesKey))
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"{speciesKey} is not one of this mod's species: only their skin order can be set.");
        if (StartingSkins.Of(Manifest, speciesKey) is null)
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"{speciesKey} needs an editable starting skin first (Make an editable starting skin on its page).");
        var own = Manifest.Skins.Where(s => s.Species == speciesKey).Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        if (skinIds.Count != own.Count || !own.SetEquals(skinIds))
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"The skin order of {speciesKey} must list each of its skins exactly once: {string.Join(", ", own)}.");
        var old = Manifest.SkinOrders.TryGetValue(speciesKey, out var previous) ? previous : null;
        Manifest.SkinOrders[speciesKey] = skinIds.ToList();
        try { Save(); }
        catch { RestoreSkinOrder(speciesKey, old); throw; }
    }

    private void RestoreSkinOrder(string speciesKey, List<string>? order)
    {
        if (order is null) Manifest.SkinOrders.Remove(speciesKey);
        else Manifest.SkinOrders[speciesKey] = order;
    }

    /// <summary>Drops a removed skin from every saved order (and orders left empty).</summary>
    private void ForgetSkinInOrders(string skinId)
    {
        foreach (var (species, order) in Manifest.SkinOrders.ToList())
        {
            order.RemoveAll(id => id == skinId);
            if (order.Count == 0) Manifest.SkinOrders.Remove(species);
        }
    }
}
