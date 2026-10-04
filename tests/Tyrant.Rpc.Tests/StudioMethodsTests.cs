using System.Text.Json;
using Tyrant.Core.Tests;
using Tyrant.Rpc.Studio;

namespace Tyrant.Rpc.Tests;

public class StudioMethodsTests
{
    private static async Task<(RpcHarness Harness, string Workspace)> Opened(FakeGame game, StudioOptions? options = null)
    {
        var harness = new RpcHarness(options ?? TestStudio.Options());
        var dir = TestStudio.TempDir();
        await harness.Call("workspace.create", new { dir, gamePath = game.Root });
        return (harness, dir);
    }

    [Fact]
    public async Task Session_log_lines_reach_the_app_as_log_notifications()
    {
        using var game = new FakeGame();
        var (harness, _) = await Opened(game);

        Assert.Contains(harness.Notifications, n =>
            n.GetProperty("method").GetString() == "log"
            && n.GetProperty("params").GetProperty("level").GetString() == "info"
            && n.GetProperty("params").GetProperty("message").GetString()!.StartsWith("Created the workspace for"));
    }

    [Fact]
    public async Task App_info_reports_the_version_and_protocol()
    {
        var info = await new RpcHarness(TestStudio.Options()).Call("app.info");

        Assert.Equal(StudioMethods.ProtocolVersion, info.GetProperty("protocolVersion").GetInt32());
        Assert.Equal("0.1.0", info.GetProperty("version").GetString());
    }

    [Fact]
    public async Task Detect_with_a_game_path_returns_its_build()
    {
        using var game = new FakeGame(buildGuid: "guid-rpc");

        var info = await new RpcHarness(TestStudio.Options()).Call("install.detect", new { gamePath = game.Root });

        Assert.Equal(game.Root, info.GetProperty("rootDir").GetString());
        Assert.Equal("guid-rpc", info.GetProperty("buildGuid").GetString());
    }

    [Fact]
    public async Task Detect_with_a_bad_path_asks_to_pick_the_game_folder()
    {
        var ex = await Assert.ThrowsAsync<RpcCallException>(() =>
            new RpcHarness(TestStudio.Options()).Call("install.detect", new { gamePath = Path.GetTempPath() }));

        Assert.Equal("GAME_NOT_FOUND", ex.DataCode);
        Assert.Equal("PICK_GAME_FOLDER", ex.Fix);
    }

    [Fact]
    public async Task Create_opens_the_workspace_and_reports_its_status()
    {
        using var game = new FakeGame(buildGuid: "guid-ws");
        var (harness, dir) = await Opened(game);

        var status = await harness.Call("workspace.status");

        Assert.Equal(Path.GetFullPath(dir), status.GetProperty("dir").GetString());
        Assert.Equal(game.Root, status.GetProperty("gameRoot").GetString());
        Assert.Equal("guid-ws", status.GetProperty("buildGuid").GetString());
        Assert.Equal("notInstalled", status.GetProperty("dumper").GetString());
        Assert.False(status.GetProperty("hasData").GetBoolean());
        Assert.False(status.GetProperty("stale").GetBoolean());
        Assert.Equal(0, status.GetProperty("outputs").GetArrayLength());
    }

    [Fact]
    public async Task Status_without_a_workspace_asks_to_pick_one()
    {
        var ex = await Assert.ThrowsAsync<RpcCallException>(() => new RpcHarness(TestStudio.Options()).Call("workspace.status"));

        Assert.Equal("WORKSPACE_INVALID", ex.DataCode);
        Assert.Equal("PICK_WORKSPACE_FOLDER", ex.Fix);
    }

    [Fact]
    public async Task Open_of_a_folder_that_is_not_a_workspace_asks_to_pick_one()
    {
        var ex = await Assert.ThrowsAsync<RpcCallException>(() =>
            new RpcHarness(TestStudio.Options()).Call("workspace.open", new { dir = TestStudio.TempDir() }));

        Assert.Equal("WORKSPACE_INVALID", ex.DataCode);
        Assert.Equal("PICK_WORKSPACE_FOLDER", ex.Fix);
    }

