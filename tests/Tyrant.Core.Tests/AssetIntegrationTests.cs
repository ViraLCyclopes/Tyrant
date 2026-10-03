using Tyrant.Core.Species;
using System.Text.Json;
using Tyrant.Core.Assets;
using Tyrant.Core.Catalog;
using Tyrant.Core.Install;

namespace Tyrant.Core.Tests;

/// <summary>Builds the real asset index once (≈15 s) when TYRANT_GAME_DIR is set.</summary>
public sealed class RealGameIndex
{
    public static readonly string? GameDir = Environment.GetEnvironmentVariable("TYRANT_GAME_DIR");

    private readonly Lazy<(GameInstall Install, AssetIndex Index)> _built = new(() =>
    {
        var install = new GameInstall(Path.TrimEndingDirectorySeparator(Path.GetFullPath(GameDir!)), null);
        return (install, new AssetIndexer().Build(install, null, CancellationToken.None));
    });

    public GameInstall Install => _built.Value.Install;
    public AssetIndex Index => _built.Value.Index;
}

[Trait("Category", "Integration")]
public class AssetIntegrationTests(RealGameIndex real) : IClassFixture<RealGameIndex>
{
    [SkippableFact]
    public void Packed_normal_maps_export_as_standard_normal_maps()
    {
        Skip.If(RealGameIndex.GameDir is null, "TYRANT_GAME_DIR not set");
        var texture = real.Index.Assets.First(a => a.Type == "Texture2D" && a.Name == "T_Acrocanthosaurus_N");
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var rawPath = Path.Combine(dir, "raw.png");
        var rebuiltPath = Path.Combine(dir, "rebuilt.png");
        using var session = new AssetSession(real.Install);
        var (file, field) = session.Open(texture);
        var decoder = AssetsTools.NET.Texture.TextureFile.ReadTextureFile(field);
        using (var stream = File.Create(rawPath))
            decoder.DecodeTextureImage(decoder.FillPictureData(file), stream, AssetsTools.NET.Texture.ImageExportType.Png, 100);

        var result = new TextureExporter().Export(session, texture, rebuiltPath);

        Assert.True(result.Success, result.Error);
        Assert.True(result.RebuiltNormal);
        var raw = StbImageSharp.ImageResult.FromMemory(File.ReadAllBytes(rawPath), StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        var rebuilt = StbImageSharp.ImageResult.FromMemory(File.ReadAllBytes(rebuiltPath), StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        Assert.Equal((raw.Width, raw.Height), (rebuilt.Width, rebuilt.Height));
        // Same orientation as the regular export: the rebuilt red is the packed alpha (X), green is unchanged (Y).
        for (var p = 0; p < raw.Width * raw.Height; p += 7919)
        {
            Assert.InRange(Math.Abs(rebuilt.Data[p * 4] - raw.Data[p * 4 + 3]), 0, 1);
            Assert.InRange(Math.Abs(rebuilt.Data[p * 4 + 1] - raw.Data[p * 4 + 1]), 0, 1);
        }
        var blue = Enumerable.Range(0, rebuilt.Width * rebuilt.Height).Average(p => (double)rebuilt.Data[p * 4 + 2]);
        Assert.True(blue > 200, $"a tangent-space normal map is mostly blue (mean blue {blue:0})");
    }

    [SkippableFact]
    public void A_mesh_streamed_from_a_resS_file_is_reported_as_unreadable()
    {
        Skip.If(RealGameIndex.GameDir is null, "TYRANT_GAME_DIR not set");
        var mesh = real.Index.Assets.First(a => a.Type == "Mesh" && a.Name == "carnivore.macromound.medium");
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "model");

        var ex = Assert.Throws<Tyrant.Core.Errors.TyrantException>(() => new BundleAssetReader().WriteModel(real.Install, mesh, dir));

        Assert.Equal(Tyrant.Core.Errors.TyrantErrorCode.AssetUnreadable, ex.Code);
    }

    [SkippableFact]
    public void Bundle_reader_inspects_and_previews_a_real_skin_texture()
    {
        Skip.If(RealGameIndex.GameDir is null, "TYRANT_GAME_DIR not set");
        var texture = real.Index.Assets.First(a => a.ContainerPath == SkinTexture && a.Type == "Texture2D");
        var reader = new BundleAssetReader();
        var png = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "skin.png");

        var inspection = reader.Inspect(real.Install, texture);
        var facts = reader.WriteTexture(real.Install, texture, png);

        Assert.True(inspection.ByteSize > 0);
        Assert.Contains("m_Width", inspection.FieldsJson);
        Assert.True(facts.Width > 0 && facts.Height > 0);
        Assert.True(new FileInfo(png).Length > 0);
    }

