using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Models;

namespace Tyrant.Core.Mods;

/// <summary>An animal prefab's animal-shader material: its name, shader values and iridescence textures (what a mod's settings start from).</summary>
public sealed record AnimalMaterial(string Name, IReadOnlyDictionary<string, float> Floats, IReadOnlyDictionary<string, float[]> Colors, AssetRecord? Ramp, AssetRecord? Mask)
{
    public const string RampSlot = "_IridescenceTexture";
    public const string MaskSlot = "_IridescenceMask";

    /// <summary>The prefab's first material on the animal shader; null when it has none.</summary>
    public static AnimalMaterial? Of(GameInstall install, IAssetReader reader, AssetIndex index, AssetRecord prefab)
    {
        foreach (var material in reader.ReadMaterials(install, prefab))
        {
            var resolved = MaterialResolver.Resolve(index, prefab.Bundle, material);
            if (!resolved.Animal) continue;
            return new AnimalMaterial(material.Name, material.Floats, material.Colors,
                resolved.Slots.FirstOrDefault(s => s.Name == RampSlot)?.Texture, resolved.Slots.FirstOrDefault(s => s.Name == MaskSlot)?.Texture);
        }
        return null;
    }
}

/// <summary>Texts shared by Check and the app.</summary>
public static class MaterialMessages
{
    public static string NotAnimalMaterial(string name, string animal) =>
        $"\"{name}\" is not this animal's material ({animal}); extra materials come in a later Tyrant.";
}
