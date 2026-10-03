using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Workspaces;

namespace Tyrant.Rpc.Studio;

/// <summary>A studio.log line, also sent to the app's log panel.</summary>
public sealed record LogNotification(string Level, string Message);

/// <summary>The workspace and game install Studio has open (one per sidecar process).</summary>
public sealed class StudioSession(StudioOptions options)
{
    public const string LogFileName = "studio.log";
    public const string LogMethod = "log";

    /// <summary>Raised for every log line (level, message), also when no workspace is open (nothing is written to disk then).</summary>
    public event Action<string, string>? Logged;

    private readonly object _lock = new();
    private readonly object _logLock = new();
    private Workspace? _workspace;
    private GameInstall? _install;

    public StudioOptions Options { get; } = options;

    /// <summary>Raised when the open workspace's dump may have changed (new dump, other workspace).</summary>
    public event Action? DataChanged;

    public (Workspace Workspace, GameInstall Install) Current()
    {
        lock (_lock)
        {
            if (_workspace is null || _install is null)
                throw new TyrantException(TyrantErrorCode.WorkspaceInvalid, "No workspace is open. Open or create one on the Workspace tab (or File → Open workspace…).", FixAction.PickWorkspaceFolder);
            return (_workspace, _install);
        }
    }

    /// <summary>The open workspace, or null when none is.</summary>
    public (Workspace Workspace, GameInstall Install)? TryCurrent()
    {
        lock (_lock) return _workspace is null || _install is null ? null : (_workspace, _install);
    }

    public void Set(Workspace workspace, GameInstall install)
    {
        lock (_lock) (_workspace, _install) = (workspace, install);
        DataChanged?.Invoke();
    }

    public void NotifyDataChanged() => DataChanged?.Invoke();

    /// <summary>Appends a line to &lt;workspace&gt;/logs/studio.log and tells the app; logging never fails an operation.</summary>
    public void Log(string message, string level = "info")
    {
        try { Logged?.Invoke(level, message); } catch (Exception) { } // a closed pipe must not fail the operation
        Workspace? ws;
        lock (_lock) ws = _workspace;
        if (ws is null) return;
        try
        {
            lock (_logLock)
            {
                Directory.CreateDirectory(ws.LogsDir);
                File.AppendAllText(Path.Combine(ws.LogsDir, LogFileName), $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
