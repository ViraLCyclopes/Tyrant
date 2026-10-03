using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace Tyrant.Core.Models;

/// <summary>Reads a prefab's transform hierarchy and mesh renderers from a loaded bundle file.</summary>
internal static class PrefabReader
{
    public static PrefabModel Read(AssetsManager manager, AssetsFileInstance file, long rootGameObjectPathId)
    {
        var transforms = file.file.GetAssetsOfType(AssetClassID.Transform).ToDictionary(i => i.PathId, i => manager.GetBaseField(file, i));
        var transformByGameObject = new Dictionary<long, long>();
        foreach (var (id, t) in transforms) transformByGameObject.TryAdd(t["m_GameObject.m_PathID"].AsLong, id);
        if (!transformByGameObject.TryGetValue(rootGameObjectPathId, out var rootTransform))
            throw new InvalidDataException($"GameObject {rootGameObjectPathId} has no Transform in this bundle; it is not a prefab root.");

        var nodes = new Dictionary<long, SkeletonNode>();
        var root = BuildNode(manager, file, transforms, rootTransform, null, nodes);

        var renderers = new List<RendererModel>();
        var failures = new List<string>();
        foreach (var info in file.file.GetAssetsOfType(AssetClassID.SkinnedMeshRenderer))
            AddRenderer(manager, file, manager.GetBaseField(file, info), skinned: true, transformByGameObject, nodes, renderers, failures);
        foreach (var info in file.file.GetAssetsOfType(AssetClassID.MeshFilter))
            AddRenderer(manager, file, manager.GetBaseField(file, info), skinned: false, transformByGameObject, nodes, renderers, failures);

        return new PrefabModel(root.Name, root, renderers, failures);
    }

    private static SkeletonNode BuildNode(AssetsManager manager, AssetsFileInstance file, Dictionary<long, AssetTypeValueField> transforms,
        long id, SkeletonNode? parent, Dictionary<long, SkeletonNode> nodes)
    {
        var t = transforms[id];
        var gameObject = manager.GetExtAsset(file, t["m_GameObject"]).baseField;
        var node = new SkeletonNode(
            gameObject?["m_Name"].AsString ?? $"node_{id}",
            UnityToGltf.ReadVector3(t["m_LocalPosition"]),
            UnityToGltf.ReadQuaternion(t["m_LocalRotation"]),
            UnityToGltf.ReadVector3(t["m_LocalScale"]),
            parent);
        nodes[id] = node;
        foreach (var child in t["m_Children.Array"].Children)
        {
            var childId = child["m_PathID"].AsLong;
            if (transforms.ContainsKey(childId) && !nodes.ContainsKey(childId))
                node.Children.Add(BuildNode(manager, file, transforms, childId, node, nodes));
        }
        return node;
    }

    private static void AddRenderer(AssetsManager manager, AssetsFileInstance file, AssetTypeValueField renderer, bool skinned,
        Dictionary<long, long> transformByGameObject, Dictionary<long, SkeletonNode> nodes, List<RendererModel> renderers, List<string> failures)
    {
        if (!transformByGameObject.TryGetValue(renderer["m_GameObject.m_PathID"].AsLong, out var transformId)
            || !nodes.TryGetValue(transformId, out var owner))
            return; // not part of this prefab's hierarchy

        try
        {
            var meshAsset = manager.GetExtAsset(file, renderer["m_Mesh"]);
            if (meshAsset.baseField is null) throw new InvalidDataException("its mesh could not be found");
            var mesh = MeshDecoder.Decode(meshAsset.baseField);
            var bones = skinned
                ? renderer["m_Bones.Array"].Children
                    .Select(b => nodes.TryGetValue(b["m_PathID"].AsLong, out var bone) ? bone : throw new InvalidDataException("one of its bones is outside the prefab"))
                    .ToList()
                : [];
            renderers.Add(new RendererModel(owner.Name, mesh, bones, owner));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // AssetsTools.NET throws plain Exceptions for unreadable external files; keep the other renderers.
            failures.Add($"{owner.Name}: {ex.Message}");
        }
    }
}
