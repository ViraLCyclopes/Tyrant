using Tyrant.Core.Models;
using Tyrant.Core.Tests;

namespace Tyrant.Rpc.Tests;

public class BlenderMethodsTests
{
    private static string Temp() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"))).FullName;

    private static (string ProgramFiles, string AppData, string Exe, FakeBlenderProcess Process) Blender52(bool addon)
    {
        var programFiles = Temp();
        var dir = Directory.CreateDirectory(Path.Combine(programFiles, "Blender Foundation", "Blender 5.2")).FullName;
        var exe = Path.Combine(dir, "blender.exe");
        File.WriteAllText(exe, "");
        var process = new FakeBlenderProcess();
        process.Versions[exe] = "Blender 5.2.2";
        var appData = Temp();
        if (addon)
        {
            var a = Directory.CreateDirectory(Path.Combine(appData, "Blender Foundation", "Blender", "5.2", "extensions", "user_default", "tyrant_blender")).FullName;
            File.WriteAllText(Path.Combine(a, "blender_manifest.toml"), "version = \"0.1.0\"");
        }
        return (programFiles, appData, exe, process);
    }

    private static async Task<(RpcHarness H, string Ws)> Setup(FakeGame game, Tyrant.Core.Blender.BlenderEnvironment blender)
    {
        var prefab = ModelFixture.Prefab(ModelFixture.Triangle(name: "Carch_LOD0"));
        prefab = prefab with { Renderers = [prefab.Renderers[0] with { Materials = [new MaterialModel("Carch", [])] }] };
        var h = new RpcHarness(TestStudio.Options(dumperDir: TestStudio.FakeGameModsDir(), reader: new FakeAssetReader { PrefabModelToReturn = prefab }, blender: blender));
        var ws = TestStudio.TempDir();
        await h.Call("workspace.create", new { dir = ws, gamePath = game.Root });
        SkinDumps.Write(Path.Combine(ws, "data"));
        AssetFixtures.WriteIndex(ws, [.. SkinDumps.Textures, SkinDumps.Prefab],
            Tyrant.Core.Install.GameFingerprint.Compute(new Tyrant.Core.Install.GameInstall(game.Root, null)));
        return (h, ws);
    }

    [Fact]
    public async Task Status_without_blender_says_so()
    {
        using var game = new FakeGame();
        var (h, _) = await Setup(game, TestStudio.NoBlender());

        var status = await h.Call("blender.status", new { });

        Assert.False(status.GetProperty("found").GetBoolean());
        Assert.Contains("not found", status.GetProperty("problem").GetString());
    }

    [Fact]
    public async Task Set_path_is_saved_in_the_workspace()
    {
        using var game = new FakeGame();
        var (_, appData, exe, process) = Blender52(addon: false);
        var (h, ws) = await Setup(game, TestStudio.NoBlender(process, appData: appData));

        var status = await h.Call("blender.setPath", new { path = exe });

        Assert.Equal(exe, status.GetProperty("exe").GetString());
        Assert.Equal(exe, Tyrant.Core.Workspaces.Workspace.Open(ws).Data.BlenderPath);
    }

    [Fact]
    public async Task Install_runs_blenders_extension_installer()
    {
        using var game = new FakeGame();
        var (programFiles, appData, _, process) = Blender52(addon: false);
        var zip = Path.Combine(Temp(), "tyrant_blender.zip");
        using (var a = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create)) a.CreateEntry("blender_manifest.toml");
        var (h, _) = await Setup(game, TestStudio.NoBlender(process, programFiles: programFiles, appData: appData) with { AddonZip = zip });

        await h.Call("blender.installAddon", new { });

        Assert.Contains(process.Runs, r => r.Args.Contains("install-file"));
    }

    [Fact]
    public async Task Open_asks_the_running_blender()
    {
        using var game = new FakeGame();
        var (programFiles, appData, _, process) = Blender52(addon: true);
        var link = new TestStudio.SilentLink(answers: true);
        var (h, _) = await Setup(game, TestStudio.NoBlender(process, link, programFiles, appData));

        var result = await h.Call("blender.open", new { species = "Carcharodontosaurus", fresh = false, lods = false });

        Assert.Equal("running", result.GetProperty("how").GetString());
        Assert.True(File.Exists(result.GetProperty("projectFile").GetString()));
        Assert.Single(link.Opened);
    }

    [Fact]
    public async Task Open_takes_a_prefab_from_the_assets_tab()
    {
        using var game = new FakeGame();
        var (programFiles, appData, _, process) = Blender52(addon: true);
        var (h, _) = await Setup(game, TestStudio.NoBlender(process, new TestStudio.SilentLink(answers: true), programFiles, appData));

        var result = await h.Call("blender.open", new { prefabRef = SkinDumps.Prefab.Ref });

        Assert.Equal("Carcharodontosaurus", Tyrant.Core.Blender.BlenderProjectFile.Read(result.GetProperty("projectFile").GetString()!).Source.Species);
    }

    [Fact]
    public async Task Open_can_start_as_female()
    {
        using var game = new FakeGame();
        var (programFiles, appData, _, process) = Blender52(addon: true);
        var (h, _) = await Setup(game, TestStudio.NoBlender(process, new TestStudio.SilentLink(answers: true), programFiles, appData));

        var result = await h.Call("blender.open", new { species = "Carcharodontosaurus", sex = "female" });

        Assert.Equal("female", Tyrant.Core.Blender.BlenderProjectFile.Read(result.GetProperty("projectFile").GetString()!).Sex);
    }

    [Fact]
    public async Task Open_can_skip_the_ik_controls()
    {
        using var game = new FakeGame();
        var (programFiles, appData, _, process) = Blender52(addon: true);
        var (h, _) = await Setup(game, TestStudio.NoBlender(process, new TestStudio.SilentLink(answers: true), programFiles, appData));

        var result = await h.Call("blender.open", new { species = "Carcharodontosaurus", ik = false });

        Assert.False(Tyrant.Core.Blender.BlenderProjectFile.Read(result.GetProperty("projectFile").GetString()!).IkOnOpen);
    }
}
