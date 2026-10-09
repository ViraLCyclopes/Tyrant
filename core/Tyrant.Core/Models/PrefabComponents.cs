using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Tyrant.Core.Assets;

namespace Tyrant.Core.Models;

/// <summary>A component on a prefab object: its type, which of that type it is, and its serialized fields as JSON.</summary>
public sealed record PrefabComponent(string Type, string? Namespace, string? Assembly, int Index, string FieldsJson);

/// <summary>An object of a prefab by its path from the root ("" is the root), with the components prefab settings can change.</summary>
public sealed record PrefabObject(string Path, IReadOnlyList<PrefabComponent> Components);

public sealed record PrefabComponents(string Prefab, IReadOnlyList<PrefabObject> Objects);

/// <summary>Reads every game script, LODGroup and Animator on a prefab, walking the GameObjects' own component lists.</summary>
internal static class PrefabComponentReader
{
    public static PrefabComponents Read(AssetsManager manager, AssetsFileInstance file, long rootGameObjectPathId)
    {
        var rootInfo = file.file.GetAssetInfo(rootGameObjectPathId);
        var root = manager.GetBaseField(file, rootInfo);
        var found = new List<(string, string, string?, string?, string)>();
        Walk(manager, file, root, "", found);
        return new PrefabComponents(root["m_Name"].AsString, Group(found));
    }

    private static void Walk(AssetsManager manager, AssetsFileInstance file, AssetTypeValueField gameObject, string path,
        List<(string, string, string?, string?, string)> found)
    {
        AssetTypeValueField? transform = null;
        foreach (var entry in gameObject["m_Component.Array"].Children)
        {
            var ext = manager.GetExtAsset(file, entry["component"], onlyGetInfo: true);
            if (ext.info is null || ext.file is null) continue;
            switch ((AssetClassID)ext.info.TypeId)
            {
                case AssetClassID.Transform:
                case AssetClassID.RectTransform:
                    transform = manager.GetBaseField(ext.file, ext.info);
                    break;
                case AssetClassID.MonoBehaviour:
                {
                    var header = manager.GetBaseField(ext.file, ext.info, AssetReadFlags.SkipMonoBehaviourFields);
                    var script = manager.GetExtAsset(ext.file, header["m_Script"]).baseField;
                    if (script is null) continue;
                    var full = manager.GetBaseField(ext.file, ext.info);
                    found.Add((path, script["m_ClassName"].AsString, script["m_Namespace"].AsString, script["m_AssemblyName"].AsString, FieldJsonWriter.ToJson(full)));
                    break;
                }
                case AssetClassID.LODGroup:
                    found.Add((path, "LODGroup", null, null, FieldJsonWriter.ToJson(manager.GetBaseField(ext.file, ext.info))));
                    break;
                case AssetClassID.Animator:
                    found.Add((path, "Animator", null, null, FieldJsonWriter.ToJson(manager.GetBaseField(ext.file, ext.info))));
                    break;
            }
        }
        if (transform is null) return;
        foreach (var child in transform["m_Children.Array"].Children)
        {
            var childTransform = manager.GetExtAsset(file, child).baseField;
            if (childTransform is null) continue;
            var childObject = manager.GetExtAsset(file, childTransform["m_GameObject"]).baseField;
            if (childObject is null) continue;
            var name = childObject["m_Name"].AsString;
            Walk(manager, file, childObject, path.Length == 0 ? name : path + "/" + name, found);
        }
    }

    internal static IReadOnlyList<PrefabObject> Group(IEnumerable<(string Path, string Type, string? Ns, string? Asm, string Json)> found)
    {
        var objects = new List<(string Path, List<PrefabComponent> Components)>();
        foreach (var (path, type, ns, asm, json) in found)
        {
            var at = objects.FindIndex(o => o.Path == path);
            if (at < 0) { objects.Add((path, [])); at = objects.Count - 1; }
            var index = objects[at].Components.Count(c => c.Type == type);
            objects[at].Components.Add(new PrefabComponent(type, string.IsNullOrEmpty(ns) ? null : ns, asm, index, json));
        }
        return objects.Select(o => new PrefabObject(o.Path, o.Components)).ToList();
    }
}
