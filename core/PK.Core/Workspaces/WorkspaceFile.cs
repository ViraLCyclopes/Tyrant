using PK.Core.Install;

namespace PK.Core.Workspaces;

/// <summary>Serialized form of pkws.json.</summary>
public sealed class WorkspaceFile
{
    public int SchemaVersion { get; set; } = 1;
    public string GameRoot { get; set; } = "";
    public GameFingerprint? Fingerprint { get; set; }

    /// <summary>Files this toolset added to the game folder (BepInEx, plugins) — removed on uninstall.</summary>
    public List<string> InstalledFiles { get; set; } = [];

    /// <summary>Output name ("source", "assets", "data") → build it was generated from.</summary>
    public Dictionary<string, OutputStamp> Outputs { get; set; } = [];
}

public sealed record OutputStamp(GameFingerprint Fingerprint, DateTimeOffset CreatedUtc);
