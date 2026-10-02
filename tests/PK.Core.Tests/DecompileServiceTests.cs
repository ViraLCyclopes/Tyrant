using PK.Core.Decompile;
using PK.Core.Install;
using PK.Core.Jobs;
using PK.Core.Workspaces;

namespace PK.Core.Tests;

public class DecompileServiceTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "pk-tests", Guid.NewGuid().ToString("N"));

    private sealed class ListProgress : IProgress<JobProgress>
    {
        public List<JobProgress> Items { get; } = [];
        public void Report(JobProgress value) => Items.Add(value);
    }

    [Fact]
    public void DecompileAssembly_produces_csproj_and_source_files()
    {
        var asm = typeof(KeyValuesParser).Assembly.Location; // PK.Core.dll — synthetic, no game content
        var outDir = TempDir();

        var result = new DecompileService().DecompileAssembly(asm, [Path.GetDirectoryName(asm)!], outDir, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.NotEmpty(Directory.EnumerateFiles(outDir, "*.csproj"));
        Assert.Contains(Directory.EnumerateFiles(outDir, "*.cs", SearchOption.AllDirectories),
            f => Path.GetFileName(f) == "KeyValuesParser.cs");
    }

    [Fact]
    public void DecompileGame_continues_past_missing_assembly_and_stamps_source()
    {
        using var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(TempDir(), install);
        var progress = new ListProgress();

        var result = new DecompileService().DecompileGame(install, ws, ["Assembly-CSharp", "PKMissing"], progress, CancellationToken.None);

        Assert.False(result.AllSucceeded);
        var ok = Assert.Single(result.Assemblies, a => a.Success);
        Assert.Equal("Assembly-CSharp", ok.AssemblyName);
        Assert.Equal(Path.Combine(ws.SourceDir, "Assembly-CSharp"), ok.OutputDir);
        var failed = Assert.Single(result.Assemblies, a => !a.Success);
        Assert.Contains("not found", failed.Error);
        Assert.Contains("source", Workspace.Open(ws.Dir).Data.Outputs.Keys);
        Assert.Equal(1.0, progress.Items[^1].Fraction);
    }

    [Fact]
    public void Rerun_replaces_stale_output()
    {
        using var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(TempDir(), install);
        var stale = Path.Combine(ws.SourceDir, "Assembly-CSharp", "Stale.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        File.WriteAllText(stale, "// from an older game build");

        var result = new DecompileService().DecompileGame(install, ws, ["Assembly-CSharp"], null, CancellationToken.None);

        Assert.True(result.AllSucceeded);
        Assert.False(File.Exists(stale));
    }

    [Fact]
    public void Cancelled_token_stops_before_work()
    {
        using var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(TempDir(), install);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            new DecompileService().DecompileGame(install, ws, null, null, cts.Token));
    }

    [Fact]
    public void Default_set_is_the_game_code_assemblies()
    {
        Assert.Equal(
            new[] { "Assembly-CSharp", "Assembly-CSharp-firstpass", "PKEnums", "PKInterfaces", "PKMath", "PKUtility" },
            DecompileService.DefaultAssemblies);
    }
}
