using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Tests;

public class WorkspaceTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws");

    [Fact]
    public void Create_writes_pkws_json_and_subfolders_and_open_round_trips()
    {
        using var game = new FakeGame(buildGuid: "guid-1");
        var install = new GameInstall(game.Root, null);
        var dir = TempDir();

        Workspace.Create(dir, install);
        var ws = Workspace.Open(dir);

        Assert.True(File.Exists(Path.Combine(dir, Workspace.FileName)));
        Assert.True(Directory.Exists(ws.SourceDir));
        Assert.True(Directory.Exists(ws.LogsDir));
        Assert.Equal(game.Root, ws.Data.GameRoot);
        Assert.Equal("guid-1", ws.Data.Fingerprint!.BuildGuid);
        Assert.Equal(1, ws.Data.SchemaVersion);
    }

    [Fact]
    public void Create_inside_game_folder_is_rejected()
    {
        using var game = new FakeGame();
        var install = new GameInstall(game.Root, null);

        foreach (var dir in new[] { game.Root, Path.Combine(game.Root, "mods", "ws") })
        {
            var ex = Assert.Throws<TyrantException>(() => Workspace.Create(dir, install));
            Assert.Equal(TyrantErrorCode.WorkspaceInGameFolder, ex.Code);
            Assert.Equal(FixAction.PickWorkspaceFolder, ex.Fix);
        }
    }

    [Fact]
    public void Create_in_sibling_with_similar_name_is_allowed()
    {
        using var game = new FakeGame();
        var sibling = game.Root + " Modding"; // e.g. "...\Prehistoric Kingdom Modding"
        var ws = Workspace.Create(sibling, new GameInstall(game.Root, null));
        Assert.True(File.Exists(Path.Combine(ws.Dir, Workspace.FileName)));
    }

    [Fact]
    public void Create_over_existing_workspace_is_rejected()
    {
        using var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var dir = TempDir();
        Workspace.Create(dir, install);
        var ex = Assert.Throws<TyrantException>(() => Workspace.Create(dir, install));
        Assert.Equal(TyrantErrorCode.WorkspaceInvalid, ex.Code);
    }

    [Fact]
    public void Open_missing_or_corrupt_workspace_throws_workspace_invalid()
    {
        var missing = TempDir();
        Assert.Equal(TyrantErrorCode.WorkspaceInvalid, Assert.Throws<TyrantException>(() => Workspace.Open(missing)).Code);

        var corrupt = TempDir();
        Directory.CreateDirectory(corrupt);
        File.WriteAllText(Path.Combine(corrupt, Workspace.FileName), "{ not json");
        var ex = Assert.Throws<TyrantException>(() => Workspace.Open(corrupt));
        Assert.Equal(TyrantErrorCode.WorkspaceInvalid, ex.Code);
        Assert.Equal(FixAction.PickWorkspaceFolder, ex.Fix);
    }

    [Fact]
    public void Stamped_outputs_become_stale_when_fingerprint_changes()
    {
        using var game = new FakeGame();
        var dir = TempDir();
        var ws = Workspace.Create(dir, new GameInstall(game.Root, null));
        var oldBuild = new GameFingerprint("old", "aaa");
        var newBuild = new GameFingerprint("new", "bbb");

        ws.StampOutput("source", oldBuild);
        var reopened = Workspace.Open(dir);

        Assert.Empty(reopened.StaleOutputs(oldBuild));
        Assert.Equal(new[] { "source" }, reopened.StaleOutputs(newBuild));
        Assert.True(reopened.IsStale(newBuild));
    }

    [Fact]
    public void Restamping_with_current_build_clears_staleness()
    {
        using var game = new FakeGame(buildGuid: "old-build");
        var ws = Workspace.Create(TempDir(), new GameInstall(game.Root, null));
        var newBuild = new GameFingerprint("new-build", "zzz");

        ws.StampOutput("source/Assembly-CSharp", newBuild);

        Assert.False(ws.IsStale(newBuild));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"gameRoot\":\"\"}")]
    public void Open_rejects_workspace_without_game_root(string json)
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, Workspace.FileName), json);
        Assert.Equal(TyrantErrorCode.WorkspaceInvalid, Assert.Throws<TyrantException>(() => Workspace.Open(dir)).Code);
    }

    [Fact]
    public void Open_normalizes_null_collections()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, Workspace.FileName),
            """{"gameRoot":"C:\\Games\\PK","outputs":null,"installedFiles":null}""");

        var ws = Workspace.Open(dir);

        Assert.Empty(ws.Data.Outputs);
        Assert.Empty(ws.Data.InstalledFiles);
    }

    [Fact]
    public void Create_where_path_is_a_file_throws_workspace_invalid()
    {
        using var game = new FakeGame();
        var file = TempDir();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "not a folder");

        var ex = Assert.Throws<TyrantException>(() => Workspace.Create(file, new GameInstall(game.Root, null)));

        Assert.Equal(TyrantErrorCode.WorkspaceInvalid, ex.Code);
    }

    [Fact]
    public void A_workspace_from_before_the_rename_is_opened_and_its_file_renamed()
    {
        using var game = new FakeGame();
        var dir = TempDir();
        Workspace.Create(dir, new GameInstall(game.Root, null));
        File.Move(Path.Combine(dir, Workspace.FileName), Path.Combine(dir, Workspace.LegacyFileName));

        var ws = Workspace.Open(dir);

        Assert.Equal(game.Root, ws.Data.GameRoot);
        Assert.True(File.Exists(Path.Combine(dir, Workspace.FileName)));
        Assert.False(File.Exists(Path.Combine(dir, Workspace.LegacyFileName)));
    }

    private sealed class NoSteam : ISteamRootProvider
    {
        public string? GetSteamRoot() => null;
    }

    [Fact]
    public void Opener_repoints_a_workspace_at_a_moved_game()
    {
        using var oldGame = new FakeGame();
        using var newGame = new FakeGame(buildGuid: "moved");
        var dir = TempDir();
        Workspace.Create(dir, new GameInstall(oldGame.Root, null));

        var (ws, install, repointed) = WorkspaceOpener.Open(dir, newGame.Root, new GameInstallLocator(new NoSteam()));

        Assert.True(repointed);
        Assert.Equal(newGame.Root, install.RootDir);
        Assert.Equal(newGame.Root, ws.Data.GameRoot);
        Assert.Equal(newGame.Root, Workspace.Open(dir).Data.GameRoot);
    }

    [Fact]
    public void Opener_without_a_game_path_uses_the_recorded_game()
    {
        using var game = new FakeGame();
        var dir = TempDir();
        Workspace.Create(dir, new GameInstall(game.Root, null));

        var (_, install, repointed) = WorkspaceOpener.Open(dir, null, new GameInstallLocator(new NoSteam()));

        Assert.False(repointed);
        Assert.Equal(game.Root, install.RootDir);
    }

    [Fact]
    public void Opener_refuses_a_workspace_inside_the_game_folder()
    {
        using var game = new FakeGame();
        var dir = Path.Combine(game.Root, "my-workspace");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, Workspace.FileName), $$"""{"gameRoot":{{System.Text.Json.JsonSerializer.Serialize(game.Root)}}}""");

        var ex = Assert.Throws<TyrantException>(() => WorkspaceOpener.Open(dir, null, new GameInstallLocator(new NoSteam())));

        Assert.Equal(TyrantErrorCode.WorkspaceInGameFolder, ex.Code);
        Assert.Equal(FixAction.PickWorkspaceFolder, ex.Fix);
    }

    [Fact]
    public async Task Reading_outputs_while_a_job_stamps_them_never_throws()
    {
        using var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), install);
        var fingerprint = GameFingerprint.Compute(install);
        using var stop = new CancellationTokenSource();

        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 300 && !stop.IsCancellationRequested; i++) ws.StampOutput($"source/A{i}", fingerprint);
        });
        while (!writer.IsCompleted)
        {
            _ = ws.Outputs().Count; // what diagnostics and status read during a decompile
            _ = ws.StaleOutputs(fingerprint);
        }
        await writer;

        Assert.Equal(300, ws.Outputs().Count);
    }
}
