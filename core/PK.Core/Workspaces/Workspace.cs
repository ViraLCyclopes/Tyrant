using System.Text.Json;
using PK.Core.Errors;
using PK.Core.Install;

namespace PK.Core.Workspaces;

/// <summary>A user-chosen folder holding everything the toolset generates. Never inside the game folder.</summary>
public sealed class Workspace
{
    public const string FileName = "pkws.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

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
            throw new PkException(PkErrorCode.WorkspaceInvalid,
                $"'{full}' is already a workspace. Use 'pk workspace status' or choose an empty folder.",
                FixAction.PickWorkspaceFolder);

        var ws = new Workspace(full, new WorkspaceFile
        {
            GameRoot = install.RootDir,
            Fingerprint = GameFingerprint.Compute(install),
        });
        foreach (var sub in new[] { ws.SourceDir, ws.DataDir, ws.AssetsDir, ws.CacheDir, ws.LogsDir })
            Directory.CreateDirectory(sub);
        ws.Save();
        return ws;
    }

    public static Workspace Open(string dir)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
        var file = Path.Combine(full, FileName);
        if (!File.Exists(file))
            throw Invalid($"'{full}' is not a workspace (no {FileName}). Run 'pk workspace init' first.");
        try
        {
            var data = JsonSerializer.Deserialize<WorkspaceFile>(File.ReadAllText(file), Json)
                ?? throw Invalid($"{file} is empty.");
            return new Workspace(full, data);
        }
        catch (JsonException ex)
        {
            throw Invalid($"{file} is corrupt: {ex.Message}", ex);
        }
    }

    /// <summary>Writes pkws.json via a temp file so a crash never leaves it half-written.</summary>
    public void Save()
    {
        Directory.CreateDirectory(Dir);
        var file = Path.Combine(Dir, FileName);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Data, Json));
        File.Move(tmp, file, overwrite: true);
    }

    public void StampOutput(string name, GameFingerprint fingerprint)
    {
        Data.Outputs[name] = new OutputStamp(fingerprint, DateTimeOffset.UtcNow);
        Save();
    }

    public IReadOnlyList<string> StaleOutputs(GameFingerprint current) =>
        Data.Outputs.Where(kv => kv.Value.Fingerprint != current).Select(kv => kv.Key).Order().ToList();

    public bool IsStale(GameFingerprint current) =>
        Data.Fingerprint != current || StaleOutputs(current).Count > 0;

    internal static void EnsureOutsideGame(string fullDir, string gameRoot)
    {
        var game = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot)) + Path.DirectorySeparatorChar;
        var candidate = fullDir + Path.DirectorySeparatorChar;
        if (candidate.StartsWith(game, StringComparison.OrdinalIgnoreCase))
            throw new PkException(PkErrorCode.WorkspaceInGameFolder,
                $"The workspace must not be inside the game folder ('{gameRoot}'). Mods and tools never write game files.",
                FixAction.PickWorkspaceFolder);
    }

    private static PkException Invalid(string message, Exception? inner = null) =>
        new(PkErrorCode.WorkspaceInvalid, message, FixAction.PickWorkspaceFolder, inner);
}
