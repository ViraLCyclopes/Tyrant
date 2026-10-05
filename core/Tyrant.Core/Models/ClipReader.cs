using System.Text;
using System.Text.Json;
using AssetsTools.NET;
using Tyrant.Core.Data;

namespace Tyrant.Core.Models;

/// <summary>One bound channel of a clip: the bone (CRC32 of its path below the Animator), what it sets, and how far it moves.</summary>
/// <param name="Attribute">Unity's generic binding attribute: 1 position, 2 rotation, 3 scale, 4 euler.</param>
/// <param name="Range">The largest change of any of its curves across the clip (0 for a constant).</param>
public sealed record ClipBinding(uint PathHash, int Attribute, float Range);

public sealed record ClipChannels(string Name, IReadOnlyList<ClipBinding> Bindings);

/// <summary>
/// A species' animation clips, for rig editing's warnings: which bones the game's animations really move (not only hold
/// at their prefab place). The clips come from the species' animation table in the data dump.
/// </summary>
public static class ClipReader
{
    private const string AnimalType = "PrehistoricKingdom.AnimalData";
    private const string ClipType = "UnityEngine.AnimationClip";

    /// <summary>A position that changes more than this (metres) in a clip counts as moved by the animations.</summary>
    public const float MovedThreshold = 0.01f;

    /// <summary>The clip names of the species' AnimalData.animationTable, in table order, each once; empty when unknown.</summary>
    public static IReadOnlyList<string> ClipNames(DataStore store, string speciesId)
    {
        var animals = store.Types().FirstOrDefault(t => t.FullName == AnimalType);
        if (animals is null) return [];
        foreach (var (_, root) in store.LoadAll(animals))
        {
            if (!root.TryGetProperty("speciesID", out var id) || !string.Equals(id.GetString(), speciesId, StringComparison.OrdinalIgnoreCase)) continue;
            if (!root.TryGetProperty("animationTable", out var table) || !table.TryGetProperty("$ref", out var reference)) return [];
            var typeName = reference.TryGetProperty("type", out var t) ? t.GetString() : null;
            var name = reference.TryGetProperty("name", out var n) ? n.GetString() : null;
            var type = store.Types().FirstOrDefault(x => x.FullName == typeName);
            if (type is null || name is null || !store.ObjectNames(type).Contains(name, StringComparer.OrdinalIgnoreCase)) return [];
            var names = new List<string>();
            Collect(store.Load(type, name), names);
            return names.Distinct(StringComparer.Ordinal).ToList();
        }
        return [];
    }

