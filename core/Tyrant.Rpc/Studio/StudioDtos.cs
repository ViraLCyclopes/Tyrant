using Tyrant.Core.Dumping;

namespace Tyrant.Rpc.Studio;

public sealed record AppInfo(string Version, int ProtocolVersion, string Runtime);

public sealed record DiagnosticsResult(string Text);

public sealed record InstallDetectParams(string? GamePath = null);

public sealed record InstallInfo(string RootDir, string? SteamAppId, string BuildGuid);

public sealed record WorkspaceOpenParams(string Dir, string? GamePath = null);

public sealed record OutputStatus(string Name, DateTimeOffset CreatedUtc, bool Stale);

public sealed record WorkspaceStatus(string Dir, string GameRoot, string? SteamAppId, string BuildGuid, bool Stale,
    IReadOnlyList<OutputStatus> Outputs, InstallState Dumper, bool HasData, bool HasAssetIndex, bool HasSource,
    FrameworkState Framework = FrameworkState.Missing, string? FrameworkInGame = null, string FrameworkBundled = "", string? LoaderInGame = null);

public sealed record DumperInstallResult(bool InstalledLoader, string Message);

/// <summary>Out: where to write the zip; null writes &lt;workspace&gt;/exports/Tyrant-Framework-&lt;version&gt;.zip.</summary>
public sealed record GamePackageFrameworkParams(string? Out = null);

public sealed record GamePackageFrameworkResult(string Path);

/// <summary>The version on Tyrant's Nexus page and its address; all null when the page is not set up; Error when Nexus failed.</summary>
public sealed record NexusCheck(string? Version, string? Url, string? Error);

public sealed record DumperUninstallResult(bool RemovedLoader, string Message);

public sealed record DumpRunParams(int TimeoutSeconds = 300);

public sealed record DumpRunResult(int Objects, int Types, int Languages, IReadOnlyList<string> Errors);

public sealed record DecompileParams(IReadOnlyList<string>? Assemblies = null);

public sealed record DecompileItem(string Assembly, bool Success, string? Error);

public sealed record DecompileRunResult(IReadOnlyList<DecompileItem> Assemblies);

public sealed record AssetIndexRunResult(int Assets, int Failures, int MissingBundles);

public enum RefreshStepStatus
{
    Ok,
    Failed,
    Skipped,
}

public sealed record RefreshStep(string Name, RefreshStepStatus Status, string Message);

public sealed record RefreshAllResult(IReadOnlyList<RefreshStep> Steps);
