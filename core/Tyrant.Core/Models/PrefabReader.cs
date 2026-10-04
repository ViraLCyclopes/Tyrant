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
        var externals = file.file.Metadata.Externals.Select(e => e.PathName).ToList();

        var renderers = new List<RendererModel>();
        var failures = new List<string>();
        var materialFailures = new List<string>();
        foreach (var info in file.file.GetAssetsOfType(AssetClassID.SkinnedMeshRenderer))
            AddRenderer(manager, file, manager.GetBaseField(file, info), skinned: true, transformByGameObject, nodes, renderers, failures, materialFailures, externals);
        // A static mesh is a MeshFilter (the mesh) plus a MeshRenderer on the same GameObject (the materials).
        var meshRenderers = new Dictionary<long, AssetTypeValueField>();
        foreach (var info in file.file.GetAssetsOfType(AssetClassID.MeshRenderer))
        {
            var meshRenderer = manager.GetBaseField(file, info);
            meshRenderers.TryAdd(meshRenderer["m_GameObject.m_PathID"].AsLong, meshRenderer);
        }
        foreach (var info in file.file.GetAssetsOfType(AssetClassID.MeshFilter))
            AddRenderer(manager, file, manager.GetBaseField(file, info), skinned: false, transformByGameObject, nodes, renderers, failures, materialFailures, externals, meshRenderers);

        return new PrefabModel(root.Name, root, renderers, failures) { MaterialFailures = materialFailures };
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
        Dictionary<long, long> transformByGameObject, Dictionary<long, SkeletonNode> nodes, List<RendererModel> renderers, List<string> failures, List<string> materialFailures,
        IReadOnlyList<string> externals, Dictionary<long, AssetTypeValueField>? meshRenderers = null)
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
            // Skinned renderers carry their own materials; a MeshFilter's are on its GameObject's MeshRenderer.
            var materialSource = skinned ? renderer : meshRenderers?.GetValueOrDefault(renderer["m_GameObject.m_PathID"].AsLong);
            var materials = materialSource is null ? [] : ReadMaterials(manager, file, materialSource, externals, owner.Name, materialFailures);
            renderers.Add(new RendererModel(owner.Name, mesh, bones, owner) { Materials = materials });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // AssetsTools.NET throws plain Exceptions for unreadable external files; keep the other renderers.
            failures.Add($"{owner.Name}: {ex.Message}");
        }
    }

    /// <summary>One material per sub-mesh slot; a material kept in another bundle is listed without textures (drawn plain).</summary>
    private static List<MaterialModel> ReadMaterials(AssetsManager manager, AssetsFileInstance file, AssetTypeValueField renderer,
        IReadOnlyList<string> externals, string owner, List<string> materialFailures) =>
        renderer["m_Materials.Array"].Children
            .Select((m, i) => m["m_FileID"].AsInt == 0 && file.file.GetAssetInfo(m["m_PathID"].AsLong) is { } info
                ? ReadMaterialSafely(() => MaterialReader.Read(manager.GetBaseField(file, info), externals), owner, i, materialFailures)
                : new MaterialModel("", []))
            .ToList();

    /// <summary>A material that cannot be read becomes a plain one, so its mesh is still shown (C12).</summary>
    internal static MaterialModel ReadMaterialSafely(Func<MaterialModel> read, string owner, int slot, List<string> failures)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failures.Add($"{owner}: material {slot + 1} could not be read ({ex.Message}); it is drawn plain.");
            return new MaterialModel("", []);
        }
    }
}
