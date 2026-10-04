using Tyrant.Core.Models;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>Which skin slots the skin page offers: the ones the species' animal material fills (the others are unused by its shader).</summary>
public static class ShaderSlots
{
    /// <summary>The mod.json slots the first animal material fills; null when no material is an animal one (shader unknown).</summary>
    public static IReadOnlyList<string>? FromMaterials(IReadOnlyList<MaterialModel> materials)
    {
        var animal = materials.FirstOrDefault(m => m.Textures.Any(t => t.Slot == "_AdultDiffuse"));
        return animal?.Textures
            .Select(t => MaterialResolver.AnimalSlotNames.GetValueOrDefault(t.Slot))
            .OfType<string>()
            .Distinct()
            .OrderBy(Order)
            .ToList();
    }

    /// <summary>The base skin's slots the shader uses; the shader's when the base lists none; the base's when the shader is unknown.</summary>
    public static IReadOnlyList<string> Shown(IReadOnlyList<string> baseSlots, IReadOnlyList<string>? shaderSlots)
    {
        if (shaderSlots is null) return baseSlots;
        if (baseSlots.Count == 0) return shaderSlots;
        return baseSlots.Where(shaderSlots.Contains).ToList();
    }

    private static int Order(string slot)
    {
        for (var i = 0; i < SkinSlotNames.All.Count; i++)
            if (SkinSlotNames.All[i] == slot) return i;
        return int.MaxValue;
    }
}
