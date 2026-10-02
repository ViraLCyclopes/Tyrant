using PK.Core.Assets;
using PK.Core.Catalog;
using PK.Core.Errors;
using PK.Core.Install;

namespace PK.Core.Tests;

public class AssetIndexTests
{
    private const string Guid = "19b2ba2040871f64cac8f8c0c66ca525";
    private const string TexPath = "Assets/Art/Animals/Stego/T_Stego_D.png";
    private const string Bundle = "StandaloneWindows64/stego_assets/t_stego_d.png_1.bundle";

    private static AssetIndex Sample() => new()
    {
        Fingerprint = new GameFingerprint("b", "h"),
        Assets =
        [
            new AssetRecord(Bundle, 10, "Texture2D", "T_Stego_D", TexPath, Guid, null),
            new AssetRecord(Bundle, 11, "Sprite", "T_Stego_D", TexPath, Guid, null),
            new AssetRecord("StandaloneWindows64/other.bundle", 5, "MonoBehaviour", "", null, null, "AnimalGrowthManager"),
        ],
    };

    [Fact]
    public void Query_filters_by_type_and_case_insensitive_text()
    {
        var index = Sample();
        Assert.Equal(2, index.Query(null, "stego_d").Count());
        Assert.Single(index.Query("texture2d", "STEGO"));
        Assert.Single(index.Query(null, "growthmanager"));
        Assert.Equal(3, index.Query(null, null).Count());
    }

    [Fact]
    public void Resolve_by_ref_guid_or_container_path()
    {
        var index = Sample();
        Assert.Equal(11, index.Resolve($"{Bundle}#11").PathId);
        Assert.Equal(10, index.Resolve(Guid, "Texture2D").PathId);
        Assert.Equal(11, index.Resolve(TexPath.ToUpperInvariant(), "sprite").PathId);
    }

    [Fact]
    public void Resolve_ambiguous_key_lists_refs()
    {
        var ex = Assert.Throws<PkException>(() => Sample().Resolve(Guid));
        Assert.Equal(PkErrorCode.AssetAmbiguous, ex.Code);
        Assert.Contains($"{Bundle}#10", ex.Message);
        Assert.Contains($"{Bundle}#11", ex.Message);
    }

    [Fact]
    public void Resolve_unknown_key_is_not_found()
    {
        Assert.Equal(PkErrorCode.AssetNotFound, Assert.Throws<PkException>(() => Sample().Resolve("nope")).Code);
        Assert.Equal(PkErrorCode.AssetNotFound, Assert.Throws<PkException>(() => Sample().Resolve(Guid, "Mesh")).Code);
    }

    [Fact]
    public void Save_and_load_round_trip()
    {
        var path = Path.Combine(Path.GetTempPath(), "pk-tests", System.Guid.NewGuid().ToString("N"), AssetIndex.FileName);
        var index = Sample();
        index.Failures.Add(new IndexFailure("StandaloneWindows64/broken.bundle", "boom"));
        index.MissingBundles.Add("StandaloneWindows64/DLC/x.bundle");

        index.Save(path);
        var loaded = AssetIndex.Load(path);

        Assert.Equal(index.Assets, loaded.Assets);
        Assert.Equal(index.Failures, loaded.Failures);
        Assert.Equal(index.MissingBundles, loaded.MissingBundles);
        Assert.Equal(index.Fingerprint, loaded.Fingerprint);
    }

    [Fact]
    public void Load_missing_or_corrupt_index_throws_index_missing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pk-tests", System.Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, AssetIndex.FileName);
        Assert.Equal(PkErrorCode.AssetIndexMissing, Assert.Throws<PkException>(() => AssetIndex.Load(path)).Code);

        Directory.CreateDirectory(dir);
        File.WriteAllText(path, "{ nope");
        var ex = Assert.Throws<PkException>(() => AssetIndex.Load(path));
        Assert.Equal(PkErrorCode.AssetIndexMissing, ex.Code);
        Assert.Equal(FixAction.RefreshWorkspace, ex.Fix);
    }

    [Fact]
    public void Catalog_guids_attach_by_container_path_and_type()
    {
        var catalog = AddressablesCatalog.Parse(CatalogFixture.Build(
            CatalogFixture.Bundle("StandaloneWindows64/x.bundle"),
            new CatalogFixture.Entry(TexPath, CatalogFixture.AssetProvider, "UnityEngine.Texture2D", [TexPath, Guid], [0]),
            new CatalogFixture.Entry("Assets/Data/Audio.asset", CatalogFixture.AssetProvider, "PrehistoricKingdom.AnimalAudioDatabase",
                ["Assets/Data/Audio.asset", "aaaabbbbccccddddeeeeffff00001111"], [0])));
        AssetRecord[] records =
        [
            new(Bundle, 10, "Texture2D", "T_Stego_D", TexPath, null, null),
            new(Bundle, 12, "Mesh", "m", null, null, null),
            new("StandaloneWindows64/x.bundle", 3, "MonoBehaviour", "Audio", "assets/data/audio.asset", null, "AnimalAudioDatabase"),
        ];

        var result = AssetIndex.AttachCatalogKeys(records, catalog);

        Assert.Equal(Guid, result[0].Guid);
        Assert.Null(result[1].Guid);
        Assert.Equal("aaaabbbbccccddddeeeeffff00001111", result[2].Guid);
    }

    [Theory]
    [InlineData("""{"assets":[null]}""")]
    [InlineData("""{"assets":[{"bundle":null,"pathId":1,"type":"Texture2D","name":"x"}]}""")]
    public void Load_rejects_incomplete_records(string json)
    {
        var dir = Path.Combine(Path.GetTempPath(), "pk-tests", System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, AssetIndex.FileName);
        File.WriteAllText(path, json);

        Assert.Equal(PkErrorCode.AssetIndexMissing, Assert.Throws<PkException>(() => AssetIndex.Load(path)).Code);
    }
}
