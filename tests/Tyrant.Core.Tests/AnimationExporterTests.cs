using Tyrant.Core.Animation;
using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Models;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Tests;

public class AnimationExporterTests
{
    private static (FakeGame Game, Workspace Ws, GameInstall Install, FakeAssetReader Reader, AssetIndex Index) Setup(PrefabModel? prefab = null)
    {
        var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), install);
        SkinDumps.Write(ws.DataDir);
        AnimationFixture.WriteTable(ws.DataDir);
        var reader = new FakeAssetReader
        {
            PrefabModelToReturn = prefab ?? ModelFixture.Prefab(ModelFixture.Triangle(name: "Carch_LOD00"), skinned: true),
            RawClipsToReturn = [AnimationFixture.WalkClip(), AnimationFixture.RoarClip()],
        };
        var index = new AssetIndex { Assets = [.. SkinDumps.Textures, SkinDumps.Prefab, AnimationFixture.Walk, AnimationFixture.Roar] };
        return (game, ws, install, reader, index);
    }

    private static SharpGLTF.Schema2.ModelRoot Read(string glb) => SharpGLTF.Schema2.ModelRoot.Load(glb);

    [Fact]
    public void Each_animation_is_its_own_file_by_default_with_its_keys_on_the_bones()
    {
        var (game, ws, install, reader, index) = Setup();
        using var _ = game;
        var dir = Path.Combine(ws.Dir, "out");

        var result = AnimationExporter.Export(install, ws, index, SpeciesSkinsReader.Load(ws), reader, "Carcharodontosaurus",
            ["Carch|Walk", "Carch|Roar"], dir, singleFile: false, null, CancellationToken.None);

        Assert.Equal(["Carcharodontosaurus-Walk.glb", "Carcharodontosaurus-Roar.glb"], result.Files.Select(Path.GetFileName));
        var walk = Read(result.Files[0]);
        var animation = Assert.Single(walk.LogicalAnimations);
        Assert.Equal("Walk", animation.Name);
        var channel = Assert.Single(animation.Channels, c => c.TargetNode.Name == "Hip" && c.TargetNodePath == SharpGLTF.Schema2.PropertyPath.translation);
        Assert.Equal(1f, channel.GetTranslationSampler().GetLinearKeys().Last().Key, 4);
        // Unity's z travel stays z in glTF (only x is mirrored).
        Assert.Equal(1f, channel.GetTranslationSampler().GetLinearKeys().Last().Value.Z, 4);
    }

    [Fact]
    public void All_in_one_file_holds_every_animation()
    {
        var (game, ws, install, reader, index) = Setup();
        using var _ = game;

        var result = AnimationExporter.Export(install, ws, index, SpeciesSkinsReader.Load(ws), reader, "Carcharodontosaurus",
            ["Carch|Walk", "Carch|Roar"], Path.Combine(ws.Dir, "out"), singleFile: true, null, CancellationToken.None);

        var file = Assert.Single(result.Files);
        Assert.Equal("Carcharodontosaurus-animations.glb", Path.GetFileName(file));
        Assert.Equal(["Walk", "Roar"], Read(file).LogicalAnimations.Select(a => a.Name));
    }

    [Fact]
    public void Only_the_first_level_of_detail_is_written()
    {
        var prefab = ModelFixture.Prefab(ModelFixture.Triangle(name: "Carch_LOD00"), skinned: true);
        var lod1 = prefab.Renderers[0] with { Name = "Carch_LOD01", Mesh = ModelFixture.Triangle(name: "Carch_LOD01") };
        prefab = prefab with { Renderers = [prefab.Renderers[0], lod1] };
        var (game, ws, install, reader, index) = Setup(prefab);
        using var _ = game;

        var result = AnimationExporter.Export(install, ws, index, SpeciesSkinsReader.Load(ws), reader, "Carcharodontosaurus",
            ["Carch|Walk"], Path.Combine(ws.Dir, "out"), singleFile: false, null, CancellationToken.None);

        var model = Read(Assert.Single(result.Files));
        Assert.Equal("Carch_LOD00", Assert.Single(model.LogicalMeshes).Name);
        Assert.Contains(model.LogicalAnimations[0].Channels, c => c.TargetNode.Name == "Hip");
    }

    [Fact]
    public void An_unknown_animation_is_a_note_and_the_others_still_export()
    {
        var (game, ws, install, reader, index) = Setup();
        using var _ = game;

        var result = AnimationExporter.Export(install, ws, index, SpeciesSkinsReader.Load(ws), reader, "Carcharodontosaurus",
            ["Carch|Walk", "Carch|Nope"], Path.Combine(ws.Dir, "out"), singleFile: false, null, CancellationToken.None);

        Assert.Single(result.Files);
        Assert.Contains(result.Notes, n => n.Contains("Carch|Nope"));
    }

    [Fact]
    public void Fbx_conversion_keeps_every_action_as_a_take()
    {
        Assert.Contains("bake_anim_use_all_actions=True", Tyrant.Core.Blender.BlenderModelConverter.Scripts.GlbToFbx);
        Assert.Contains("bake_anim=bool(bpy.data.actions)", Tyrant.Core.Blender.BlenderModelConverter.Scripts.GlbToFbx);
    }

    [Fact]
    public void The_model_and_its_textures_are_read_once_however_many_files_are_written()
    {
        var (game, ws, install, reader, index) = Setup();
        using var _ = game;

        var result = AnimationExporter.Export(install, ws, index, SpeciesSkinsReader.Load(ws), reader, "Carcharodontosaurus",
            ["Carch|Walk", "Carch|Roar"], Path.Combine(ws.Dir, "out"), singleFile: false, null, CancellationToken.None);

        Assert.Equal(2, result.Files.Count);
        Assert.Equal(1, reader.AnimatedModelReads);
    }
}
