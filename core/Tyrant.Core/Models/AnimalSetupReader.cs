using System.Text.Json;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Tyrant.Core.Assets;

namespace Tyrant.Core.Models;

/// <summary>The game's AnimalSetup on an animal prefab: which bones it treats as the head and the neck (head look, IK).</summary>
public sealed record AnimalSetupBones(SkeletonNode Head, IReadOnlyList<SkeletonNode> Necks);

/// <summary>Reads AnimalSetup (Head, Necks) from a prefab, the way <see cref="FabrikReader"/> reads the IK chains.</summary>
public static class AnimalSetupReader
{
    internal static AnimalSetupBones? Read(AssetsManager manager, AssetsFileInstance file, Dictionary<long, long> transformByGameObject,
        Dictionary<long, SkeletonNode> nodes)
    {
        foreach (var info in file.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            AssetTypeValueField header;
            try
            {
                header = manager.GetBaseField(file, info, AssetReadFlags.SkipMonoBehaviourFields);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                continue;
            }
            if (!transformByGameObject.ContainsKey(header["m_GameObject.m_PathID"].AsLong)) continue;
            if (AssetIndexer.ScriptClass(manager, file, header) != "AnimalSetup") continue;
            try
            {
                using var json = JsonDocument.Parse(FieldJsonWriter.ToJson(manager.GetBaseField(file, info)));
                return Parse(json.RootElement, id => nodes.GetValueOrDefault(id));
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or InvalidDataException)
            {
                return null; // the head look correction is skipped; nothing else depends on it
            }
        }
        return null;
    }

    /// <summary>AnimalSetup's fields → its head and neck bones; null when the head or every neck is missing from the skeleton.</summary>
    public static AnimalSetupBones? Parse(JsonElement fields, Func<long, SkeletonNode?> nodeOf)
    {
        if (!fields.TryGetProperty("Head", out var head) || nodeOf(head.GetProperty("m_PathID").GetInt64()) is not { } headNode) return null;
        var necks = new List<SkeletonNode>();
        if (fields.TryGetProperty("Necks", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var neck in list.EnumerateArray())
                if (nodeOf(neck.GetProperty("m_PathID").GetInt64()) is { } node) necks.Add(node);
        return necks.Count == 0 ? null : new AnimalSetupBones(headNode, necks);
    }
}
