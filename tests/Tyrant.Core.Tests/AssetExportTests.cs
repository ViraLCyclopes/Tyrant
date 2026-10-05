using System.Text.Json;
using Tyrant.Core.Assets;
using Tyrant.Core.Blender;
using Tyrant.Core.Install;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Tests;

public class AssetExportTests
{
    private static (FakeGame Game, GameInstall Install, Workspace Ws) Setup()
    {
        var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), install);
        return (game, install, ws);
    }

    private static AssetRecord Asset(string type, string name, long pathId, string? container = null, string? guid = null) =>
        new("animals/stego_assets_assets/stego.bundle", pathId, type, name, container, guid, null);

    [Fact]
    public void Routes_each_type_to_its_format_and_folder()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        var reader = new FakeAssetReader();

        var (report, reportPath) = new AssetExport(reader).Run(install, ws, [
            Asset("Texture2D", "T_Stego_D", 1, "Assets/Art/Stego/T_Stego_D.png", "0123456789abcdef0123456789abcdef"),
            Asset("GameObject", "Stegosaurus", 2),
            Asset("Mesh", "Stego_Body", 3),
            Asset("MonoBehaviour", "AnimalData_Stego", 4),
        ], null, CancellationToken.None);

        Assert.All(report.Items, i => Assert.True(i.Success, i.Error));
        Assert.Equal(Path.Combine(ws.AssetsDir, "textures", "Art", "Stego", "T_Stego_D.png"), Assert.Single(report.Items[0].Outputs));
        Assert.Equal(2, report.Items[1].Outputs.Count);
        Assert.StartsWith(Path.Combine(ws.AssetsDir, "models", "Stegosaurus") + Path.DirectorySeparatorChar, report.Items[1].Outputs[0]);
        Assert.StartsWith(Path.Combine(ws.AssetsDir, "models", "Stego_Body") + Path.DirectorySeparatorChar, report.Items[2].Outputs[0]);
        Assert.Equal(Path.Combine(ws.AssetsDir, "json", "MonoBehaviour", "AnimalData_Stego_4.json"), Assert.Single(report.Items[3].Outputs));
        Assert.Equal((1, 2, 1), (reader.Textures, reader.Models, reader.Json));
        Assert.StartsWith(Path.Combine(ws.Dir, "exports", "asset-export-"), reportPath);
    }

    [Fact]
    public void One_failure_does_not_stop_the_rest_and_is_reported()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        var bad = Asset("Texture2D", "Broken", 1);
        var reader = new FakeAssetReader();
        reader.FailFor.Add(bad.Ref);

        var (report, _) = new AssetExport(reader).Run(install, ws, [bad, Asset("Texture2D", "Fine", 2)], null, CancellationToken.None);

        Assert.False(report.Items[0].Success);
        Assert.Contains("could not be read", report.Items[0].Error);
        Assert.Empty(report.Items[0].Outputs);
        Assert.True(report.Items[1].Success);
    }

    [Fact]
    public void Same_named_assets_get_distinct_files()
    {
        var (game, install, ws) = Setup();
        using var _ = game;

        var (report, _) = new AssetExport(new FakeAssetReader()).Run(install, ws, [
            Asset("Texture2D", "T", 1, "Assets/Art/T.png"), Asset("Texture2D", "T", 2, "Assets/Art/T.png"),
            Asset("GameObject", "Rex", 3), Asset("GameObject", "Rex", 4),
        ], null, CancellationToken.None);

        Assert.NotEqual(report.Items[0].Outputs[0], report.Items[1].Outputs[0]);
        Assert.NotEqual(Path.GetDirectoryName(report.Items[2].Outputs[0]), Path.GetDirectoryName(report.Items[3].Outputs[0]));
    }

    [Fact]
    public void Unsafe_names_stay_inside_the_workspace()
    {
        var (game, install, ws) = Setup();
        using var _ = game;

        var (report, _) = new AssetExport(new FakeAssetReader()).Run(install, ws, [
            Asset("GameObject", "..\\..\\evil:name", 1), Asset("MonoBehaviour", "", 2), Asset("Mesh", "a/b", 3),
        ], null, CancellationToken.None);

        var root = Path.GetFullPath(ws.AssetsDir) + Path.DirectorySeparatorChar;
        Assert.All(report.Items.SelectMany(i => i.Outputs), o => Assert.StartsWith(root, Path.GetFullPath(o)));
    }

    [Fact]
    public void The_report_lists_every_item_with_its_keys()
    {
        var (game, install, ws) = Setup();
        using var _ = game;

        var (_, reportPath) = new AssetExport(new FakeAssetReader()).Run(install, ws, [
            Asset("Texture2D", "T_Stego_D", 1, "Assets/Art/Stego/T_Stego_D.png", "0123456789abcdef0123456789abcdef"),
        ], null, CancellationToken.None);

        var item = JsonDocument.Parse(File.ReadAllText(reportPath)).RootElement.GetProperty("items")[0];
        Assert.Equal("Assets/Art/Stego/T_Stego_D.png", item.GetProperty("containerPath").GetString());
        Assert.Equal("0123456789abcdef0123456789abcdef", item.GetProperty("guid").GetString());
        Assert.Equal("animals/stego_assets_assets/stego.bundle#1", item.GetProperty("ref").GetString());
    }

    [Fact]
    public void Cancellation_stops_the_export()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            new AssetExport(new FakeAssetReader()).Run(install, ws, [Asset("Texture2D", "T", 1)], null, cts.Token));
    }

    [Fact]
    public void An_unexpected_reader_error_fails_only_that_item_and_the_report_is_still_written()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        var streamed = Asset("Mesh", "carnivore.macromound.medium", 1);
        var reader = new FakeAssetReader { FailWith = _ => new NotSupportedException("The vertex data is streamed in a .resS file.") };
        reader.FailFor.Add(streamed.Ref);

        var (report, reportPath) = new AssetExport(reader).Run(install, ws, [Asset("Texture2D", "T", 2), streamed], null, CancellationToken.None);

        Assert.True(report.Items[0].Success);
        Assert.False(report.Items[1].Success);
        Assert.Contains(".resS", report.Items[1].Error);
        Assert.True(File.Exists(reportPath));
    }

    [Fact]
    public void Models_are_written_with_the_index_so_their_textures_are_found()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        var reader = new FakeAssetReader();
        var index = new AssetIndex();

        new AssetExport(reader, index).Run(install, ws, [Asset("GameObject", "Stegosaurus", 2)], null, CancellationToken.None);

        Assert.Same(index, reader.LastModelIndex);
    }

    [Fact]
    public void Texture_notes_reach_the_report()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        var reader = new FakeAssetReader { ModelNotes = ["Texture T_Stego_D could not be decoded, so its material is plain: BC7"] };

        var (report, _) = new AssetExport(reader, new AssetIndex()).Run(install, ws, [Asset("GameObject", "Stegosaurus", 2)], null, CancellationToken.None);

        Assert.True(report.Items[0].Success);
        Assert.Contains("BC7", Assert.Single(report.Items[0].Notes!));
    }

    [Fact]
    public void A_model_export_lists_its_texture_pngs_in_the_report()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        var reader = new FakeAssetReader { ModelTextureFiles = ["T_Stego_D.png"] };

        var (report, _) = new AssetExport(reader).Run(install, ws, [Asset("GameObject", "Stegosaurus", 2)], null, CancellationToken.None);

        Assert.Contains(report.Items[0].Outputs, o => o.EndsWith(Path.Combine("textures", "T_Stego_D.png")));
        Assert.Contains(report.Items[0].Outputs, o => o.EndsWith(".glb"));
    }

    [Fact]
    public void A_model_export_as_fbx_lists_the_fbx_files_in_one_conversion()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        var reader = new FakeAssetReader { ModelTextureFiles = ["T_Stego_D.png"] };
        var converter = new FakeModelConverter();

        var (report, _) = new AssetExport(reader, null, ModelFormat.Fbx, converter).Run(install, ws,
            [Asset("GameObject", "Stegosaurus", 2), Asset("GameObject", "Rex", 3), Asset("Texture2D", "T", 4)], null, CancellationToken.None);

        Assert.Single(converter.Batches);
        foreach (var model in report.Items.Take(2))
        {
            Assert.NotEmpty(model.Outputs.Where(o => o.EndsWith(".fbx")));
            Assert.All(model.Outputs.Where(o => o.EndsWith(".fbx")), o => Assert.True(File.Exists(o)));
            Assert.DoesNotContain(model.Outputs, o => o.EndsWith(".glb"));
            Assert.Contains(model.Outputs, o => o.EndsWith("T_Stego_D.png"));
        }
        Assert.EndsWith(".png", Assert.Single(report.Items[2].Outputs)); // a texture is untouched
    }

    [Fact]
    public void A_bare_mesh_export_says_its_skin_is_not_included()
    {
        var (game, install, ws) = Setup();
        using var _ = game;

        var (report, _) = new AssetExport(new FakeAssetReader()).Run(install, ws, [Asset("Mesh", "Stego_Body", 3), Asset("GameObject", "Stegosaurus", 2)], null, CancellationToken.None);

        Assert.Contains(report.Items[0].Notes ?? [], n => n.Contains("without its bones"));
        Assert.DoesNotContain(report.Items[1].Notes ?? [], n => n.Contains("without its bones"));
    }

    [Fact]
    public void An_export_runs_in_one_batch()
    {
        var (game, install, ws) = Setup();
        using var _ = game;
        var reader = new FakeAssetReader();

        new AssetExport(reader).Run(install, ws, [Asset("MonoBehaviour", "A", 1), Asset("MonoBehaviour", "B", 2), Asset("Texture2D", "C", 3)], null, CancellationToken.None);

        Assert.Equal(1, reader.Batches);
        Assert.Equal(3, reader.CallsInBatch);
    }

    [Theory]
    [InlineData(20, 20)]
    [InlineData(80, 80)]
    [InlineData(200, 79)]
    public void Long_names_are_shortened_with_a_hash(int length, int expected)
    {
        Assert.Equal(expected, AssetExport.Shorten(new string('a', length)).Length);
        Assert.NotEqual(AssetExport.Shorten(new string('a', 200)), AssetExport.Shorten(new string('a', 199) + "b"));
    }

    [Fact]
    public void A_long_asset_name_gives_a_short_file_name()
    {
        var (game, install, ws) = Setup();
        using var _ = game;

        var (report, _) = new AssetExport(new FakeAssetReader()).Run(install, ws, [Asset("MonoBehaviour", new string('x', 240), 9)], null, CancellationToken.None);

        Assert.True(Path.GetFileName(report.Items[0].Outputs[0]).Length <= 100, report.Items[0].Outputs[0]);
    }
}
