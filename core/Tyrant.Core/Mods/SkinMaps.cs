using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>Which picture a skin wears in each texture slot: the mod's own PNG, else its base (vanilla) skin's texture.</summary>
public static class SkinMaps
{
    public static readonly string[] Slots =
        ["diffuse", "normal", "extra", "pattern", "fur", "infantDiffuse", "infantNormal", "infantExtra", "infantPattern", "infantFur"];

    public static VanillaSkin? BaseOf(IReadOnlyList<SpeciesSkins>? species, SkinEntry skin) =>
        species is null ? null : Vanilla(species, skin.Species, skin.Base);

    /// <summary>A species' vanilla skin by number or name (any case); null skin = its first.</summary>
    public static VanillaSkin? Vanilla(IReadOnlyList<SpeciesSkins> species, string speciesId, string? skin)
    {
        var target = species.FirstOrDefault(s => string.Equals(s.SpeciesId, speciesId, StringComparison.OrdinalIgnoreCase));
        if (target is null) return null;
        if (skin is null) return target.Skins.FirstOrDefault();
        return int.TryParse(skin, out var number) ? target.Skins.FirstOrDefault(v => v.Index == number)
            : target.Skins.FirstOrDefault(v => string.Equals(v.Name, skin, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// "file:&lt;path&gt;" or "guid:&lt;texture guid&gt;" for the slot, or null. sex: "female"; "male-only" (the 3D view: one sex, as the
    /// game); "infant" (the male skin's infant slot, then the adult one, as the game); "male" also borrows the female skin's map
    /// per slot (Blender projects: male-only and female-only skins still get every map).
    /// </summary>
    public static string? Source(SkinEntry? own, string? modDir, VanillaSkin? vanilla, string slot, string sex)
    {
        var candidates = sex == "infant" ? new[] { "infant" + char.ToUpperInvariant(slot[0]) + slot[1..], slot } : [slot];
        IReadOnlyDictionary<string, string>?[] owns = sex switch
        {
            "female" => [own?.Female],
            "male" => [own?.Male, own?.Female],
            _ => [own?.Male],
        };
        IReadOnlyDictionary<string, string>?[] vanillas = sex switch
        {
            "female" => [vanilla?.Female],
            "male" => [vanilla?.Male, vanilla?.Female],
            _ => [vanilla?.Male],
        };
        foreach (var candidate in candidates)
        {
            if (modDir is not null && owns.Select(d => d?.GetValueOrDefault(candidate)).FirstOrDefault(f => !string.IsNullOrEmpty(f)) is { } file)
            {
                var path = Path.GetFullPath(Path.Combine(modDir, file));
                if (ModPaths.IsInside(path, modDir) && File.Exists(path)) return "file:" + path;
            }
            if (vanillas.Select(d => d?.GetValueOrDefault(candidate)).FirstOrDefault(g => !string.IsNullOrEmpty(g)) is { } guid) return "guid:" + guid;
        }
        return null;
    }

    /// <summary>Puts the source's picture at pngPath (copied or decoded, once) and returns it; null when there is none.</summary>
    public static string? Write(string? source, GameInstall install, AssetIndex index, IAssetReader reader, string pngPath)
    {
        if (source is null) return null;
        if (File.Exists(pngPath)) return pngPath;
        if (source.StartsWith("file:", StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(pngPath)!);
            File.Copy(source[5..], pngPath, overwrite: true);
            return pngPath;
        }
        var guid = source[5..];
        var texture = index.Assets.FirstOrDefault(a => a.Type == "Texture2D" && string.Equals(a.Guid, guid, StringComparison.OrdinalIgnoreCase));
        if (texture is null) return null;
        Directory.CreateDirectory(Path.GetDirectoryName(pngPath)!);
        reader.WriteTexture(install, texture, pngPath);
        return pngPath;
    }
}
