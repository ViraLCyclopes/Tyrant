namespace Tyrant.Core.Rigging;

public static class RigLimits
{
    /// <summary>
    /// Whether the framework re-applies rig edits after the game's growth job, so bones the growth positions or scales can be
    /// edited (proven in game: rig spike round 4). Without it those bones are refused.
    /// </summary>
    public const bool GrowthBonesSupported = false;
}

/// <summary>What Check, Send and the panels say about a rig edit, from the species' rig info.</summary>
public static class RigRules
{
    public static (List<string> Errors, List<string> Warnings) Problems(IReadOnlyDictionary<string, Tyrant.Framework.Core.RigOffset> rig, RigInfo info,
        bool hasModel, string where)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var bones = info.Bones.ToHashSet(StringComparer.Ordinal);
        static string Are(List<string> names) => names.Count == 1 ? "is" : "are";

        var missing = rig.Keys.Where(b => !bones.Contains(b)).Order(StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
            errors.Add($"{where}: rig edit bone(s) {string.Join(", ", missing)} {Are(missing)} not in the species' skeleton (renamed by a game update?); remove them from the rig edit.");
        var clip = info.ClipMoved.Where(rig.ContainsKey).ToList();
        if (clip.Count > 0)
            warnings.Add($"{where}: {string.Join(", ", clip)} {Are(clip)} moved by the game's animations: the rig edit changes that motion.");
        var grown = info.Bones.Where(b => rig.ContainsKey(b) && (info.GrowthMoved.Contains(b) || info.GrowthScaled.Contains(b))).ToList();
        if (grown.Count > 0)
        {
            if (RigLimits.GrowthBonesSupported)
                warnings.Add($"{where}: {string.Join(", ", grown)} {Are(grown)} positioned or scaled by the game's growth: the rig edit is applied again after it.");
            else
                errors.Add($"{where}: {string.Join(", ", grown)} {Are(grown)} positioned or scaled by the game's growth, which puts {(grown.Count == 1 ? "it" : "them")} back every frame; rig edits on growth bones are not supported yet. Remove {(grown.Count == 1 ? "it" : "them")} from the rig edit (Clear rig edit in Blender, then edit the other bones).");
        }
        if (!hasModel) warnings.Add($"{where}: the rig edit has no model of its own: the game's mesh stretches with the moved bones.");
        return (errors, warnings);
    }
}
