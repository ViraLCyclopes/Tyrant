using Tyrant.Core.Errors;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>One portrait to set: a PNG on disk, or PNG bytes (Render from 3D). Rendered marks the file so a later render may replace it.</summary>
public sealed record PortraitSource(string? Png = null, byte[]? Data = null, bool Rendered = false);

public sealed partial class ModProject
{
    /// <summary>Sets (or, null, clears) a skin's portraits; a cleared slot keeps its file (undo restores mod.json only).</summary>
    public IReadOnlyDictionary<string, string> SetPortraits(string skinId, IReadOnlyDictionary<string, PortraitSource?> set)
    {
        var skin = Skin(skinId);
        var portraits = new Dictionary<string, string>(skin.Portraits ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        ApplyPortraits(portraits, $"skins/{skinId}/portraits", set);
        skin.Portraits = portraits.Count == 0 ? null : portraits;
        Save();
        return portraits;
    }

    /// <summary>The same for a custom species' starting skin, saved in animals.db.</summary>
    public IReadOnlyDictionary<string, string> SetSpeciesPortraits(string speciesId, IReadOnlyDictionary<string, PortraitSource?> set)
    {
        var entry = Manifest.Species.FirstOrDefault(s => s.Id == speciesId)
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, "Species not found: " + speciesId);
        var old = entry.Portraits;
        var portraits = new Dictionary<string, string>(old, StringComparer.Ordinal);
        ApplyPortraits(portraits, $"portraits/{speciesId}", set);
        entry.Portraits = portraits;
        try { SaveSpecies(); }
        catch { entry.Portraits = old; throw; }
        return portraits;
    }

    private void ApplyPortraits(Dictionary<string, string> portraits, string folder, IReadOnlyDictionary<string, PortraitSource?> set)
    {
        // Everything is checked before any file is written.
        foreach (var (slot, source) in set)
        {
            if (!PortraitSlots.IsSlot(slot))
                throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{slot}' is not a portrait (use {string.Join(", ", PortraitSlots.All)}).");
            if (source is null) continue;
            if (source.Data is null == source.Png is null)
                throw new TyrantException(TyrantErrorCode.ModInvalid, $"Portrait '{slot}': give a PNG file or PNG data.");
            if (source.Data is not null && !source.Data.AsSpan().StartsWith(PngSignature))
                throw new TyrantException(TyrantErrorCode.ModInvalid, $"Portrait '{slot}' is not a PNG image.");
        }
        foreach (var (slot, source) in set)
        {
            if (source is null)
            {
                portraits.Remove(slot);
                continue;
            }
            var file = $"{folder}/{PortraitSlots.FileName(slot, source.Rendered)}";
            if (source.Png is not null) CopyPng(source.Png, file);
            else
            {
                var destination = Path.Combine(Dir, file.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, source.Data!);
            }
            portraits[slot] = file;
        }
    }
}
