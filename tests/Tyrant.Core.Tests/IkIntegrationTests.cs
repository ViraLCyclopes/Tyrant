using Tyrant.Core.Assets;
using Tyrant.Core.Models;
using Tyrant.Core.Species;

namespace Tyrant.Core.Tests;

/// <summary>The real game's FABRIK chains (TYRANT_GAME_DIR): bipeds have a head and two legs, quadrupeds a head and four.</summary>
[Trait("Category", "Integration")]
public class IkIntegrationTests(RealGameIndex real) : IClassFixture<RealGameIndex>
{
    private PrefabModel Read(string name)
    {
        var species = SpeciesCatalog.Find(SpeciesCatalog.FromIndex(real.Index), name);
        using var session = new AssetSession(real.Install);
        return new ModelExporter().ReadPrefab(session, species.Prefab);
    }

    [SkippableFact]
    public void Every_animal_reads_three_or_five_chains_without_failures()
    {
        Skip.If(RealGameIndex.GameDir is null, "TYRANT_GAME_DIR not set");
        using var session = new AssetSession(real.Install);
        var withChains = 0;
        foreach (var species in SpeciesCatalog.FromIndex(real.Index))
        {
            var prefab = new ModelExporter().ReadPrefab(session, species.Prefab);
            session.Release();
            Assert.True(prefab.IkFailures.Count == 0, $"{species.DisplayName}: {string.Join(" | ", prefab.IkFailures)}");
            if (prefab.IkChains.Count == 0) continue;
            withChains++;
            Assert.True(prefab.IkChains.Count is 3 or 5, $"{species.DisplayName}: {prefab.IkChains.Count} chains");
            Assert.Single(prefab.IkChains, c => c.Kind == IkChainKind.Head);
        }
        Assert.True(withChains >= 60, $"only {withChains} species have IK chains");
    }
}
