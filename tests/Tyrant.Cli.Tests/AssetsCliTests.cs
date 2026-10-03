using Tyrant.Cli;
using Tyrant.Core.Tests;

namespace Tyrant.Cli.Tests;

public class AssetsCliTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws");

    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var (oldOut, oldErr) = (Console.Out, Console.Error);
        var (o, e) = (new StringWriter(), new StringWriter());
        Console.SetOut(o);
        Console.SetError(e);
        try { return (CliApp.Run(args), o.ToString(), e.ToString()); }
        finally { Console.SetOut(oldOut); Console.SetError(oldErr); }
    }

    private static string InitWorkspace(FakeGame game)
    {
        var dir = TempDir();
        Assert.Equal(ExitCodes.Ok, Run("workspace", "init", dir, "--game", game.Root).Code);
        return dir;
    }

    [Fact]
    public void Index_on_install_without_bundles_succeeds()
    {
        using var game = new FakeGame();
        var dir = InitWorkspace(game);

        var (code, output, _) = Run("assets", "index", "-w", dir);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("Indexed 0 assets", output);
        Assert.True(File.Exists(Path.Combine(dir, "cache", "asset-index.json")));
    }

    [Fact]
    public void Index_with_unreadable_bundle_is_partial_and_names_it()
    {
        using var game = new FakeGame();
        var platform = Path.Combine(game.Root, "Prehistoric Kingdom_Data", "StreamingAssets", "aa", "StandaloneWindows64");
        Directory.CreateDirectory(platform);
        File.WriteAllBytes(Path.Combine(platform, "broken.bundle"), [9, 9, 9]);
        var dir = InitWorkspace(game);

        var (code, output, _) = Run("assets", "index", "-w", dir);

        Assert.Equal(ExitCodes.Partial, code);
        Assert.Contains("FAIL  StandaloneWindows64/broken.bundle", output);
    }

    [Fact]
    public void List_before_index_reports_index_missing()
    {
        using var game = new FakeGame();
        var dir = InitWorkspace(game);

        var (code, _, err) = Run("assets", "list", "-w", dir);

        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("ASSET_INDEX_MISSING", err);
    }

    [Fact]
    public void Dump_unknown_key_reports_not_found()
    {
        using var game = new FakeGame();
        var dir = InitWorkspace(game);
        Run("assets", "index", "-w", dir);

        var (code, _, err) = Run("assets", "dump", "nope", "-w", dir);

        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("ASSET_NOT_FOUND", err);
    }

    [Fact]
    public void Dump_refuses_output_inside_game_folder()
    {
        using var game = new FakeGame();
        var dir = InitWorkspace(game);
        Run("assets", "index", "-w", dir);

        var (code, _, err) = Run("assets", "dump", "nope", "-w", dir, "--out", Path.Combine(game.Root, "dump.json"));

        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("OUTPUT_IN_GAME_FOLDER", err);
        Assert.False(File.Exists(Path.Combine(game.Root, "dump.json")));
    }

    [Fact]
    public void Export_textures_with_no_matches_reports_not_found()
    {
        using var game = new FakeGame();
        var dir = InitWorkspace(game);
        Run("assets", "index", "-w", dir);

        var (code, _, err) = Run("assets", "export-textures", "-w", dir, "--filter", "stego");

        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("ASSET_NOT_FOUND", err);
    }

    [Fact]
    public void Workspace_copied_into_game_folder_is_refused()
    {
        using var game = new FakeGame();
        var dir = InitWorkspace(game);
        var inside = Path.Combine(game.Root, "ws");
        Directory.CreateDirectory(inside);
        File.Copy(Path.Combine(dir, "tyrant-workspace.json"), Path.Combine(inside, "tyrant-workspace.json"));

        var (code, _, err) = Run("assets", "index", "-w", inside);

        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("WORKSPACE_IN_GAME_FOLDER", err);
        Assert.False(Directory.Exists(Path.Combine(inside, "cache")));
    }

    [Fact]
    public void Index_with_corrupt_catalog_warns_and_is_partial()
    {
        using var game = new FakeGame();
        var aa = Path.Combine(game.Root, "Prehistoric Kingdom_Data", "StreamingAssets", "aa");
        Directory.CreateDirectory(aa);
        File.WriteAllText(Path.Combine(aa, "catalog.json"), "{ nope");
        var dir = InitWorkspace(game);

        var (code, output, _) = Run("assets", "index", "-w", dir);

        Assert.Equal(ExitCodes.Partial, code);
        Assert.Contains("WARNING", output);
        Assert.True(File.Exists(Path.Combine(dir, "cache", "asset-index.json")));
    }

    [Fact]
    public void Status_says_when_bundles_were_downloaded_since_the_index()
    {
        using var game = new FakeGame();
        var dir = InitWorkspace(game);
        var install = new Tyrant.Core.Install.GameInstall(game.Root, null);
        new Tyrant.Core.Assets.AssetIndex { MissingBundles = ["StandaloneWindows64/dlc.bundle"], Fingerprint = Tyrant.Core.Install.GameFingerprint.Compute(install) }
            .Save(Path.Combine(dir, "cache", Tyrant.Core.Assets.AssetIndex.FileName));
        var aa = Tyrant.Core.Assets.AssetSession.AaDirOf(install);
        Directory.CreateDirectory(Path.Combine(aa, "StandaloneWindows64"));
        File.WriteAllText(Path.Combine(aa, "StandaloneWindows64", "dlc.bundle"), "x");

        var (_, output, _) = Run("workspace", "status", "-w", dir);

        Assert.Contains("downloaded since", output);
    }
}
