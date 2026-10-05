using Tyrant.Core.Blender;
using Tyrant.Core.Errors;

namespace Tyrant.Core.Tests;

public class ModelConverterTests
{
    private static string Temp() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"))).FullName;

    [Fact]
    public void Fbx_to_glb_runs_blender_in_the_background_with_the_script_and_both_paths()
    {
        var dir = Temp();
        var fbx = Path.Combine(dir, "my.fbx");
        File.WriteAllText(fbx, "fbx");
        var glb = Path.Combine(dir, "out.glb");
        var process = new FakeBlenderProcess { OnRun = (_, _) => { File.WriteAllText(glb, "glb"); return new BlenderRun(0, "TYRANT-CONVERTED"); } };

        new BlenderModelConverter(process, @"C:\b\blender.exe").FbxToGlb(fbx, glb);

        var (exe, args, _) = Assert.Single(process.Runs);
        Assert.Equal(@"C:\b\blender.exe", exe);
        Assert.Equal(["-b", "--factory-startup", "--python"], args.Take(3));
        Assert.Equal([Path.GetFullPath(fbx), Path.GetFullPath(glb)], args.SkipWhile(a => a != "--").Skip(1));
        Assert.False(File.Exists(args[3])); // the temporary script is gone
    }

    [Fact]
    public void A_failed_run_says_blenders_last_lines_and_cleans_up()
    {
        var dir = Temp();
        var fbx = Path.Combine(dir, "broken.fbx");
        File.WriteAllText(fbx, "nope");
        string? script = null;
        var process = new FakeBlenderProcess { OnRun = (_, args) => { script = args[3]; return new BlenderRun(1, "Traceback\nline\nRuntimeError: the FBX has no mesh"); } };

        var ex = Assert.Throws<TyrantException>(() => new BlenderModelConverter(process, "b.exe").FbxToGlb(fbx, Path.Combine(dir, "o.glb")));

        Assert.Equal(TyrantErrorCode.BlenderFailed, ex.Code);
        Assert.Contains("broken.fbx", ex.Message);
        Assert.Contains("the FBX has no mesh", ex.Message);
        Assert.NotNull(script);
        Assert.False(File.Exists(script));
    }

    [Fact]
    public void Glb_to_fbx_converts_a_batch_in_one_run_and_names_each_failure()
    {
        var dir = Temp();
        var a = (Glb: Path.Combine(dir, "a.glb"), Fbx: Path.Combine(dir, "a.fbx"));
        var b = (Glb: Path.Combine(dir, "b.glb"), Fbx: Path.Combine(dir, "b.fbx"));
        var process = new FakeBlenderProcess { OnRun = (_, _) => { File.WriteAllText(a.Fbx, "fbx"); return new BlenderRun(0, "TYRANT-FBX-OK 0\nTYRANT-FBX-FAIL 1 no armature"); } };

        var failures = new BlenderModelConverter(process, "b.exe").GlbToFbx([a, b]);

        Assert.Single(process.Runs);
        var failed = Assert.Single(failures);
        Assert.Equal(b.Glb, failed.Key);
        Assert.Equal("no armature", failed.Value);
    }
}