    private static void Collect(JsonElement element, List<string> names)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("$ref", out var reference) && reference.ValueKind == JsonValueKind.Object
                    && reference.TryGetProperty("type", out var type) && type.GetString() == ClipType
                    && reference.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } clip)
                {
                    names.Add(clip);
                    return;
                }
                foreach (var property in element.EnumerateObject()) Collect(property.Value, names);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Collect(item, names);
                break;
        }
    }

    /// <summary>A clip's bindings and how far each moves: streamed, dense and constant curves (Mecanim's muscle clip).</summary>
    public static ClipChannels Decode(AssetTypeValueField clip)
    {
        var data = clip["m_MuscleClip"]["m_Clip"]["data"];
        var streamed = data["m_StreamedClip"];
        var streamedCount = (int)streamed["curveCount"].AsUInt;
        var words = streamed["data"]["Array"].Children.Select(c => c.AsUInt).ToArray();
        var dense = data["m_DenseClip"];
        var denseCount = dense["m_CurveCount"].AsInt;
        var frames = dense["m_FrameCount"].AsInt;
        var samples = dense["m_SampleArray"]["Array"].Children.Select(c => c.AsFloat).ToArray();
        var constant = data["m_ConstantClip"]["data"]["Array"].Children.Select(c => c.AsFloat).ToArray();
        var total = streamedCount + denseCount + constant.Length;
        var min = Enumerable.Repeat(float.MaxValue, total).ToArray();
        var max = Enumerable.Repeat(float.MinValue, total).ToArray();
        void See(int curve, float value)
        {
            if (curve < 0 || curve >= total || float.IsNaN(value)) return;
            min[curve] = Math.Min(min[curve], value);
            max[curve] = Math.Max(max[curve], value);
        }
        // Streamed: frames of (time, key count, keys of (curve index, 4 coefficients)); the 4th coefficient is the value at the key.
        for (var p = 0; p + 1 < words.Length;)
        {
            var keys = (int)words[p + 1];
            p += 2;
            for (var k = 0; k < keys && p + 4 < words.Length; k++, p += 5)
                See((int)words[p], BitConverter.UInt32BitsToSingle(words[p + 4]));
        }
        for (var f = 0; f < frames; f++)
            for (var c = 0; c < denseCount && f * denseCount + c < samples.Length; c++)
                See(streamedCount + c, samples[f * denseCount + c]);
        for (var c = 0; c < constant.Length; c++) See(streamedCount + denseCount + c, constant[c]);

        var bindings = new List<ClipBinding>();
        var offset = 0;
        foreach (var b in clip["m_ClipBindingConstant"]["genericBindings"]["Array"].Children)
        {
            var attribute = (int)b["attribute"].AsUInt;
            var dims = attribute switch { 1 => 3, 2 => 4, 3 => 3, 4 => 3, _ => 1 };
            var animated = offset < streamedCount + denseCount;
            var range = animated
                ? Enumerable.Range(offset, dims).Where(i => i < total && max[i] >= min[i]).Select(i => max[i] - min[i]).DefaultIfEmpty(0).Max()
                : 0f;
            bindings.Add(new ClipBinding(b["path"].AsUInt, attribute, range));
            offset += dims;
        }
        return new ClipChannels(clip["m_Name"].AsString, bindings);
    }

    /// <summary>
    /// Bones whose position some clip moves more than the threshold, in skeleton order. Clip paths start below the Animator's
    /// object: of the prefab's top nodes, the one whose paths match the most bindings is taken as that object.
    /// </summary>
    public static IReadOnlyList<string> MovedBones(IReadOnlyList<ClipChannels> clips, SkeletonNode prefabRoot, float threshold = MovedThreshold)
    {
        var moved = clips.SelectMany(c => c.Bindings).Where(b => b.Attribute == 1 && b.Range > threshold).Select(b => b.PathHash).ToHashSet();
        if (moved.Count == 0) return [];
        var bound = clips.SelectMany(c => c.Bindings).Select(b => b.PathHash).ToHashSet();
        var candidates = Within(prefabRoot, 3).ToList();
        var animatorRoot = candidates.MaxBy(c => PathsBelow(c).Count(p => bound.Contains(p.Hash))) ?? prefabRoot;
        return PathsBelow(animatorRoot).Where(p => moved.Contains(p.Hash)).Select(p => p.Node.Name).ToList();
    }

    private static IEnumerable<SkeletonNode> Within(SkeletonNode node, int depth)
    {
        yield return node;
        if (depth == 0) yield break;
        foreach (var child in node.Children)
            foreach (var n in Within(child, depth - 1)) yield return n;
    }

    /// <summary>Every node below <paramref name="root"/> with the CRC32 of its path ("A/B/C", the root left out), depth first.</summary>
    private static IEnumerable<(SkeletonNode Node, uint Hash)> PathsBelow(SkeletonNode root)
    {
        var stack = new Stack<(SkeletonNode Node, string Path)>();
        for (var i = root.Children.Count - 1; i >= 0; i--) stack.Push((root.Children[i], root.Children[i].Name));
        while (stack.Count > 0)
        {
            var (node, path) = stack.Pop();
            yield return (node, Crc32(path));
            for (var i = node.Children.Count - 1; i >= 0; i--) stack.Push((node.Children[i], path + "/" + node.Children[i].Name));
        }
    }

    private static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    /// <summary>Standard (zlib) CRC32 of the UTF-8 text, as Unity hashes binding paths.</summary>
    public static uint Crc32(string text)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in Encoding.UTF8.GetBytes(text)) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }
}
