namespace Tyrant.Core.Rigging;

public static class RigLimits
{
    /// <summary>
    /// Whether the framework re-applies rig edits after the game's growth job, so bones the growth positions or scales can be
    /// edited (proven in game: rig spike round 4). Without it those bones are refused.
    /// </summary>
    public const bool GrowthBonesSupported = false;
}
