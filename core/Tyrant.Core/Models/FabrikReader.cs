using System.Numerics;
using System.Text.Json;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Tyrant.Core.Assets;

namespace Tyrant.Core.Models;

/// <summary>Reads the game's FABRIK IK chains (FABRIKComponentAnimal MonoBehaviours) of a prefab.</summary>
public static class FabrikReader
{
    private static readonly HashSet<string> Scripts = new(StringComparer.Ordinal) { "FABRIKComponentAnimal", "FABRIKComponentGeneral" };

    /// <summary>Every FABRIK component on a GameObject of this prefab; a component that cannot be read is listed in Failures.</summary>
    internal static (List<IkChain> Chains, List<string> Failures) ReadAll(AssetsManager manager, AssetsFileInstance file,
        Dictionary<long, long> transformByGameObject, Dictionary<long, SkeletonNode> nodes)
    {
        var chains = new List<IkChain>();
        var failures = new List<string>();
        foreach (var info in file.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            AssetTypeValueField mono;
            try
            {
                mono = manager.GetBaseField(file, info);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                continue; // a MonoBehaviour without a readable layout is not one of ours
            }
            if (!transformByGameObject.TryGetValue(mono["m_GameObject.m_PathID"].AsLong, out var transform) || !nodes.TryGetValue(transform, out var owner))
                continue;
            if (AssetIndexer.ScriptClass(manager, file, mono) is not { } script || !Scripts.Contains(script)) continue;
            try
            {
                using var json = JsonDocument.Parse(FieldJsonWriter.ToJson(mono));
                chains.Add(Parse(json.RootElement, id => nodes.GetValueOrDefault(id)));
            }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                failures.Add($"IK chain on {owner.Name}: {ex.Message}");
            }
        }
        return (chains, failures);
    }

    /// <summary>One component's fields (as FieldJsonWriter writes them) → its chain; nodeOf maps a Transform path id to the skeleton.</summary>
    public static IkChain Parse(JsonElement fields, Func<long, SkeletonNode?> nodeOf)
    {
        var type = fields.GetProperty("queryData").GetProperty("queryType").GetInt32();
        var kind = type switch
        {
            1 => IkChainKind.Limb,
            2 => IkChainKind.Head,
            _ => throw new InvalidDataException($"its solver type {type} is neither a limb nor a head"),
        };
        var joints = new List<IkJoint>();
        foreach (var joint in fields.GetProperty("chain").EnumerateArray())
        {
            var id = joint.GetProperty("jointTransform").GetProperty("m_PathID").GetInt64();
            var node = nodeOf(id) ?? throw new InvalidDataException($"joint {joints.Count + 1} (#{id}) is not in the prefab's skeleton");
            var forces = new List<IkForce>();
            if (joint.TryGetProperty("forces", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var force in list.EnumerateArray())
                    if (nodeOf(force.GetProperty("nonchainTransform").GetProperty("m_PathID").GetInt64()) is { } bone)
                        forces.Add(new IkForce(bone, Vec(force.GetProperty("localDirection")), force.GetProperty("strength").GetSingle()));
            joints.Add(new IkJoint(node, joint.GetProperty("boneLength").GetSingle(), forces));
        }
        if (joints.Count < 2) throw new InvalidDataException($"it has {joints.Count} joint(s); a chain needs two or more");
        // FABRIKQueryDataHeadNeck is stored in raw bytes; its first byte is matchHeadRotation.
        var match = kind == IkChainKind.Head
            && fields.GetProperty("queryData").TryGetProperty("rawQueryData", out var raw)
            && raw.TryGetProperty("internalDataA", out var a) && a.TryGetProperty("c0", out var c0) && c0.TryGetProperty("x", out var x)
            && (BitConverter.SingleToInt32Bits(x.GetSingle()) & 0xFF) != 0;
        return new IkChain(kind, fields.GetProperty("core").GetProperty("influence").GetSingle(), joints,
            Vec(fields.GetProperty("endLocalOffset")), match);
    }

    private static Vector3 Vec(JsonElement e) => new(e.GetProperty("x").GetSingle(), e.GetProperty("y").GetSingle(), e.GetProperty("z").GetSingle());
}
