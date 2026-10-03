using PK.Core.Assets;
using PK.Core.Errors;
using PK.Core.Species;

namespace PK.Core.Tests;

public class SpeciesCatalogTests
{
    private static AssetRecord Prefab(string container, string bundle) => new(bundle, 1, "GameObject", "x", container, null, null);
    private static AssetRecord Texture(string name, string bundle) => new(bundle, 2, "Texture2D", name, $"Assets/Art/{name}.png", null, null);

    private static AssetIndex Sample() => new()
    {
        Assets =
        [
            Prefab("Assets/Prefabs/Animals/V2-MainPrefabs/Stegosaurus Stenops.V2.prefab", "StandaloneWindows64/stego_assets_assets/prefabs/stenops.bundle"),
            Prefab("Assets/Prefabs/Animals/V2-MainPrefabs/Stegosaurus Ungulatus.V2.prefab", "StandaloneWindows64/stego_assets_assets/prefabs/ungulatus.bundle"),
            Prefab("Assets/Prefabs/Animals/V2-MainPrefabs/Scelidosaurus.V2.prefab", "StandaloneWindows64/scelido_assets_all_abc.bundle"),
            Prefab("Assets/Prefabs/Animals/Vivarium/Titanoboa.prefab", "StandaloneWindows64/titano_assets_assets/prefabs/titanoboa.bundle"),
            Prefab("Assets/Prefabs/Animals/Dung/Dung.prefab", "StandaloneWindows64/dung.bundle"),
            Texture("T_stego_D", "StandaloneWindows64/stego_assets_assets/art/t_stego_d.bundle"),
            Texture("T_scelido_D", "StandaloneWindows64/scelido_assets_all_abc.bundle"),
            Texture("T_other", "StandaloneWindows64/other_assets_assets/art/t_other.bundle"),
        ],
    };

    [Fact]
    public void Lists_park_and_vivarium_animals_only()
    {
        var species = SpeciesCatalog.FromIndex(Sample());

        Assert.Equal(new[] { "scelidosaurus", "stegosaurusstenops", "stegosaurusungulatus", "titanoboa" }, species.Select(s => s.Key));
        Assert.Equal("Stegosaurus Stenops", species.Single(s => s.Key == "stegosaurusstenops").DisplayName);
        Assert.True(species.Single(s => s.Key == "titanoboa").Vivarium);
        Assert.False(species.Single(s => s.Key == "scelidosaurus").Vivarium);
    }

    [Theory]
    [InlineData("Stegosaurus Stenops", "stegosaurusstenops")]
    [InlineData("stegosaurusstenops", "stegosaurusstenops")]
    [InlineData("UNGULATUS", "stegosaurusungulatus")]
    [InlineData("titano", "titanoboa")]
    public void Find_accepts_display_names_keys_and_unique_fragments(string query, string expected)
    {
        Assert.Equal(expected, SpeciesCatalog.Find(SpeciesCatalog.FromIndex(Sample()), query).Key);
    }

    [Fact]
    public void Find_with_ambiguous_fragment_lists_candidates()
    {
        var ex = Assert.Throws<PkException>(() => SpeciesCatalog.Find(SpeciesCatalog.FromIndex(Sample()), "stego"));
        Assert.Equal(PkErrorCode.AssetAmbiguous, ex.Code);
        Assert.Contains("stegosaurusstenops", ex.Message);
        Assert.Contains("stegosaurusungulatus", ex.Message);
    }

    [Fact]
    public void Find_unknown_species_is_not_found()
    {
        Assert.Equal(PkErrorCode.AssetNotFound, Assert.Throws<PkException>(() => SpeciesCatalog.Find(SpeciesCatalog.FromIndex(Sample()), "giraffatitan")).Code);
    }

    [Theory]
    [InlineData("StandaloneWindows64/stego_assets_assets/prefabs/x.bundle", "StandaloneWindows64/stego_assets_assets")]
    [InlineData("StandaloneWindows64/scelido_assets_all_abc.bundle", "StandaloneWindows64/scelido_assets_all_abc.bundle")]
    [InlineData("StandaloneWindows64/DLC/Deluxe/dlc_deluxe_giraffa_assets_assets/art/t.bundle", "StandaloneWindows64/DLC/Deluxe/dlc_deluxe_giraffa_assets_assets")]
    public void Bundle_group_is_the_asset_group_folder(string bundle, string group)
    {
        Assert.Equal(group, SpeciesCatalog.BundleGroup(bundle));
    }

    [Fact]
    public void Textures_come_from_the_species_asset_group()
    {
        var index = Sample();
        var all = SpeciesCatalog.FromIndex(index);

        Assert.Equal(new[] { "T_stego_D" }, SpeciesCatalog.TexturesFor(index, SpeciesCatalog.Find(all, "stenops")).Select(t => t.Name));
        Assert.Equal(new[] { "T_scelido_D" }, SpeciesCatalog.TexturesFor(index, SpeciesCatalog.Find(all, "scelido")).Select(t => t.Name));
    }
}
