using PK.Core.Errors;
using PK.Core.Install;
using PK.Core.Workspaces;

namespace PK.Core.Tests;

public class WorkspaceTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "pk-tests", Guid.NewGuid().ToString("N"), "ws");

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
            var ex = Assert.Throws<PkException>(() => Workspace.Create(dir, install));
            Assert.Equal(PkErrorCode.WorkspaceInGameFolder, ex.Code);
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
        var ex = Assert.Throws<PkException>(() => Workspace.Create(dir, install));
        Assert.Equal(PkErrorCode.WorkspaceInvalid, ex.Code);
    }

    [Fact]
    public void Open_missing_or_corrupt_workspace_throws_workspace_invalid()
    {
        var missing = TempDir();
        Assert.Equal(PkErrorCode.WorkspaceInvalid, Assert.Throws<PkException>(() => Workspace.Open(missing)).Code);

        var corrupt = TempDir();
        Directory.CreateDirectory(corrupt);
        File.WriteAllText(Path.Combine(corrupt, Workspace.FileName), "{ not json");
        var ex = Assert.Throws<PkException>(() => Workspace.Open(corrupt));
        Assert.Equal(PkErrorCode.WorkspaceInvalid, ex.Code);
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
        Assert.Equal(PkErrorCode.WorkspaceInvalid, Assert.Throws<PkException>(() => Workspace.Open(dir)).Code);
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

        var ex = Assert.Throws<PkException>(() => Workspace.Create(file, new GameInstall(game.Root, null)));

        Assert.Equal(PkErrorCode.WorkspaceInvalid, ex.Code);
    }
}
