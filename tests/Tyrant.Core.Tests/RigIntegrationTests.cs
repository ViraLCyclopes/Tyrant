using Tyrant.Core.Assets;
using Tyrant.Core.Blender;
using Tyrant.Core.Data;
using Tyrant.Core.Models;
using Tyrant.Core.Species;

namespace Tyrant.Core.Tests;

/// <summary>Rig editing's analysis on the real game (TYRANT_GAME_DIR) and a data dump of it (TYRANT_DATA_DIR, a workspace's data folder).</summary>
[Trait("Category", "Integration")]
public class RigIntegrationTests(RealGameIndex real) : IClassFixture<RealGameIndex>
{
    private static readonly string? DataDir = Environment.GetEnvironmentVariable("TYRANT_DATA_DIR");

    private (PrefabModel Prefab, IReadOnlyList<string> ClipMoved, BlenderGrowth? Growth) Analyse(string speciesId)
    {
        var store = DataStore.OpenDirectory(DataDir!);
        var species = SpeciesCatalog.Find(SpeciesCatalog.FromIndex(real.Index), speciesId);
        var reader = new BundleAssetReader();
        var prefab = reader.ReadPrefabModel(real.Install, species.Prefab);
        var names = ClipReader.ClipNames(store, speciesId).ToHashSet(StringComparer.Ordinal);
        var records = real.Index.Assets.Where(a => a.Type == "AnimationClip" && names.Contains(a.Name)).GroupBy(a => a.Name).Select(g => g.First()).ToList();
        var (clips, failures) = reader.ReadClips(real.Install, records);
        Assert.Empty(failures);
        Assert.True(clips.Count > 20, $"only {clips.Count} clips");
        return (prefab, ClipReader.MovedBones(clips, prefab.Root), BlenderGrowthReader.Read(store, speciesId, prefab.Root.DepthFirst().Select(n => n.Name)));
    }

    [SkippableFact]
    public void Carcharodontosaurus_animations_move_its_pelvis_and_femurs_but_not_its_calves()
    {
        Skip.If(RealGameIndex.GameDir is null || DataDir is null, "TYRANT_GAME_DIR and TYRANT_DATA_DIR not set");

        var (_, moved, _) = Analyse("Carcharodontosaurus");

        Assert.Contains("Pelvis", moved);
        Assert.Contains("Femur.L", moved);
        Assert.DoesNotContain("Calve.L", moved);
        Assert.DoesNotContain("Head", moved);
    }

    [SkippableFact]
    public void Allosaurus_anax_growth_positions_its_neck()
    {
        Skip.If(RealGameIndex.GameDir is null || DataDir is null, "TYRANT_GAME_DIR and TYRANT_DATA_DIR not set");

        var (_, _, growth) = Analyse("Allosaurus Anax");

        Assert.Contains(growth!.Bones, b => b.Name == "Neck.002" && b.Translation);
    }

    [SkippableFact]
    public void Carcharodontosaurus_walk_samples_into_keys_that_travel()
    {
        Skip.If(RealGameIndex.GameDir is null || DataDir is null, "TYRANT_GAME_DIR and TYRANT_DATA_DIR not set");
        var store = DataStore.OpenDirectory(DataDir!);
        var species = SpeciesCatalog.Find(SpeciesCatalog.FromIndex(real.Index), "Carcharodontosaurus");
        var reader = new BundleAssetReader();
        var prefab = reader.ReadPrefabModel(real.Install, species.Prefab);
        var names = ClipReader.ClipNames(store, "Carcharodontosaurus");
        Assert.True(names.Count > 50, $"only {names.Count} clips"); // 83 distinct: the table repeats clips (343 references)
        var walkId = names.First(n => n.EndsWith("|LocWalk", StringComparison.Ordinal));
        var record = real.Index.Assets.First(a => a.Type == "AnimationClip" && a.Name == walkId);

        var (raws, failures) = reader.ReadRawClips(real.Install, [record]);

        Assert.Empty(failures);
        var raw = Assert.Single(raws);
        var info = Tyrant.Core.Animation.ClipSampler.Info(raw, prefab.Root);
        var walk = Tyrant.Core.Animation.ClipSampler.Sample(raw, prefab.Root);
        Assert.True(info.Travels && walk.Travels);
        Assert.True(walk.Length > 0.3f && walk.FrameRate >= 24);
        var main = walk.Bones.Single(b => b.Bone == "MainBone");
        Assert.True(main.Position!.Count >= 2);
        Assert.True(walk.Bones.Count > 50, $"only {walk.Bones.Count} bones");
        // A sampled key equals the curve's own value at that time.
        var evaluator = new Tyrant.Core.Animation.ClipEvaluator(raw);
        var nodes = ClipReader.PathHashes(ClipReader.AnimatorRoot(raw.Bindings.Select(b => b.PathHash), prefab.Root));
        var offset = 0;
        foreach (var b in raw.Bindings)
        {
            if (b.Attribute == 1 && nodes.TryGetValue(b.PathHash, out var node) && node.Name == "MainBone") break;
            offset += Tyrant.Core.Animation.RawClip.Dims(b.Attribute);
        }
        var last = main.Position![^1];
        Assert.Equal(evaluator.Value(offset, raw.StartTime + last.Time), last.X, 4);
    }
}
