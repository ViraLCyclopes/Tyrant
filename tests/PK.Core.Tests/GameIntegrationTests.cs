using PK.Core.Decompile;
using PK.Core.Install;
using PK.Core.Workspaces;

namespace PK.Core.Tests;

/// <summary>Runs against a real install. Set PK_GAME_DIR to the game folder to enable.</summary>
[Trait("Category", "Integration")]
public class GameIntegrationTests
{
    private static readonly string? GameDir = Environment.GetEnvironmentVariable("PK_GAME_DIR");

    private sealed class NoSteam : ISteamRootProvider
    {
        public string? GetSteamRoot() => null;
    }

    [SkippableFact]
    public void Real_install_is_valid_and_fingerprinted()
    {
        Skip.If(GameDir is null, "PK_GAME_DIR not set");
        var install = new GameInstallLocator(new NoSteam()).FromPath(GameDir!);
        var fp = GameFingerprint.Compute(install);

        Assert.Matches("^[0-9a-f]{32}$", fp.BuildGuid);
        Assert.Matches("^[0-9a-f]{64}$", fp.AssemblySha256);
        Assert.NotNull(install.SteamAppId);
    }

    [SkippableFact]
    public void Real_install_decompiles_game_code_with_animal_database()
    {
        Skip.If(GameDir is null, "PK_GAME_DIR not set");
        var install = new GameInstallLocator(new NoSteam()).FromPath(GameDir!);
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "pk-tests", Guid.NewGuid().ToString("N")), install);

        var result = new DecompileService().DecompileGame(install, ws, ["Assembly-CSharp", "PKEnums"], null, CancellationToken.None);

        Assert.True(result.AllSucceeded, string.Join("; ", result.Assemblies.Select(a => a.Error)));
        Assert.Contains(Directory.EnumerateFiles(Path.Combine(ws.SourceDir, "Assembly-CSharp"), "*.cs", SearchOption.AllDirectories),
            f => Path.GetFileName(f) == "AnimalDatabase.cs");
    }
}
