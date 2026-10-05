using System.Numerics;
using System.Text.Json;
using Tyrant.Core.Models;

namespace Tyrant.Core.Tests;

public class FabrikReaderTests
{
    private static Dictionary<long, SkeletonNode> Skeleton()
    {
        var root = new SkeletonNode("Animal", Vector3.Zero, Quaternion.Identity, Vector3.One);
        var hip = new SkeletonNode("Hip", new Vector3(0, 1, 0), Quaternion.Identity, Vector3.One, root);
        var femur = new SkeletonNode("Femur.L", new Vector3(0.3f, 0, 0), Quaternion.Identity, Vector3.One, hip);
        var calve = new SkeletonNode("Calve.L", new Vector3(0, -0.5f, 0.1f), Quaternion.Identity, Vector3.One, femur);
        var heel = new SkeletonNode("Heel.L", new Vector3(0, -0.4f, 0), Quaternion.Identity, Vector3.One, calve);
        return new() { [10] = hip, [11] = femur, [12] = calve, [13] = heel };
    }

    // As FieldJsonWriter writes a FABRIKComponentAnimal (trimmed to the fields Tyrant reads).
    private const string Limb = """
        {
          "core": { "influence": 0.75, "numberOfJoints": 3 },
          "queryData": { "queryType": 1, "rawQueryData": { "internalDataA": { "c0": { "x": 0, "y": 0, "z": 0, "w": 0 } } } },
          "chain": [
            { "jointTransform": { "m_FileID": 0, "m_PathID": 11 }, "forces": [], "boneLength": 0.5 },
            { "jointTransform": { "m_FileID": 0, "m_PathID": 12 }, "forces": [
                { "nonchainTransform": { "m_FileID": 0, "m_PathID": 10 }, "localDirection": { "x": 0, "y": 0, "z": 1 }, "strength": 0.4 },
                { "nonchainTransform": { "m_FileID": 0, "m_PathID": 999 }, "localDirection": { "x": 1, "y": 0, "z": 0 }, "strength": 0.2 } ],
              "boneLength": 0.4 },
            { "jointTransform": { "m_FileID": 0, "m_PathID": 13 }, "forces": [], "boneLength": 0 }
          ],
          "endLocalOffset": { "x": 0, "y": 0.07, "z": 0 }
        }
        """;

    private static IkChain Parse(string json)
    {
        var nodes = Skeleton();
        using var doc = JsonDocument.Parse(json);
        return FabrikReader.Parse(doc.RootElement, id => nodes.GetValueOrDefault(id));
    }

    [Fact]
    public void A_limb_chain_has_its_joints_forces_end_offset_and_influence()
    {
        var chain = Parse(Limb);

        Assert.Equal(IkChainKind.Limb, chain.Kind);
        Assert.Equal(0.75f, chain.Influence);
        Assert.Equal(["Femur.L", "Calve.L", "Heel.L"], chain.Joints.Select(j => j.Node.Name));
        Assert.Equal([0.5f, 0.4f, 0f], chain.Joints.Select(j => j.Length));
        var force = Assert.Single(chain.Joints[1].Forces); // the force on a transform outside the prefab is dropped
        Assert.Equal("Hip", force.Bone.Name);
        Assert.Equal(new Vector3(0, 0, 1), force.LocalDirection);
        Assert.Equal(0.4f, force.Strength);
        Assert.Equal(new Vector3(0, 0.07f, 0), chain.EndLocalOffset);
        Assert.False(chain.MatchHeadRotation);
    }

    [Fact]
    public void A_head_chain_reads_match_head_rotation_from_the_first_byte_of_its_query_data()
    {
        var head = Limb.Replace("\"queryType\": 1", "\"queryType\": 2").Replace("\"c0\": { \"x\": 0", "\"c0\": { \"x\": 1E-45");

        var chain = Parse(head);

        Assert.Equal(IkChainKind.Head, chain.Kind);
        Assert.True(chain.MatchHeadRotation);
    }

    [Fact]
    public void Another_solver_type_is_refused_with_the_reason()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Parse(Limb.Replace("\"queryType\": 1", "\"queryType\": 0")));
        Assert.Contains("neither a limb nor a head", ex.Message);
    }

    [Fact]
    public void A_joint_outside_the_skeleton_is_refused_with_the_reason()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Parse(Limb.Replace("\"m_PathID\": 13", "\"m_PathID\": 77")));
        Assert.Contains("not in the prefab's skeleton", ex.Message);
    }

    [Fact]
    public void A_chain_with_one_joint_is_refused()
    {
        const string single = """
            { "core": { "influence": 1 }, "queryData": { "queryType": 1 },
              "chain": [ { "jointTransform": { "m_PathID": 11 }, "forces": [], "boneLength": 0.5 } ],
              "endLocalOffset": { "x": 0, "y": 0, "z": 0 } }
            """;
        Assert.Throws<InvalidDataException>(() => Parse(single));
    }
}
