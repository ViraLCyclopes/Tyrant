using System.Numerics;
using Tyrant.Core.Data;
using Tyrant.Core.Models;

namespace Tyrant.Core.Tests;

public class ClipReaderTests
{
    private static SkeletonNode Node(string name, SkeletonNode? parent)
    {
        var node = new SkeletonNode(name, Vector3.Zero, Quaternion.Identity, Vector3.One, parent);
        parent?.Children.Add(node);
        return node;
    }

    [Fact]
    public void Crc32_is_zlibs() => Assert.Equal(0xCBF43926u, ClipReader.Crc32("123456789"));

    [Fact]
    public void Bones_whose_position_channel_moves_are_listed_in_skeleton_order()
    {
        var root = Node("Carcharodontosaurus.V2", null);
        var armature = Node("Armature", root); // the clips' paths start below the Animator's object, found by matching
        var main = Node("MainBone", armature);
        var pelvis = Node("Pelvis", main);
        var femur = Node("Femur.L", pelvis);
        var jaw = Node("Jaw", main);
        var walk = new ClipChannels("Carch|Walk",
        [
            new ClipBinding(ClipReader.Crc32("MainBone/Pelvis/Femur.L"), 1, 0.2f),
            new ClipBinding(ClipReader.Crc32("MainBone/Jaw"), 1, 0.001f), // constant: not moved
            new ClipBinding(ClipReader.Crc32("MainBone/Jaw"), 2, 1.0f),   // rotation does not count
            new ClipBinding(ClipReader.Crc32("MainBone"), 3, 0.5f),       // nor scale
        ]);
        var idle = new ClipChannels("Carch|Idle", [new ClipBinding(ClipReader.Crc32("MainBone/Pelvis"), 1, 0.05f)]);

        Assert.Equal(["Pelvis", "Femur.L"], ClipReader.MovedBones([walk, idle], root));
    }

    [Fact]
    public void No_clips_move_no_bones()
    {
        Assert.Empty(ClipReader.MovedBones([], Node("Animal", null)));
    }

    [Fact]
    public void A_species_clips_come_from_its_animation_table_in_the_data_dump()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "data");
        SkinDumps.Write(dir);
        var animal = Path.Combine(dir, "objects", "PrehistoricKingdom.AnimalData", "Carcharodontosaurus.json");
        File.WriteAllText(animal, File.ReadAllText(animal).TrimEnd().TrimEnd('}')
            + ""","animationTable":{"$ref":{"type":"PrehistoricKingdom.AnimationV2.AnimationTableV2","name":"CarchTable","id":7}}}""");
        var tables = Directory.CreateDirectory(Path.Combine(dir, "objects", "PrehistoricKingdom.AnimationV2.AnimationTableV2")).FullName;
        File.WriteAllText(Path.Combine(tables, "CarchTable.json"), """
            {"$type":"PrehistoricKingdom.AnimationV2.AnimationTableV2","$name":"CarchTable","$id":7,
             "sets":[{"_Clip":{"$ref":{"type":"UnityEngine.AnimationClip","name":"Carch|Walk","id":1}}},
                     {"nested":{"_Clip":{"$ref":{"type":"UnityEngine.AnimationClip","name":"Carch|Idle","id":2}}}},
                     {"_Clip":{"$ref":{"type":"UnityEngine.AnimationClip","name":"Carch|Walk","id":1}}},
                     {"other":{"$ref":{"type":"UnityEngine.Texture2D","name":"NotAClip","id":3}}}]}
            """);

        var names = ClipReader.ClipNames(DataStore.OpenDirectory(dir), "Carcharodontosaurus");

        Assert.Equal(["Carch|Walk", "Carch|Idle"], names);
        Assert.Empty(ClipReader.ClipNames(DataStore.OpenDirectory(dir), "Stegosaurus"));
    }
}
