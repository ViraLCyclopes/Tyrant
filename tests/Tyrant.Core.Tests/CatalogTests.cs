using System.Text;
using Tyrant.Core.Catalog;
using Tyrant.Core.Errors;

namespace Tyrant.Core.Tests;

public class CatalogTests
{
    private const string Guid = "19b2ba2040871f64cac8f8c0c66ca525";
    private const string TexturePath = "Assets/Art/Animals/T_Test_D.png";

    private static string SampleCatalog() => CatalogFixture.Build(
        CatalogFixture.Bundle("StandaloneWindows64/test_assets/t_test_d.png_abc.bundle"),
        new CatalogFixture.Entry(TexturePath, CatalogFixture.AssetProvider, "UnityEngine.Texture2D", [TexturePath, Guid], [0]));

    [Fact]
    public void Parses_entries_keys_and_dependencies()
    {
        var catalog = AddressablesCatalog.Parse(SampleCatalog());

        Assert.Equal(2, catalog.Entries.Count);
        var bundle = catalog.Entries[0];
        var texture = catalog.Entries[1];
        Assert.True(bundle.IsBundle);
        Assert.False(texture.IsBundle);
        Assert.Equal(TexturePath, texture.InternalId);
        Assert.Equal(new[] { Guid, TexturePath }.Order(StringComparer.Ordinal), texture.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(new[] { 0 }, texture.Dependencies);
        Assert.Equal("Texture2D", texture.ShortResourceType);
    }

    [Fact]
    public void Bundle_relative_path_strips_runtime_token_and_normalizes_separators()
    {
        Assert.Equal("StandaloneWindows64/a/b.bundle",
            AddressablesCatalog.BundleRelativePath(AddressablesCatalog.RuntimePathToken + @"\StandaloneWindows64\a/b.bundle"));
        Assert.Null(AddressablesCatalog.BundleRelativePath(TexturePath));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"m_InternalIds\":[],\"m_ProviderIds\":[],\"m_resourceTypes\":[],\"m_KeyDataString\":\"\",\"m_BucketDataString\":\"AQAAAA==\",\"m_EntryDataString\":\"\"}")]
    public void Corrupt_catalog_throws_catalog_invalid(string json)
    {
        var ex = Assert.Throws<TyrantException>(() => AddressablesCatalog.Parse(json));
        Assert.Equal(TyrantErrorCode.CatalogInvalid, ex.Code);
    }

    [Fact]
    public void Missing_catalog_file_throws_catalog_invalid()
    {
        var ex = Assert.Throws<TyrantException>(() => AddressablesCatalog.Load(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "catalog.json")));
        Assert.Equal(TyrantErrorCode.CatalogInvalid, ex.Code);
    }

    [Fact]
    public void Reads_each_serialized_object_type()
    {
        Assert.Equal("abc", CatalogBinaryReader.ReadObject([0, 3, 0, 0, 0, (byte)'a', (byte)'b', (byte)'c'], 0));
        Assert.Equal("hi", CatalogBinaryReader.ReadObject([1, 4, 0, 0, 0, (byte)'h', 0, (byte)'i', 0], 0));
        Assert.Equal((ushort)7, CatalogBinaryReader.ReadObject([2, 7, 0], 0));
        Assert.Equal(42u, CatalogBinaryReader.ReadObject([3, 42, 0, 0, 0], 0));
        Assert.Equal(42, CatalogBinaryReader.ReadObject([4, 42, 0, 0, 0], 0));
        Assert.Equal("ff", CatalogBinaryReader.ReadObject([5, 2, (byte)'f', (byte)'f'], 0));

        var json = Encoding.Unicode.GetBytes("{}");
        byte[] jsonObject = [7, 1, (byte)'A', 3, (byte)'C', (byte)'l', (byte)'s', (byte)json.Length, 0, 0, 0, .. json];
        Assert.Equal(new CatalogJsonObject("Cls", "{}"), CatalogBinaryReader.ReadObject(jsonObject, 0));
    }

    [Fact]
    public void Unknown_object_type_throws_FormatException()
    {
        Assert.Throws<FormatException>(() => CatalogBinaryReader.ReadObject([9, 0, 0, 0, 0], 0));
    }
}
