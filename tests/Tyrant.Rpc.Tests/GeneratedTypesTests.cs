using Tyrant.Rpc.TypeScript;

namespace Tyrant.Rpc.Tests;

public class GeneratedTypesTests
{
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Tyrant.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Tyrant.slnx not found above the test output folder.");
    }

    [Fact]
    public void Checked_in_typescript_matches_the_csharp_protocol()
    {
        var (server, _) = RpcHost.Build(TestStudio.Options(), _ => { }, TextWriter.Null);
        var expected = TsEmitter.Emit(server.Methods);
        var path = Path.Combine(RepoRoot(), "studio", "src", "lib", "rpc", "types.gen.ts");
        if (Environment.GetEnvironmentVariable("TYRANT_UPDATE_GENERATED") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, expected);
        }

        Assert.True(File.Exists(path), $"{path} is missing; run 'npm run gen:types' in studio/.");
        Assert.True(expected == File.ReadAllText(path).ReplaceLineEndings("\n"),
            "studio/src/lib/rpc/types.gen.ts is out of date; run 'npm run gen:types' in studio/ (or set TYRANT_UPDATE_GENERATED=1 and re-run this test).");
    }
}