    [SkippableFact]
    public void Bundle_reader_converts_a_real_species_prefab()
    {
        Skip.If(RealGameIndex.GameDir is null, "TYRANT_GAME_DIR not set");
        var species = SpeciesCatalog.Find(SpeciesCatalog.FromIndex(real.Index), "Stegosaurus Stenops");
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "model");

        var facts = new BundleAssetReader().WriteModel(real.Install, species.Prefab, dir);

        Assert.NotEmpty(facts.Files);
        Assert.True(facts.Triangles > 0);
        Assert.True(facts.Skinned);
    }

    private const string SkinTexture = "Assets/Art/Animals/Dinosaurs/Acrocanthosaurus/Textures/T_Acrocanthosaurus_alt1_D.png";

    [SkippableFact]
    public void Catalog_maps_skin_texture_to_guid_and_installed_bundle()
    {
        Skip.If(RealGameIndex.GameDir is null, "TYRANT_GAME_DIR not set");
        var aaDir = AssetSession.AaDirOf(real.Install);
        var catalog = AddressablesCatalog.Load(Path.Combine(aaDir, "catalog.json"));

        var entry = Assert.Single(catalog.Entries, e => e.InternalId == SkinTexture && e.ShortResourceType == "Texture2D");
        Assert.Contains(entry.Keys, k => k.Length == 32 && k.All(Uri.IsHexDigit));
        var bundle = catalog.Entries[Assert.Single(entry.Dependencies)];
        Assert.True(File.Exists(Path.Combine(aaDir, AddressablesCatalog.BundleRelativePath(bundle.InternalId)!)));
    }

    [SkippableFact]
    public void Index_covers_all_bundles_with_few_failures()
    {
        Skip.If(RealGameIndex.GameDir is null, "TYRANT_GAME_DIR not set");
        var index = real.Index;

        Assert.True(index.Assets.Count > 20_000, $"only {index.Assets.Count} assets");
        Assert.True(index.Failures.Count < 20, string.Join("; ", index.Failures.Take(5)));
        Assert.Contains(index.Assets, a => a.Type == "Texture2D" && a.ContainerPath == SkinTexture && a.Guid is not null);
        Assert.Contains(index.Assets, a => a.Type == "MonoBehaviour" && a.Script == "AnimalGrowthManager");
    }

    [SkippableFact]
    public void Skin_texture_exports_to_png()
    {
        Skip.If(RealGameIndex.GameDir is null, "TYRANT_GAME_DIR not set");
        var texture = real.Index.Resolve(SkinTexture, "Texture2D");
        var output = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "skin.png");
        using var session = new AssetSession(real.Install);

        var result = new TextureExporter().Export(session, texture, output);

        Assert.True(result.Success, result.Error);
        var header = File.ReadAllBytes(output).Take(8).ToArray();
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, header);
        Assert.True(new FileInfo(output).Length > 10_000);
    }

    [SkippableFact]
    public void Skin_texture_dumps_as_json_with_name()
    {
        Skip.If(RealGameIndex.GameDir is null, "TYRANT_GAME_DIR not set");
        var texture = real.Index.Resolve(SkinTexture, "Texture2D");
        using var session = new AssetSession(real.Install);

        var json = JsonDocument.Parse(FieldJsonWriter.ToJson(session.Open(texture).BaseField)).RootElement;

        Assert.Equal("T_Acrocanthosaurus_alt1_D", json.GetProperty("m_Name").GetString());
        Assert.True(json.GetProperty("m_Width").GetInt32() > 0);
    }
}
