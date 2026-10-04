using Tyrant.Core.Assets;

namespace Tyrant.Core.Models;

/// <summary>
/// A material with its textures found in the index: the picture (base colour) and normal map glTF can carry.
/// Missing lists slots that point at a texture the index does not know (or at something that is not a texture).
/// </summary>
public sealed record ResolvedMaterial(string Name, AssetRecord? BaseColor, string? BaseColorSlot, AssetRecord? Normal, IReadOnlyList<string> Missing);

public static class MaterialResolver
{
    // The game's animal shader first (its infant slots repeat the adult files), then its scenery shaders (_DiffuseTex:
    // fences, paths and buildings; _Albedo/_Normals: scenery) and Unity's standard names.
    private static readonly string[] BaseColorSlots = ["_AdultDiffuse", "_DiffuseTex", "_BaseColorMap", "_BaseMap", "_MainTex", "_Albedo", "_Diffuse", "_DiffuseMap"];
    private static readonly string[] NormalSlots = ["_AdultNormal", "_NormalTex", "_Normals", "_NormalMap", "_BumpMap", "_Normal"];

    /// <param name="bundle">The bundle the material was read from; its own-file textures live there.</param>
    public static ResolvedMaterial Resolve(AssetIndex index, string bundle, MaterialModel material)
    {
        var missing = new List<string>();
        var (baseColor, baseSlot) = First(BaseColorSlots);
        var (normal, _) = First(NormalSlots);
        return new ResolvedMaterial(material.Name, baseColor, baseSlot, normal, missing);

        (AssetRecord? Texture, string? Slot) First(string[] slots)
        {
            foreach (var slot in slots)
                if (Texture(slot) is { } texture) return (texture, slot);
            return (null, null);
        }

        AssetRecord? Texture(string slot)
        {
            var entry = material.Textures.FirstOrDefault(t => string.Equals(t.Slot, slot, StringComparison.Ordinal));
            if (entry is null) return null;
            var target = entry.Archive is null ? bundle : index.BundleOfArchive(entry.Archive);
            var record = target is null ? null : index.Find(target, entry.PathId);
            if (record is { Type: "Texture2D" }) return record;
            missing.Add(slot);
            return null;
        }
    }
}
