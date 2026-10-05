using System.Numerics;
using System.Text.Json;
using Tyrant.Core.Data;
using Tyrant.Core.Errors;
using Tyrant.Core.Models;

namespace Tyrant.Core.Blender;

/// <summary>One bone's growth: which parts the game changes and its baby/adolescent/adult [px,py,pz,sx,sy,sz] (glTF space).</summary>
public sealed record BlenderGrowthBone(string Name, bool Translation, bool Scale, float[] Baby, float[] Adolescent, float[] Adult);

/// <summary>What the Growth slider needs: the blend-shape and skin curves sampled over maturity 0–1, and the bone proportions.</summary>
public sealed record BlenderGrowth(float[] Blend, float[] Skin, bool Relative, IReadOnlyList<BlenderGrowthBone> Bones);

/// <summary>Reads a species' growth (AnimalData blendGrowthCurve, skinGrowthCurve, blendShapesRelative, GrowthData.bones).</summary>
public static class BlenderGrowthReader
{
    private const string AnimalType = "PrehistoricKingdom.AnimalData";

    public static BlenderGrowth? Read(DataStore? store, string speciesId)
    {
        if (store is null) return null;
        var type = store.Types().FirstOrDefault(t => t.FullName == AnimalType);
        if (type is null) return null;
        foreach (var (_, root) in store.LoadAll(type))
        {
            if (!root.TryGetProperty("speciesID", out var id) || !string.Equals(id.GetString(), speciesId, StringComparison.OrdinalIgnoreCase)) continue;
            if (!root.TryGetProperty("blendGrowthCurve", out var blend)) return null;
            JsonElement? skin = root.TryGetProperty("skinGrowthCurve", out var s) ? s : null;
            var relative = root.TryGetProperty("blendShapesRelative", out var r) && r.ValueKind == JsonValueKind.True;
            return new BlenderGrowth(UnityCurve.Sample(blend), UnityCurve.Sample(skin), relative, Bones(root));
        }
        return null;
    }

    /// <summary>The workspace's data dump, or null when there is none (growth then follows a straight line).</summary>
    public static DataStore? TryStore(Tyrant.Core.Workspaces.Workspace ws)
    {
        try
        {
            return DataStore.Open(ws);
        }
        catch (TyrantException)
        {
            return null;
        }
    }

    private static List<BlenderGrowthBone> Bones(JsonElement root)
    {
        var bones = new List<BlenderGrowthBone>();
        if (!root.TryGetProperty("GrowthData", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("bones", out var list) || list.ValueKind != JsonValueKind.Array)
            return bones;
        foreach (var b in list.EnumerateArray())
        {
            var name = b.TryGetProperty("transformName", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            if (string.IsNullOrEmpty(name)) continue;
            // BoneTranslationScaleMode is a flags enum: Translation, Scale, All (both).
            var mode = b.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
            var all = mode.Contains("All", StringComparison.OrdinalIgnoreCase);
            bones.Add(new BlenderGrowthBone(name,
                all || mode.Contains("Translation", StringComparison.OrdinalIgnoreCase),
                all || mode.Contains("Scale", StringComparison.OrdinalIgnoreCase),
                Stage(b, "localBabyTransformation"), Stage(b, "localAdolescentTransformation"), Stage(b, "localAdultTransformation")));
        }
        return bones;
    }

    private static float[] Stage(JsonElement bone, string name)
    {
        if (!bone.TryGetProperty(name, out var stage) || stage.ValueKind != JsonValueKind.Object) return [0, 0, 0, 1, 1, 1];
        var p = UnityToGltf.Position(Vec(stage, "position", 0));
        var sc = Vec(stage, "scale", 1);
        return [p.X, p.Y, p.Z, sc.X, sc.Y, sc.Z];
    }

    private static Vector3 Vec(JsonElement e, string name, float fallback) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object
            ? new Vector3(F(v, "x", fallback), F(v, "y", fallback), F(v, "z", fallback))
            : new Vector3(fallback);

    private static float F(JsonElement v, string axis, float fallback) =>
        v.TryGetProperty(axis, out var x) && x.ValueKind == JsonValueKind.Number ? x.GetSingle() : fallback;
}
