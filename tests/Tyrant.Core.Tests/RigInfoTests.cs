using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Models;
using Tyrant.Core.Rigging;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Tests;

public class RigInfoTests
{
    private static readonly AssetRecord Walk = new("@data/sharedassets0.assets", 501, "AnimationClip", "Carch|Walk", null, null, null);
    private static readonly AssetRecord Other = new("@data/sharedassets0.assets", 502, "AnimationClip", "Rex|Walk", null, null, null);

    private static (FakeGame Game, Workspace Ws, GameInstall Install, FakeAssetReader Reader) Setup()
    {
        var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), install);
        SkinDumps.Write(ws.DataDir);
        var animal = Path.Combine(ws.DataDir, "objects", "PrehistoricKingdom.AnimalData", "Carcharodontosaurus.json");
        File.WriteAllText(animal, File.ReadAllText(animal).TrimEnd().TrimEnd('}')
            + ""","animationTable":{"$ref":{"type":"PrehistoricKingdom.AnimationV2.AnimationTableV2","name":"CarchTable","id":7}}}""");
        var tables = Directory.CreateDirectory(Path.Combine(ws.DataDir, "objects", "PrehistoricKingdom.AnimationV2.AnimationTableV2")).FullName;
        File.WriteAllText(Path.Combine(tables, "CarchTable.json"),
            """{"$type":"PrehistoricKingdom.AnimationV2.AnimationTableV2","$name":"CarchTable","$id":7,"a":{"_Clip":{"$ref":{"type":"UnityEngine.AnimationClip","name":"Carch|Walk","id":1}}}}""");
        var reader = new FakeAssetReader
        {
            PrefabModelToReturn = ModelFixture.Prefab(ModelFixture.Triangle(name: "Carch_LOD00"), skinned: true),
            ClipsToReturn = [new ClipChannels("Carch|Walk", [new ClipBinding(ClipReader.Crc32("Hip/Tail"), 1, 0.3f)])],
        };
        return (game, ws, install, reader);
    }

    private static AssetIndex Index() => new() { Assets = [.. SkinDumps.Textures, SkinDumps.Prefab, Walk, Other] };

    [Fact]
    public void Lists_bones_the_clips_move_and_the_growth_positions_or_scales()
    {
        var (game, ws, install, reader) = Setup();
        using var _ = game;

        var info = RigInfoService.For(ws, install, Index(), SpeciesSkinsReader.Load(ws), reader, "Carcharodontosaurus");

        Assert.Equal(["Animal", "Hip", "Tail"], info.Bones);
        Assert.Equal(["Tail"], info.ClipMoved);
        Assert.Equal(["Hip"], info.GrowthMoved);  // mode All: position and scale
        Assert.Equal(["Hip"], info.GrowthScaled); // Arm.L grows too but is not in this skeleton
        Assert.Equal([Walk], reader.LastClipsAsked); // only the species' own clips are read
        Assert.Empty(info.Failures);
    }

    [Fact]
    public void A_second_look_comes_from_the_cache()
    {
        var (game, ws, install, reader) = Setup();
        using var _ = game;
        RigInfoService.For(ws, install, Index(), SpeciesSkinsReader.Load(ws), reader, "Carcharodontosaurus");
        reader.ClipsToReturn = [];

        var again = RigInfoService.For(ws, install, Index(), SpeciesSkinsReader.Load(ws), reader, "Carcharodontosaurus");

        Assert.Equal(["Tail"], again.ClipMoved);
    }

    [Fact]
    public void Without_an_animation_table_it_says_the_animations_were_not_read()
    {
        var (game, ws, install, reader) = Setup();
        using var _ = game;
        Directory.Delete(Path.Combine(ws.DataDir, "objects", "PrehistoricKingdom.AnimationV2.AnimationTableV2"), recursive: true);

        var info = RigInfoService.For(ws, install, Index(), SpeciesSkinsReader.Load(ws), reader, "Carcharodontosaurus");

        Assert.Empty(info.ClipMoved);
        Assert.Contains(info.Failures, f => f.Contains("animations"));
    }
}
