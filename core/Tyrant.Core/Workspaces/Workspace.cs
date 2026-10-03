using System.Text.Json;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;

namespace Tyrant.Core.Workspaces;

/// <summary>A user-chosen folder holding everything the toolset generates. Never inside the game folder.</summary>
public sealed class Workspace
{
    public const string FileName = "tyrant-workspace.json";

    /// <summary>Workspace file name before the toolkit was renamed to Tyrant; renamed on open.</summary>
    public const string LegacyFileName = "pkws.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private Workspace(string dir, WorkspaceFile data)
    {
        Dir = dir;
        Data = data;
    }

    public string Dir { get; }
    public WorkspaceFile Data { get; }
    public string SourceDir => Path.Combine(Dir, "source");
    public string DataDir => Path.Combine(Dir, "data");
    public string AssetsDir => Path.Combine(Dir, "assets");
    public string CacheDir => Path.Combine(Dir, "cache");
    public string LogsDir => Path.Combine(Dir, "logs");

    public static Workspace Create(string dir, GameInstall install)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
        EnsureOutsideGame(full, install.RootDir);
        if (File.Exists(Path.Combine(full, FileName)))
            throw new TyrantException(TyrantErrorCode.WorkspaceInvalid,
                $"'{full}' is already a workspace. Use 'tyrant workspace status' or choose an empty folder.",
                FixAction.PickWorkspaceFolder);

        var ws = new Workspace(full, new WorkspaceFile
        {
            GameRoot = install.RootDir,
            Fingerprint = GameFingerprint.Compute(install),
        });
        try
        {
            foreach (var sub in new[] { ws.SourceDir, ws.DataDir, ws.AssetsDir, ws.CacheDir, ws.LogsDir })
                Directory.CreateDirectory(sub);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw Invalid($"Cannot create a workspace at '{full}': {ex.Message}", ex);
        }
        ws.Save();
        return ws;
    }

    public static Workspace Open(string dir)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
        var file = Path.Combine(full, FileName);
        var legacy = Path.Combine(full, LegacyFileName);
        if (!File.Exists(file) && File.Exists(legacy))
        {
            try
            {
                File.Move(legacy, file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw Invalid($"Cannot rename {legacy} to {FileName}: {ex.Message}", ex);
            }
        }
        if (!File.Exists(file))
            throw Invalid($"'{full}' is not a workspace (no {FileName}). Run 'tyrant workspace init' first.");
        WorkspaceFile? data;
        try
        {
            data = JsonSerializer.Deserialize<WorkspaceFile>(File.ReadAllText(file), Json);
        }
        catch (JsonException ex)
        {
            throw Invalid($"{file} is corrupt: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw Invalid($"Cannot read {file}: {ex.Message}", ex);
        }
        if (data is null) throw Invalid($"{file} is empty.");
        if (string.IsNullOrWhiteSpace(data.GameRoot)) throw Invalid($"{file} is corrupt: it does not name the game folder (gameRoot).");
        data.Outputs ??= [];
        data.InstalledFiles ??= [];
        return new Workspace(full, data);
    }

    /// <summary>Writes tyrant-workspace.json via a temp file so a crash never leaves it half-written.</summary>
    public void Save()
    {
        var file = Path.Combine(Dir, FileName);
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Data, Json));
            File.Move(tmp, file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw Invalid($"Cannot write {file}: {ex.Message}", ex);
        }
    }

    public void StampOutput(string name, GameFingerprint fingerprint)
    {
        Data.Outputs[name] = new OutputStamp(fingerprint, DateTimeOffset.UtcNow);
        Save();
    }

    public void RemoveOutput(string name)
    {
        if (Data.Outputs.Remove(name)) Save();
    }

    /// <summary>Points the workspace at a (moved) game install.</summary>
    public void SetGameRoot(string gameRoot)
    {
        Data.GameRoot = gameRoot;
        Save();
    }

    public IReadOnlyList<string> StaleOutputs(GameFingerprint current) =>
        Data.Outputs.Where(kv => kv.Value.Fingerprint != current).Select(kv => kv.Key).Order().ToList();

    /// <summary>True when any generated output came from a different game build than <paramref name="current"/>.</summary>
    public bool IsStale(GameFingerprint current) => StaleOutputs(current).Count > 0;

    internal static void EnsureOutsideGame(string fullDir, string gameRoot)
    {
        if (PathGuard.IsInside(fullDir, gameRoot))
            throw new TyrantException(TyrantErrorCode.WorkspaceInGameFolder,
                $"The workspace must not be inside the game folder ('{gameRoot}'). Mods and tools never write game files.",
                FixAction.PickWorkspaceFolder);
    }

    private static TyrantException Invalid(string message, Exception? inner = null) =>
        new(TyrantErrorCode.WorkspaceInvalid, message, FixAction.PickWorkspaceFolder, inner);
}
