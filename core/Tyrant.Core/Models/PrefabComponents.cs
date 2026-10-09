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
            try
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
                        var className = AssetIndexer.ScriptClass(manager, ext.file, header);
                        if (className is null) continue; // a script we cannot resolve is skipped
                        var script = manager.GetExtAsset(ext.file, header["m_Script"]).baseField!;
                        var full = manager.GetBaseField(ext.file, ext.info);
                        found.Add((path, className, Text(script["m_Namespace"]), Text(script["m_AssemblyName"]), FieldJsonWriter.ToJson(full)));
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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One unreadable component must not fail the whole prefab; it is left out.
            }
        }
        if (transform is null) return;
        foreach (var child in transform["m_Children.Array"].Children)
        {
            try
            {
                var childTransform = manager.GetExtAsset(file, child);
                if (childTransform.baseField is null) continue;
                var childObject = manager.GetExtAsset(childTransform.file, childTransform.baseField["m_GameObject"]);
                if (childObject.baseField is null) continue;
                var name = childObject.baseField["m_Name"].AsString;
                Walk(manager, childObject.file, childObject.baseField, path.Length == 0 ? name : path + "/" + name, found);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A child that cannot be resolved is left out with its subtree.
            }
        }
    }

    private static string? Text(AssetTypeValueField field) => field.IsDummy ? null : field.AsString;

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
