using System.Numerics;
using Tyrant.Core.Assets;
using Tyrant.Core.Blender;
using Tyrant.Core.Install;
using Tyrant.Core.Models;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Tests;

/// <summary>The add-on's Python tests, run by the real Blender 5.x against a project Tyrant wrote; skipped without Blender.</summary>
[Trait("Category", "Blender")]
public class BlenderAddonRunTests
{
    internal static MeshData SplitQuad(Matrix4x4[] bindPoses) => ModelFixture.SplitQuad(bindPoses);

    private static Matrix4x4 Local(SkeletonNode n) =>
        Matrix4x4.CreateScale(n.LocalScale) * Matrix4x4.CreateFromQuaternion(n.LocalRotation) * Matrix4x4.CreateTranslation(n.LocalPosition);

    /// <summary>The node's world matrix with the prefab root at the origin (as Tyrant's glTF writer places it).</summary>
    private static Matrix4x4 World(SkeletonNode n) => n.Parent is null ? Matrix4x4.Identity : Local(n) * World(n.Parent);

    internal static (string ProjectFile, PrefabModel Prefab, Workspace Ws, GameInstall Install, FakeGame Game) Fixture()
    {
        var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), install);
        SkinDumps.Write(ws.DataDir);
        var root = new SkeletonNode("Animal", Vector3.Zero, Quaternion.Identity, Vector3.One);
        var hip = new SkeletonNode("Hip", new Vector3(0, 1, 0), Quaternion.Identity, Vector3.One, root);
        root.Children.Add(hip);
        // A tilted bone: the round trip must keep its rest rotation exactly.
        var tail = new SkeletonNode("Tail", new Vector3(0, 0, -1), Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.4f), Vector3.One, hip);
        hip.Children.Add(tail);
        // Bind poses match the skeleton, as the game's do.
        Matrix4x4[] binds = [.. new[] { hip, tail }.Select(b => Matrix4x4.Invert(World(b), out var inv) ? inv : Matrix4x4.Identity)];
        var renderer = new RendererModel("Quad_LOD0", SplitQuad(binds), [hip, tail], root)
        {
            Materials = [new MaterialModel("Carch", [new TextureSlot("_AdultDiffuse", null, 1), new TextureSlot("_AdultPatternMask", null, 4)])],
        };
        var prefab = new PrefabModel("Animal", root, [renderer], []);
        var reader = new FakeAssetReader { PrefabModelToReturn = prefab, RealPngs = true };
        var index = new AssetIndex { Assets = [.. SkinDumps.Textures, SkinDumps.Prefab] };
        var result = BlenderProjectWriter.Write(new BlenderOpenRequest("Carcharodontosaurus", "Alt 1", null, false, false), ws, install, index,
            SpeciesSkinsReader.Load(ws), reader, "tyrant.exe");
        return (result.ProjectFile, prefab, ws, install, game);
    }

    /// <summary>A game object (a fence post, no armature) as a project in the same workspace, for the add-on's object tests.</summary>
    internal static string ObjectProject(Workspace ws, GameInstall install)
    {
        var fence = new AssetRecord("fences.bundle", 42, "GameObject", "Fence_Post", "Assets/Fence.prefab", "abcabcabcabcabcabcabcabcabcabcab", null);
        var picture = new AssetRecord("fences.bundle", 43, "Texture2D", "T_Fence_D", null, "cdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcd", null);
        var post = ModelFixture.Prefab(ModelFixture.Triangle(name: "FencePost", skinned: false, withShape: false), skinned: false);
        post = post with { Renderers = [post.Renderers[0] with { Name = "FencePost", Materials = [new MaterialModel("Adobe", [new TextureSlot("_DiffuseTex", null, 43)])] }] };
        var index = new AssetIndex { Assets = [.. SkinDumps.Textures, SkinDumps.Prefab, fence, picture] };
        return BlenderProjectWriter.Write(new BlenderOpenRequest("", null, null, false, false) { PrefabRef = fence.Ref }, ws, install, index,
            SpeciesSkinsReader.Load(ws), new FakeAssetReader { PrefabModelToReturn = post }, "tyrant.exe").ProjectFile;
    }

    /// <summary>The IK fixture (two legs and a neck with the game's chains, IkFixture) as a project in the same workspace.</summary>
    internal static string IkProject(Workspace ws, GameInstall install)
    {
        var index = new AssetIndex { Assets = [.. SkinDumps.Textures, SkinDumps.Prefab] };
        return BlenderProjectWriter.Write(new BlenderOpenRequest("Carcharodontosaurus", null, null, false, false), ws, install, index,
            SpeciesSkinsReader.Load(ws), new FakeAssetReader { PrefabModelToReturn = IkFixture.Prefab(), RealPngs = true }, "tyrant.exe").ProjectFile;
    }

    internal static BlenderRun RunPython(string script, params string[] args) => RunPythonWith(script, new Dictionary<string, string>(), args);

    internal static BlenderRun RunPythonWith(string script, IReadOnlyDictionary<string, string> extraEnv, params string[] args)
    {
        var blender = BlenderIntegrationTests.Blender()!;
        var env = BlenderIntegrationTests.ThrowawayUser(out _);
        foreach (var (key, value) in extraEnv) env[key] = value;
        return new BlenderProcess().Run(blender.Exe,
            ["-b", "--factory-startup", "--python", Path.Combine(BlenderAddonTests.RepoRoot(), "tools", "blender", "tests", script), "--", .. args],
            env, TimeSpan.FromMinutes(5));
    }

    [SkippableTheory]
    [InlineData("male")]
    [InlineData("female")]
    public void A_model_opened_and_sent_back_unchanged_builds_without_warnings(string sex)
    {
        Skip.If(BlenderIntegrationTests.Blender() is null, "Blender 5.x not found");
        var (projectFile, prefab, _, _, game) = Fixture();
        using var _ = game;
        var modDir = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(projectFile)!, "mod")).FullName;
        var glb = Path.Combine(modDir, "send.glb");

        var run = RunPython("roundtrip.py", projectFile, glb, sex);
        Assert.True(run.ExitCode == 0 && run.Output.Contains("ROUNDTRIP"), run.Output);

        var report = Tyrant.Core.ModelReplacements.ModelBuilder.Build(modDir, "send.glb", prefab);

        Assert.True(report.Errors.Count == 0, string.Join(" | ", report.Errors));
        Assert.DoesNotContain(report.Warnings, w => w.Contains("rest pose", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(report.Warnings, w => w.Contains("Shape key", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Warnings, w => w.Contains("neutral_bone", StringComparison.Ordinal));
        Assert.Single(report.Lods);
        // The Basis went, not the female's shape mix: the growth key moves the mesh as much as the game's.
        Assert.DoesNotContain(report.Warnings, w => w.Contains("misshapen", StringComparison.Ordinal));
    }

    /// <summary>
    /// Opt-in (TYRANT_BLENDER_GUI=1: it opens a Blender window): Tyrant's real start of a fresh Blender (detached, through the
    /// shell, the multi-line start script) opens the project and saves its .blend. Only the Blender it started is closed after.
    /// </summary>
    [SkippableFact]
    public void A_blender_tyrant_starts_opens_the_project_in_a_window()
    {
        var blender = BlenderIntegrationTests.Blender();
        Skip.If(blender is null || Environment.GetEnvironmentVariable("TYRANT_BLENDER_GUI") != "1", "set TYRANT_BLENDER_GUI=1 to run (opens a Blender window)");
        var (projectFile, _, _, _, game) = Fixture();
        using var _ = game;
        var env = BlenderIntegrationTests.ThrowawayUser(out var _root);
        BlenderAddon.Install(new BlenderProcess(), blender!, BlenderIntegrationTests.AddonZip(), env);
        foreach (var (key, value) in env) Environment.SetEnvironmentVariable(key, value); // the started Blender inherits them
        var before = System.Diagnostics.Process.GetProcessesByName("blender").Select(p => p.Id).ToHashSet();
        try
        {
            // A second script writes the scenes Blender has once the open had time to run (the add-on itself saves nothing).
            var scenes = Path.Combine(Path.GetDirectoryName(projectFile)!, "scenes.txt");
            var probe = string.Join("\n",
                "import bpy",
                "def _probe():",
                $"    open(r'{scenes}', 'w', encoding='utf-8').write('\\n'.join(f\"{{s.name}}|{{s.get('tyrant_project', '')}}\" for s in bpy.data.scenes))",
                "bpy.app.timers.register(_probe, first_interval=8.0)");
            new BlenderProcess().Start(blender!.Exe, ["--python-expr", BlenderService.StartScript(projectFile), "--python-expr", probe]);
            var deadline = DateTime.UtcNow.AddMinutes(2);
            while (DateTime.UtcNow < deadline && !File.Exists(scenes)) Thread.Sleep(500);
            Thread.Sleep(300);
            var lines = File.ReadAllLines(scenes);
            Assert.Contains(lines, l => l.StartsWith("Tyrant · game · Carcharodontosaurus", StringComparison.Ordinal) && l.EndsWith("|" + projectFile, StringComparison.Ordinal));
        }
        finally
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("blender").Where(p => !before.Contains(p.Id))) p.Kill();
            foreach (var key in env.Keys) Environment.SetEnvironmentVariable(key, null);
        }
    }

    [SkippableFact]
    public void An_ik_rig_sent_back_builds_without_warnings()
    {
        Skip.If(BlenderIntegrationTests.Blender() is null, "Blender 5.x not found");
        var (_, _, ws, install, game) = Fixture();
        using var _ = game;
        var projectFile = IkProject(ws, install);
        var modDir = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(projectFile)!, "mod")).FullName;
        var glb = Path.Combine(modDir, "send.glb");

        var run = RunPython("ikroundtrip.py", projectFile, glb);
        Assert.True(run.ExitCode == 0 && run.Output.Contains("IKROUNDTRIP"), run.Output);

        var report = Tyrant.Core.ModelReplacements.ModelBuilder.Build(modDir, "send.glb", IkFixture.Prefab());
        Assert.True(report.Errors.Count == 0, string.Join(" | ", report.Errors));
        Assert.DoesNotContain(report.Warnings, w => w.Contains("rest pose", StringComparison.OrdinalIgnoreCase));
        var nodes = SharpGLTF.Schema2.ModelRoot.Load(glb).LogicalNodes.Select(n => n.Name ?? "").ToList();
        Assert.DoesNotContain(nodes, n => n.StartsWith("ctrl_", StringComparison.Ordinal) || n.StartsWith("mch_", StringComparison.Ordinal));
    }

    [SkippableFact]
    public void Python_tests_pass_in_blender()
    {
        Skip.If(BlenderIntegrationTests.Blender() is null, "Blender 5.x not found");
        var (projectFile, _, ws, install, game) = Fixture();
        using var _ = game;

        // TYRANT_PY_PATTERN=test_images.py runs one test file (run.py's second argument); unset runs them all.
        var pattern = Environment.GetEnvironmentVariable("TYRANT_PY_PATTERN");
        var env = new Dictionary<string, string>
        {
            ["TYRANT_TEST_OBJECT_PROJECT"] = ObjectProject(ws, install),
            ["TYRANT_TEST_IK_PROJECT"] = IkProject(ws, install),
        };
        var run = RunPythonWith("run.py", env, string.IsNullOrEmpty(pattern) ? [projectFile] : [projectFile, pattern]);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "tyrant-python-tests.log"), run.Output); // every test's line, pass or fail

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Matches(@"TYRANT-TESTS ran [1-9]", run.Output);
    }

    [SkippableFact]
    public void A_texture_painted_in_blender_reaches_the_skins_maps()
    {
        Skip.If(BlenderIntegrationTests.Blender() is null, "Blender 5.x not found");
        var (projectFile, prefab, ws, install, game) = Fixture();
        using var _ = game;
        var glb = Path.Combine(Path.GetDirectoryName(projectFile)!, "send.glb");

        var run = RunPython("paintsend.py", projectFile, glb);
        Assert.True(run.ExitCode == 0, run.Output);
        var line = run.Output.Split('\n').Single(l => l.StartsWith("PAINTSEND ", StringComparison.Ordinal))["PAINTSEND ".Length..];
        var images = System.Text.Json.JsonSerializer.Deserialize<string[][]>(line)!.Select(p => new BlenderImage(p[0], p[1])).ToList();
        Assert.Equal(["diffuse"], images.Select(i => i.Slot));

        var reader = new FakeAssetReader { PrefabModelToReturn = prefab, RealPngs = true };
        var index = new AssetIndex { Assets = [.. SkinDumps.Textures, SkinDumps.Prefab] };
        var species = SpeciesSkinsReader.Load(ws);
        var mod = ModProject.Create(ws, "painted", "Painted", null);
        var skin = mod.AddSkin(ws, install, index, reader, species, "Carcharodontosaurus", "Red", "1", new SkinTemplateOptions(true, false, false));

        var sent = BlenderService.Send(ws, install, index, species, reader, projectFile, glb,
            new BlenderDestination("painted", "Carcharodontosaurus", skin.Id), null, images, "male");

        Assert.True(sent.Ok, string.Join(" | ", sent.Errors));
        Assert.Equal(["diffuse"], sent.Images);
        Assert.True(ModProject.Open(ws, "painted").Skin(skin.Id).Male!.ContainsKey("diffuse"));
    }

    [SkippableFact]
    public void A_model_survives_glb_to_fbx_and_back_and_still_builds()
    {
        var blender = BlenderIntegrationTests.Blender();
        Skip.If(blender is null, "Blender 5.x not found");
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"))).FullName;
        var prefab = ModelFixture.Prefab(ModelFixture.Triangle(name: "Carch_LOD00"), skinned: true);
        prefab = prefab with { Renderers = [prefab.Renderers[0] with { Materials = [new MaterialModel("Carch", [])] }] };
        var glb = Path.Combine(dir, "carch.glb");
        GltfModelWriter.WriteGlb(prefab, prefab.Renderers[0], glb, [new GltfMaterial("Carch")]);
        var converter = new BlenderModelConverter(new BlenderProcess(), blender!.Exe, BlenderIntegrationTests.ThrowawayUser(out _));

        Assert.Empty(converter.GlbToFbx([(glb, Path.Combine(dir, "carch.fbx"))]));
        converter.FbxToGlb(Path.Combine(dir, "carch.fbx"), Path.Combine(dir, "back.glb"));

        var report = Tyrant.Core.ModelReplacements.ModelBuilder.Build(dir, "back.glb", prefab);
        Assert.True(report.Errors.Count == 0, string.Join(" | ", report.Errors)); // bones, growth keys and material survived
    }
}
