using PK.Core.Install;

namespace PK.Core.Workspaces;

/// <summary>Serialized form of pkws.json.</summary>
public sealed class WorkspaceFile
{
    public int SchemaVersion { get; set; } = 1;
    public string GameRoot { get; set; } = "";
    public GameFingerprint? Fingerprint { get; set; }

    /// <summary>Unused since Plan 4: the install record lives with the install (UserData/PKModStudio/install.json).</summary>
    public List<string> InstalledFiles { get; set; } = [];

    /// <summary>Output name ("source", "assets", "data") → build it was generated from.</summary>
    public Dictionary<string, OutputStamp> Outputs { get; set; } = [];
}

public sealed record OutputStamp(GameFingerprint Fingerprint, DateTimeOffset CreatedUtc);