    [Fact]
    public async Task Open_inside_the_game_folder_is_refused()
    {
        using var game = new FakeGame();
        var dir = Path.Combine(game.Root, "ws");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, Core.Workspaces.Workspace.FileName), $$"""{"gameRoot":{{JsonSerializer.Serialize(game.Root)}}}""");

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => new RpcHarness(TestStudio.Options()).Call("workspace.open", new { dir }));

        Assert.Equal("WORKSPACE_IN_GAME_FOLDER", ex.DataCode);
    }

    [Fact]
    public async Task Install_run_and_uninstall_the_dumper()
    {
        using var game = new FakeGame();
        var zip = TestStudio.FakeMelonLoaderZip();
        var (harness, dir) = await Opened(game, TestStudio.Options(new FakeDumpLauncher(3), zip));

        var installed = await harness.RunJob("dump.install");
        Assert.True(installed.GetProperty("installedLoader").GetBoolean());
        Assert.Contains("Installed MelonLoader", installed.GetProperty("message").GetString());
        Assert.Equal("installed", (await harness.Call("workspace.status")).GetProperty("dumper").GetString());

        var dumped = await harness.RunJob("dump.run", new { timeoutSeconds = 60 });
        Assert.Equal(3, dumped.GetProperty("objects").GetInt32());
        var status = await harness.Call("workspace.status");
        Assert.True(status.GetProperty("hasData").GetBoolean());
        Assert.Contains(status.GetProperty("outputs").EnumerateArray(), o => o.GetProperty("name").GetString() == "data");

        var removed = await harness.Call("dump.uninstall");
        Assert.True(removed.GetProperty("removedLoader").GetBoolean());
        Assert.Contains("back to vanilla", removed.GetProperty("message").GetString());
        Assert.False(File.Exists(Path.Combine(game.Root, "version.dll")));
        Assert.Contains("Install dumper", File.ReadAllText(Path.Combine(dir, "logs", StudioSession.LogFileName)));
    }

    [Fact]
    public async Task Dump_without_the_dumper_offers_to_install_it()
    {
        using var game = new FakeGame();
        var (harness, _) = await Opened(game);

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => harness.RunJob("dump.run"));

        Assert.Equal("DUMPER_NOT_INSTALLED", ex.DataCode);
        Assert.Equal("INSTALL_DUMPER", ex.Fix);
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            throw new HttpRequestException("offline");
        }
    }

    [Fact]
    public async Task The_nexus_check_asks_tyrants_page_and_reports_being_offline()
    {
        using var game = new FakeGame();
        var network = new NoNetwork();
        var (harness, _) = await Opened(game, TestStudio.Options(http: () => new HttpClient(network)));

        var nexus = await harness.Call("app.checkNexus");

        Assert.Equal(1, network.Calls);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, nexus.GetProperty("version").ValueKind);
        Assert.Equal("https://www.nexusmods.com/prehistorickingdom/mods/24", nexus.GetProperty("url").GetString());
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, nexus.GetProperty("error").ValueKind);
    }

    [Fact]
    public async Task Status_reports_the_framework_versions()
    {
        using var game = new FakeGame();
        var (harness, _) = await Opened(game, TestStudio.Options(null, TestStudio.FakeMelonLoaderZip()));
        await harness.RunJob("dump.install");

        var status = await harness.Call("workspace.status");

        Assert.Equal(Tyrant.Framework.Core.FrameworkInfo.Version, status.GetProperty("frameworkBundled").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, status.GetProperty("frameworkInGame").ValueKind); // the fake DLL has no version
    }

    [Fact]
    public async Task A_second_job_or_an_uninstall_while_a_job_runs_is_refused()
    {
        using var game = new FakeGame();
        var launcher = new BlockingDumpLauncher(1);
        var zip = TestStudio.FakeMelonLoaderZip();
        var (harness, _) = await Opened(game, TestStudio.Options(launcher, zip));
        await harness.RunJob("dump.install");

        var jobId = await harness.StartJob("dump.run", new { timeoutSeconds = 60 });
        Assert.True(launcher.Entered.Wait(TimeSpan.FromSeconds(10)));
        var second = await Assert.ThrowsAsync<RpcCallException>(() => harness.Call("decompile.run", new { }));
        var uninstall = await Assert.ThrowsAsync<RpcCallException>(() => harness.Call("dump.uninstall"));
        launcher.Release.Set();
        await harness.WaitJob(jobId);

        Assert.Equal("JOB_RUNNING", second.DataCode);
        Assert.Contains("Data dump", second.Message);
        Assert.Equal("JOB_RUNNING", uninstall.DataCode);
    }

    [Fact]
    public async Task Reopening_the_same_workspace_while_a_job_runs_keeps_its_results()
    {
        using var game = new FakeGame();
        var launcher = new BlockingDumpLauncher(1);
        var zip = TestStudio.FakeMelonLoaderZip();
        var (harness, dir) = await Opened(game, TestStudio.Options(launcher, zip));
        await harness.RunJob("dump.install");

        var jobId = await harness.StartJob("dump.run", new { timeoutSeconds = 60 });
        Assert.True(launcher.Entered.Wait(TimeSpan.FromSeconds(10)));
        await harness.Call("workspace.open", new { dir, gamePath = game.Root }); // the app window reloaded mid-job
        launcher.Release.Set();
        await harness.WaitJob(jobId);

        var status = await harness.Call("workspace.status");
        Assert.Contains(status.GetProperty("outputs").EnumerateArray(), o => o.GetProperty("name").GetString() == "data");
    }

    [Fact]
    public async Task Refresh_all_runs_every_step_and_reports_each()
    {
        using var game = new FakeGame();
        var (harness, _) = await Opened(game);

        var result = await harness.RunJob("workspace.refreshAll", timeoutSeconds: 300);

        var steps = result.GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal(["Decompile", "Asset index", "Data dump"], steps.Select(s => s.GetProperty("name").GetString()!).ToArray());
        // FakeGame has only Assembly-CSharp and no Addressables catalog, and the dumper is not installed.
        Assert.Equal("failed", steps[0].GetProperty("status").GetString());
        Assert.Contains("Assembly-CSharp-firstpass", steps[0].GetProperty("message").GetString());
        Assert.Equal("failed", steps[1].GetProperty("status").GetString());
        Assert.Contains("No assets", steps[1].GetProperty("message").GetString());
        Assert.Equal("skipped", steps[2].GetProperty("status").GetString());
        var outputs = (await harness.Call("workspace.status")).GetProperty("outputs").EnumerateArray().Select(o => o.GetProperty("name").GetString()).ToList();
        Assert.Contains("source/Assembly-CSharp", outputs);
    }

    [Fact]
    public async Task Diagnostics_include_the_build_and_the_workspace()
    {
        using var game = new FakeGame(buildGuid: "guid-diag");
        var (harness, dir) = await Opened(game);

        var text = (await harness.Call("app.diagnostics")).GetProperty("text").GetString()!;

        Assert.Contains("guid-diag", text);
        Assert.Contains(Path.GetFullPath(dir), text);
        Assert.Contains("Dumper: NotInstalled", text);
    }

    [Fact]
    public async Task Diagnostics_include_the_last_dumps_errors()
    {
        using var game = new FakeGame();
        var (harness, dir) = await Opened(game);
        Directory.CreateDirectory(Path.Combine(dir, "data"));
        File.WriteAllText(Path.Combine(dir, "data", "manifest.json"),
            """{"schemaVersion":1,"requestId":"r","buildGuid":"b","dumperVersion":"0.1.0","createdUtc":"2026-10-03T10:00:00Z","counts":{"AnimalData":69},"languages":["en"],"errors":["AnimalData 'Broken': field x failed","I2 Localization had no loaded languages"]}""");

        var text = (await harness.Call("app.diagnostics")).GetProperty("text").GetString()!;

        Assert.Contains("Data dump", text);
        Assert.Contains("AnimalData 'Broken': field x failed", text);
        Assert.Contains("I2 Localization had no loaded languages", text);
    }

    [Fact]
    public async Task Diagnostics_work_without_a_workspace()
    {
        var text = (await new RpcHarness(TestStudio.Options()).Call("app.diagnostics")).GetProperty("text").GetString()!;

        Assert.Contains("Tyrant diagnostics", text);
        Assert.Contains("No workspace", text);
    }
}
