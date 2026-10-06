using Tyrant.Core.Animation;
using Tyrant.Core.Assets;
using Tyrant.Core.Models;

namespace Tyrant.Core.Tests;

/// <summary>
/// Animations for the test species (SkinDumps' Carcharodontosaurus on ModelFixture's Animal/Hip/Tail skeleton): a data-dump
/// animation table naming a walk (Hip travels 1 m), a roar (Tail turns) and a clip the index lacks.
/// </summary>
public static class AnimationFixture
{
    public static readonly AssetRecord Walk = new("@data/sharedassets0.assets", 501, "AnimationClip", "Carch|Walk", null, null, null);
    public static readonly AssetRecord Roar = new("@data/sharedassets0.assets", 502, "AnimationClip", "Carch|Roar", null, null, null);

    public static RawClip WalkClip()
    {
        var dense = Enumerable.Range(0, 31).SelectMany(f => new[] { 0f, 0f, f / 30f }).ToArray();
        return new RawClip("Carch|Walk", 30, 0, 1, true, 0, [], 3, 31, 0, dense, [], [new ClipBindingRaw(ClipReader.Crc32("Hip"), 1)]);
    }

    public static RawClip RoarClip() =>
        new("Carch|Roar", 30, 0, 2, false, 0, [], 0, 0, 0, [], [0, 0, 0.3826834f, 0.9238795f], [new ClipBindingRaw(ClipReader.Crc32("Hip/Tail"), 2)]);

    /// <summary>Adds the animation table to a data dump written by SkinDumps.Write.</summary>
    public static void WriteTable(string dataDir)
    {
        var animal = Path.Combine(dataDir, "objects", "PrehistoricKingdom.AnimalData", "Carcharodontosaurus.json");
        File.WriteAllText(animal, File.ReadAllText(animal).TrimEnd().TrimEnd('}')
            + ""","animationTable":{"$ref":{"type":"PrehistoricKingdom.AnimationV2.AnimationTableV2","name":"CarchTable","id":7}}}""");
        var tables = Directory.CreateDirectory(Path.Combine(dataDir, "objects", "PrehistoricKingdom.AnimationV2.AnimationTableV2")).FullName;
        File.WriteAllText(Path.Combine(tables, "CarchTable.json"), """
            {"$type":"PrehistoricKingdom.AnimationV2.AnimationTableV2","$name":"CarchTable","$id":7,
             "a":{"_Clip":{"$ref":{"type":"UnityEngine.AnimationClip","name":"Carch|Walk","id":1}}},
             "b":{"_Clip":{"$ref":{"type":"UnityEngine.AnimationClip","name":"Carch|Roar","id":2}}}}
            """);
    }
}
