using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Blender;

/// <summary>An image Send took from Blender: a skin slot and the PNG the add-on wrote for it.</summary>
public sealed record BlenderImage(string Slot, string Png);

/// <summary>The slots written, where they went ("skin maps (male)", "texture replacements"), and what was skipped.</summary>
public sealed record BlenderImagesResult(IReadOnlyList<string> Written, string? To, IReadOnlyList<string> Warnings);

/// <summary>
/// Puts the images Send took from Blender into the mod: the destination skin's maps, or texture replacements of the textures
/// of the vanilla skin the project was opened from (else the species' first skin).
/// </summary>
public static class BlenderImages
{
    public static BlenderImagesResult Apply(ModProject mod, Workspace ws, AssetIndex index, IReadOnlyList<SpeciesSkins> species,
        BlenderDestination destination, IReadOnlyList<BlenderImage> images, string sex, string? vanillaSkin = null)
    {
        var written = new List<string>();
        var warnings = new List<string>();
        if (images.Count == 0) return new BlenderImagesResult([], null, []);
        var usable = images.Where(i => Usable(i, warnings)).ToList();
        string to;
        if (destination.Skin is { } skinId)
        {
            var skin = mod.Skin(skinId); // a skin that is gone is an error, before any image is written
            // The shown sex's own maps: the game gives a female only the skin's female maps (never the male's), and so on.
            var useSex = sex == "female" ? "female" : "male";
            to = $"skin maps ({useSex})";
            foreach (var image in usable)
            {
                var held = (useSex == "female" ? skin.Female : skin.Male)?.GetValueOrDefault(image.Slot);
                if (Holds(mod, held, image.Png)) continue;
                Try(image, warnings, () => { mod.SetSkinFile(skinId, useSex, image.Slot, image.Png); written.Add(image.Slot); });
            }
        }
        else
        {
            to = "texture replacements";
            // The vanilla skin the project was opened from (what the user painted), else the species' first.
            var vanilla = (vanillaSkin is null ? null : SkinMaps.Vanilla(species, destination.Species, vanillaSkin))
                ?? SkinMaps.Vanilla(species, destination.Species, null);
            foreach (var image in usable)
            {
                var male = Guid(vanilla?.Male, image.Slot);
                var female = Guid(vanilla?.Female, image.Slot);
                var guid = sex == "female" ? female ?? male : male ?? female;
                if (guid is null)
                {
                    warnings.Add($"'{image.Slot}' has no game texture on {destination.Species}'s {vanilla?.Name ?? "default"} skin, so it was not sent (send to a skin to use it).");
                    continue;
                }
                var name = index.Assets.FirstOrDefault(a => a.Type == "Texture2D" && string.Equals(a.Guid, guid, StringComparison.OrdinalIgnoreCase))?.Name;
                var held = mod.Manifest.Replace.FirstOrDefault(r => string.Equals(r.Texture, name, StringComparison.OrdinalIgnoreCase))?.File;
                if (name is not null && Holds(mod, held, image.Png)) continue;
                Try(image, warnings, () =>
                {
                    var entry = mod.Replace(ws, index, guid, image.Png);
                    written.Add(image.Slot);
                    if (male is null || female is null || string.Equals(male, female, StringComparison.OrdinalIgnoreCase))
                        warnings.Add($"'{image.Slot}': both sexes use {entry.Texture}, so its replacement changes both sexes.");
                });
            }
        }
        return new BlenderImagesResult(written, written.Count > 0 ? to : null, warnings);
    }

    private static bool Usable(BlenderImage image, List<string> warnings)
    {
        if (!SkinMaps.Slots.Contains(image.Slot, StringComparer.Ordinal))
        {
            warnings.Add($"'{image.Slot}' is not a skin slot ({string.Join(", ", SkinMaps.Slots)}); it was not sent.");
            return false;
        }
        if (!File.Exists(image.Png))
        {
            warnings.Add($"The '{image.Slot}' image ({image.Png}) is gone; it was not sent.");
            return false;
        }
        return true;
    }

    /// <summary>True when the mod's file (relative to the mod) already holds exactly this picture: nothing to write.</summary>
    private static bool Holds(ModProject mod, string? file, string png)
    {
        if (string.IsNullOrEmpty(file)) return false;
        var path = Path.Combine(mod.Dir, file.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path) || new FileInfo(path).Length != new FileInfo(png).Length) return false;
        return File.ReadAllBytes(path).AsSpan().SequenceEqual(File.ReadAllBytes(png));
    }

    private static string? Guid(IReadOnlyDictionary<string, string>? maps, string slot) =>
        maps is not null && maps.TryGetValue(slot, out var guid) && !string.IsNullOrEmpty(guid) ? guid : null;

    private static void Try(BlenderImage image, List<string> warnings, Action write)
    {
        try
        {
            write();
        }
        catch (TyrantException ex)
        {
            warnings.Add($"'{image.Slot}': {ex.Message}");
        }
    }
}
