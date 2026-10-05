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
    /// <summary>A quad as the game stores it: two triangles whose shared diagonal is split (different UVs = a seam), a shape key on one corner.</summary>
    internal static MeshData SplitQuad(Matrix4x4[] bindPoses) => new()
    {
        Name = "Quad_LOD0",
        Positions = [new(0, 0, 0), new(0, 1, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0), new(1, 0, 0)],
        Normals = [.. Enumerable.Repeat(new Vector3(0, 0, -1), 6)],
        Uv0 = [new(0, 0), new(0, 1), new(1, 0), new(1, 1), new(0.2f, 1), new(1, 0.2f)],
        Colors = [],
        Skin = [.. Enumerable.Repeat(new BoneWeight4(0, 0, 0, 0, 1, 0, 0, 0), 6)],
        Indices = [0, 1, 2, 3, 5, 4],
        SubMeshes = [new SubMesh(0, 6, 0)],
        BindPoses = bindPoses,
        BlendShapes = [new BlendShape("Infant", [3], [new Vector3(0, 0.5f, 0)], [Vector3.Zero])],
    };

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
        var reader = new FakeAssetReader { PrefabModelToReturn = prefab };
        var index = new AssetIndex { Assets = [.. SkinDumps.Textures, SkinDumps.Prefab] };
        var result = BlenderProjectWriter.Write(new BlenderOpenRequest("Carcharodontosaurus", "Alt 1", null, false, false), ws, install, index,
            SpeciesSkinsReader.Load(ws), reader, "tyrant.exe");
        return (result.ProjectFile, prefab, ws, install, game);
    }

    internal static BlenderRun RunPython(string script, params string[] args)
    {
        var blender = BlenderIntegrationTests.Blender()!;
        return new BlenderProcess().Run(blender.Exe,
            ["-b", "--factory-startup", "--python", Path.Combine(BlenderAddonTests.RepoRoot(), "tools", "blender", "tests", script), "--", .. args],
            BlenderIntegrationTests.ThrowawayUser(out _), TimeSpan.FromMinutes(5));
    }

    [SkippableFact]
    public void Python_tests_pass_in_blender()
    {
        Skip.If(BlenderIntegrationTests.Blender() is null, "Blender 5.x not found");
        var (projectFile, _, _, _, game) = Fixture();
        using var _ = game;

        var run = RunPython("run.py", projectFile);

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Matches(@"TYRANT-TESTS ran [1-9]", run.Output);
    }
}
