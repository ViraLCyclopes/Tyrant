using Tyrant.Core.Assets;

namespace Tyrant.Core.Models;

/// <summary>
/// A material with its textures found in the index: the picture (base colour) and normal map glTF can carry.
/// Missing lists slots that point at a texture the index does not know (or at something that is not a texture).
/// </summary>
public sealed record ResolvedMaterial(string Name, AssetRecord? BaseColor, string? BaseColorSlot, AssetRecord? Normal, IReadOnlyList<string> Missing)
{
    /// <summary>The shader's name or its asset file's name; null when the index does not know it.</summary>
    public string? Shader { get; init; }

    /// <summary>The game's animal shader (it has the _AdultDiffuse slot).</summary>
    public bool Animal { get; init; }

    /// <summary>Alpha below this is cut away; null when the material draws every pixel.</summary>
    public float? Cutoff { get; init; }

    /// <summary>Every slot with a texture the index knows, in the material's order.</summary>
    public IReadOnlyList<ResolvedSlot> Slots { get; init; } = [];
}

public sealed record ResolvedSlot(string Name, AssetRecord Texture);

public static class MaterialResolver
{
    // The game's animal shader first (its infant slots repeat the adult files), then its scenery shaders (_DiffuseTex:
    // fences, paths and buildings; _Albedo/_Normals: scenery) and Unity's standard names.
    private static readonly string[] BaseColorSlots = ["_AdultDiffuse", "_DiffuseTex", "_BaseColorMap", "_BaseMap", "_MainTex", "_Albedo", "_Diffuse", "_DiffuseMap"];
    private static readonly string[] NormalSlots = ["_AdultNormal", "_NormalTex", "_Normals", "_NormalMap", "_BumpMap", "_Normal"];

    /// <summary>The animal shader's slots and the mod.json slot each one is (SkinSlotNames in Tyrant.Framework.Core).</summary>
    public static readonly IReadOnlyDictionary<string, string> AnimalSlotNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["_AdultDiffuse"] = "diffuse", ["_AdultNormal"] = "normal", ["_AdultExtraMap"] = "extra", ["_AdultPatternMask"] = "pattern",
        ["_AdultFurMask"] = "fur", ["_InfantDiffuse"] = "infantDiffuse", ["_InfantNormal"] = "infantNormal",
        ["_InfantExtraMap"] = "infantExtra", ["_InfantPatternMask"] = "infantPattern", ["_InfantFurMask"] = "infantFur",
    };

    /// <summary>The decoded animal shader cuts away diffuse alpha below 0.5 (feathers, hair).</summary>
    public const float AnimalCutoff = 0.5f;

    /// <param name="bundle">The bundle the material was read from; its own-file textures live there.</param>
    public static ResolvedMaterial Resolve(AssetIndex index, string bundle, MaterialModel material)
    {
        var missing = new List<string>();
        var (baseColor, baseSlot) = First(BaseColorSlots);
        var (normal, _) = First(NormalSlots);
        var slots = material.Textures
            .Select(t => (t.Slot, Record: Find(t.Archive, t.PathId)))
            .Where(s => s.Record is { Type: "Texture2D" })
            .Select(s => new ResolvedSlot(s.Slot, s.Record!))
            .ToList();
        var animal = material.Textures.Any(t => t.Slot == "_AdultDiffuse");
        var cutsOut = material.Keywords.Any(k => k.Contains("ALPHATEST", StringComparison.OrdinalIgnoreCase)
            || k.Contains("ALPHACLIP", StringComparison.OrdinalIgnoreCase) || k.Contains("CUTOUT", StringComparison.OrdinalIgnoreCase));
        return new ResolvedMaterial(material.Name, baseColor, baseSlot, normal, missing)
        {
            Shader = ShaderName(),
            Animal = animal,
            Cutoff = animal ? AnimalCutoff : cutsOut ? material.Floats.GetValueOrDefault("_Cutoff", 0.5f) : null,
            Slots = slots,
        };

        AssetRecord? Find(string? archive, long pathId)
        {
            var target = archive is null ? bundle : index.BundleOfArchive(archive);
            return target is null ? null : index.Find(target, pathId);
        }

        string? ShaderName()
        {
            if (material.ShaderPathId == 0 || Find(material.ShaderArchive, material.ShaderPathId) is not { Type: "Shader" } shader) return null;
            if (shader.Name.Length > 0) return shader.Name;
            return shader.ContainerPath is { Length: > 0 } path ? Path.GetFileNameWithoutExtension(path) : null;
        }

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
            var record = Find(entry.Archive, entry.PathId);
            if (record is { Type: "Texture2D" }) return record;
            missing.Add(slot);
            return null;
        }
    }
}
