using Tyrant.Core.Animation;
using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Models;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Tests;

public class AnimationServiceTests
{
    private static readonly AssetRecord Walk = new("@data/sharedassets0.assets", 501, "AnimationClip", "Carch|Walk", null, null, null);
    private static readonly AssetRecord Roar = new("@data/sharedassets0.assets", 502, "AnimationClip", "Carch|Roar", null, null, null);

    /// <summary>A walk moving Tail (a first-level child of the prefab's Hip... here: Hip itself travels 1 m) and a constant roar.</summary>
    private static RawClip WalkClip()
    {
        var dense = Enumerable.Range(0, 31).SelectMany(f => new[] { 0f, 0f, f / 30f }).ToArray();
        return new RawClip("Carch|Walk", 30, 0, 1, true, 0, [], 3, 31, 0, dense, [], [new ClipBindingRaw(ClipReader.Crc32("Hip"), 1)]);
    }

    private static RawClip RoarClip() =>
        new("Carch|Roar", 30, 0, 2, false, 0, [], 0, 0, 0, [], [0, 0, 0, 1], [new ClipBindingRaw(ClipReader.Crc32("Hip/Tail"), 2)]);

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
        File.WriteAllText(Path.Combine(tables, "CarchTable.json"), """
            {"$type":"PrehistoricKingdom.AnimationV2.AnimationTableV2","$name":"CarchTable","$id":7,
             "a":{"_Clip":{"$ref":{"type":"UnityEngine.AnimationClip","name":"Carch|Walk","id":1}}},
             "b":{"_Clip":{"$ref":{"type":"UnityEngine.AnimationClip","name":"Carch|Roar","id":2}}},
             "c":{"_Clip":{"$ref":{"type":"UnityEngine.AnimationClip","name":"Carch|Gone","id":3}}}}
            """);
        var reader = new FakeAssetReader
        {
            PrefabModelToReturn = ModelFixture.Prefab(ModelFixture.Triangle(name: "Carch_LOD00"), skinned: true),
            RawClipsToReturn = [WalkClip(), RoarClip()],
        };
        return (game, ws, install, reader);
    }

    private static AssetIndex Index() => new() { Assets = [.. SkinDumps.Textures, SkinDumps.Prefab, Walk, Roar] };

    [Fact]
    public void The_list_names_each_animation_with_its_length_looping_and_travel()
    {
        var (game, ws, install, reader) = Setup();
        using var _ = game;

        var list = AnimationService.List(ws, install, Index(), SpeciesSkinsReader.Load(ws), reader, "Carcharodontosaurus");

        Assert.Equal(["Walk", "Roar"], list.Select(a => a.Name));
        Assert.Equal(new AnimationInfo("Carch|Walk", "Walk", 1, 30, true, true), list[0]);
        Assert.Equal(new AnimationInfo("Carch|Roar", "Roar", 2, 30, false, false), list[1]);
    }

    [Fact]
    public void The_list_comes_from_the_cache_the_second_time()
    {
        var (game, ws, install, reader) = Setup();
        using var _ = game;
        AnimationService.List(ws, install, Index(), SpeciesSkinsReader.Load(ws), reader, "Carcharodontosaurus");
        reader.RawClipsToReturn = [];

        Assert.Equal(2, AnimationService.List(ws, install, Index(), SpeciesSkinsReader.Load(ws), reader, "Carcharodontosaurus").Count);
    }

    [Fact]
    public void Clips_are_sampled_and_an_unknown_one_is_a_failure_naming_it()
    {
        var (game, ws, install, reader) = Setup();
        using var _ = game;
        reader.RawClipsToReturn = [WalkClip()];

        var clips = AnimationService.Clips(ws, install, Index(), SpeciesSkinsReader.Load(ws), reader, "Carcharodontosaurus", ["Carch|Walk", "Carch|Gone"]);

        Assert.Equal("Walk", clips[0].Clip!.Name);
        Assert.Equal("Hip", Assert.Single(clips[0].Clip!.Bones).Bone);
        Assert.Null(clips[1].Clip);
        Assert.Contains("Carch|Gone", clips[1].Failure);
    }

    [Fact]
    public void A_sampled_clip_comes_from_the_cache_the_second_time()
    {
        var (game, ws, install, reader) = Setup();
        using var _ = game;
        reader.RawClipsToReturn = [WalkClip()];
        AnimationService.Clips(ws, install, Index(), SpeciesSkinsReader.Load(ws), reader, "Carcharodontosaurus", ["Carch|Walk"]);
        reader.RawClipsToReturn = [];

        var again = AnimationService.Clips(ws, install, Index(), SpeciesSkinsReader.Load(ws), reader, "Carcharodontosaurus", ["Carch|Walk"]);

        Assert.NotNull(again[0].Clip);
    }

    [Fact]
    public void Without_the_animation_table_it_asks_for_the_data_dump()
    {
        var (game, ws, install, reader) = Setup();
        using var _ = game;
        Directory.Delete(Path.Combine(ws.DataDir, "objects", "PrehistoricKingdom.AnimationV2.AnimationTableV2"), recursive: true);

        var ex = Assert.Throws<TyrantException>(() => AnimationService.List(ws, install, Index(), SpeciesSkinsReader.Load(ws), reader, "Carcharodontosaurus"));

        Assert.Contains("Run data dump", ex.Message);
    }

    [Fact]
    public void An_animation_can_be_asked_for_by_its_shown_name()
    {
        var (game, ws, install, reader) = Setup();
        using var _ = game;
        reader.RawClipsToReturn = [WalkClip()];

        var clips = AnimationService.Clips(ws, install, Index(), SpeciesSkinsReader.Load(ws), reader, "Carcharodontosaurus", ["walk"]);

        Assert.Equal("Carch|Walk", clips[0].Clip!.Id);
    }
}
