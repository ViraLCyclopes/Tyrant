using System.Text.Json;
using PK.Core.Dumping;
using PK.Core.Errors;
using PK.Core.Install;
using PK.Core.Workspaces;
using PK.Dumper.Serialization;

namespace PK.Core.Tests;

public class DumpRunnerTests
{
    // Windows wildcards match "data" itself for "data.*", so filter by name explicitly.
    private static IEnumerable<string> LeftoverDumpFolders(Workspace ws) =>
        Directory.GetDirectories(ws.Dir).Select(Path.GetFileName).OfType<string>().Where(n => n.StartsWith("data.", StringComparison.OrdinalIgnoreCase));

    private static string TempDir() => Path.Combine(Path.GetTempPath(), "pk-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Marks the fake game as having our mod installed.</summary>
    private static GameInstall Installed(FakeGame game)
    {
        var install = new GameInstall(game.Root, null);
        Directory.CreateDirectory(ModLoaderInstaller.RecordDir(install));
        File.WriteAllText(Path.Combine(ModLoaderInstaller.RecordDir(install), "install.json"), """{"installedLoader":true,"files":[],"createdDirs":[]}""");
        return install;
    }

    [Fact]
    public void Request_and_log_live_in_melonloader_folders()
    {
        using var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        Assert.Equal(Path.Combine(game.Root, "UserData", "pk.dumper.request.json"), DumpRunner.RequestPath(install));
    }

    /// <summary>Simulates the plugin: reads the request and writes a dump with the given number of objects.</summary>
    private sealed class FakePluginLauncher(int objects, Action<string>? beforeWrite = null) : IGameLauncher
    {
        public int Launches { get; private set; }

        public bool IsRunning(GameInstall install) => false;

        public void Launch(GameInstall install)
        {
            Launches++;
            var request = JsonDocument.Parse(File.ReadAllText(DumpRunner.RequestPath(install))).RootElement;
            var output = request.GetProperty("outputDir").GetString()!;
            beforeWrite?.Invoke(output);
            var result = new DumpResult();
            for (var i = 0; i < objects; i++)
                result.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo("PrehistoricKingdom.AnimalData", $"Animal{i}", i), "{}"));
            DumpWriter.Write(output, result, [], new DumpManifest { RequestId = request.GetProperty("requestId").GetString()!, BuildGuid = "b" });
        }
    }

    private sealed class SilentLauncher : IGameLauncher
    {
        public bool IsRunning(GameInstall install) => false;
        public void Launch(GameInstall install) { }
    }

    private sealed class AlreadyRunningLauncher : IGameLauncher
    {
        public bool Launched { get; private set; }
        public bool IsRunning(GameInstall install) => true;
        public void Launch(GameInstall install) => Launched = true;
    }

    [Fact]
    public void Game_already_running_is_refused_before_writing_a_request()
    {
        using var game = new FakeGame();
        var install = Installed(game);
        var ws = Workspace.Create(TempDir(), install);
        var launcher = new AlreadyRunningLauncher();

        var ex = Assert.Throws<PkException>(() => new DumpRunner(launcher).Run(install, ws, TimeSpan.FromSeconds(1), null, CancellationToken.None));

        Assert.Equal(PkErrorCode.DumpFailed, ex.Code);
        Assert.Contains("already running", ex.Message);
        Assert.False(launcher.Launched);
        Assert.False(File.Exists(DumpRunner.RequestPath(install)));
    }

    [Fact]
    public void Successful_run_swaps_in_the_new_data_and_cleans_up()
    {
        using var game = new FakeGame();
        var install = Installed(game);
        var ws = Workspace.Create(TempDir(), install);
        File.WriteAllText(Path.Combine(ws.DataDir, "old.txt"), "previous dump");

        var manifest = new DumpRunner(new FakePluginLauncher(3)) { PollInterval = TimeSpan.FromMilliseconds(10) }
            .Run(install, ws, TimeSpan.FromSeconds(5), null, CancellationToken.None);

        Assert.Equal(3, manifest.Counts["PrehistoricKingdom.AnimalData"]);
        Assert.True(File.Exists(Path.Combine(ws.DataDir, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(ws.DataDir, "objects", "PrehistoricKingdom.AnimalData", "Animal2.json")));
        Assert.False(File.Exists(Path.Combine(ws.DataDir, "old.txt")));
        Assert.False(File.Exists(DumpRunner.RequestPath(install)));
        Assert.Empty(LeftoverDumpFolders(ws));
        Assert.Contains(DumpRunner.OutputName, Workspace.Open(ws.Dir).Data.Outputs.Keys);
    }

    [Fact]
    public void Timeout_keeps_previous_data_and_reports_the_log_tail()
    {
        using var game = new FakeGame();
        var install = Installed(game);
        var ws = Workspace.Create(TempDir(), install);
        File.WriteAllText(Path.Combine(ws.DataDir, "old.txt"), "previous dump");
        Directory.CreateDirectory(Path.Combine(game.Root, "MelonLoader"));
        File.WriteAllText(ModLoaderInstaller.LogPath(install), "line one\n[Error] something broke\n");

        var ex = Assert.Throws<PkException>(() => new DumpRunner(new SilentLauncher()) { PollInterval = TimeSpan.FromMilliseconds(10) }
            .Run(install, ws, TimeSpan.FromMilliseconds(200), null, CancellationToken.None));

        Assert.Equal(PkErrorCode.DumpTimeout, ex.Code);
        Assert.Contains("something broke", ex.Message);
        Assert.True(File.Exists(Path.Combine(ws.DataDir, "old.txt")));
        Assert.False(File.Exists(DumpRunner.RequestPath(install)));
        Assert.Empty(LeftoverDumpFolders(ws));
    }

    [Fact]
    public void Dump_without_objects_is_a_failure()
    {
        using var game = new FakeGame();
        var install = Installed(game);
        var ws = Workspace.Create(TempDir(), install);

        var ex = Assert.Throws<PkException>(() => new DumpRunner(new FakePluginLauncher(0)) { PollInterval = TimeSpan.FromMilliseconds(10) }
            .Run(install, ws, TimeSpan.FromSeconds(5), null, CancellationToken.None));

        Assert.Equal(PkErrorCode.DumpFailed, ex.Code);
    }

    [Fact]
    public void Manifest_from_another_request_is_ignored()
    {
        using var game = new FakeGame();
        var install = Installed(game);
        var ws = Workspace.Create(TempDir(), install);

        var ex = Assert.Throws<PkException>(() => new DumpRunner(new StaleOnlyLauncher()) { PollInterval = TimeSpan.FromMilliseconds(10) }
            .Run(install, ws, TimeSpan.FromMilliseconds(200), null, CancellationToken.None));

        Assert.Equal(PkErrorCode.DumpTimeout, ex.Code);
    }

    private sealed class StaleOnlyLauncher : IGameLauncher
    {
        public bool IsRunning(GameInstall install) => false;

        public void Launch(GameInstall install)
        {
            var output = JsonDocument.Parse(File.ReadAllText(DumpRunner.RequestPath(install))).RootElement.GetProperty("outputDir").GetString()!;
            DumpWriter.Write(output, new DumpResult(), [], new DumpManifest { RequestId = "someone-else" });
        }
    }

    [Fact]
    public void Run_without_the_plugin_reports_not_installed()
    {
        using var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(TempDir(), install);

        var ex = Assert.Throws<PkException>(() => new DumpRunner(new SilentLauncher()).Run(install, ws, TimeSpan.FromSeconds(1), null, CancellationToken.None));

        Assert.Equal(PkErrorCode.DumperNotInstalled, ex.Code);
        Assert.Contains("pk dump install", ex.Message);
    }
}
