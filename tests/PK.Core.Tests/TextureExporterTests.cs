using PK.Core.Assets;
using PK.Core.Install;
using PK.Core.Workspaces;

namespace PK.Core.Tests;

public class TextureExporterTests
{
    private static readonly string AssetsDir = Path.Combine(Path.GetTempPath(), "pk-tests", "assets-dir");
    private static readonly string TexturesDir = Path.Combine(AssetsDir, "textures");

    private static AssetRecord Tex(string? container, string name = "T", long pathId = 7, string bundle = "StandaloneWindows64/x/t.png_1.bundle") =>
        new(bundle, pathId, "Texture2D", name, container, null, null);

    [Fact]
    public void Output_path_mirrors_container_path_without_assets_prefix()
    {
        Assert.Equal(Path.Combine(TexturesDir, "Art", "Animals", "T_Stego_D.png"),
            TextureExporter.OutputPathFor(Tex("Assets/Art/Animals/T_Stego_D.png"), AssetsDir));
        Assert.Equal(Path.Combine(TexturesDir, "Art", "T_Stego_D.tga.png"),
            TextureExporter.OutputPathFor(Tex("Assets/Art/T_Stego_D.tga"), AssetsDir));
    }

    [Fact]
    public void Output_path_stays_inside_textures_folder()
    {
        var path = TextureExporter.OutputPathFor(Tex("Assets/../../../Windows/evil.png"), AssetsDir);
        Assert.StartsWith(TexturesDir + Path.DirectorySeparatorChar, path);
        Assert.Equal(Path.Combine(TexturesDir, "Windows", "evil.png"), path);
    }

    [Fact]
    public void Invalid_filename_characters_are_replaced()
    {
        Assert.Equal(Path.Combine(TexturesDir, "Art", "a_b.png"), TextureExporter.OutputPathFor(Tex("Assets/Art/a:b.png"), AssetsDir));
    }

    [Fact]
    public void Texture_without_container_path_goes_to_unnamed_folder()
    {
        Assert.Equal(Path.Combine(TexturesDir, "_unnamed", "t.png_1", "FurMask_42.png"),
            TextureExporter.OutputPathFor(Tex(null, "FurMask", 42), AssetsDir));
    }

    [Fact]
    public void Non_texture_and_missing_bundle_fail_without_throwing()
    {
        using var game = new FakeGame();
        using var session = new AssetSession(new GameInstall(game.Root, null));
        var exporter = new TextureExporter();

        var mesh = exporter.Export(session, Tex("Assets/m.fbx") with { Type = "Mesh" }, Path.Combine(AssetsDir, "m.png"));
        var gone = exporter.Export(session, Tex("Assets/t.png"), Path.Combine(AssetsDir, "t.png"));

        Assert.False(mesh.Success);
        Assert.Contains("not a Texture2D", mesh.Error);
        Assert.False(gone.Success);
        Assert.Contains("no longer exists", gone.Error);
    }

    [Fact]
    public void ExportMany_never_assigns_two_textures_the_same_file()
    {
        using var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "pk-tests", Guid.NewGuid().ToString("N")), install);

        var results = new TextureExporter().ExportMany(install, ws,
            [Tex("Assets/Art/T.png", pathId: 1), Tex("Assets/Art/T.png", pathId: 2)], null, CancellationToken.None);

        Assert.Equal(2, results.Select(r => r.OutputPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.EndsWith("T_2.png", results[1].OutputPath);
    }

    [Fact]
    public void Sources_differing_only_by_extension_get_distinct_files()
    {
        var png = TextureExporter.OutputPathFor(Tex("Assets/Art/T_archaeopteryx_female_D.png"), AssetsDir);
        var tga = TextureExporter.OutputPathFor(Tex("Assets/Art/T_archaeopteryx_female_D.tga"), AssetsDir);
        var jpg = TextureExporter.OutputPathFor(Tex("Assets/Art/T_archaeopteryx_female_D.jpg"), AssetsDir);
        Assert.Equal(3, new[] { png, tga, jpg }.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ExportMany_keeps_suffixed_names_unique()
    {
        using var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "pk-tests", Guid.NewGuid().ToString("N")), install);

        var results = new TextureExporter().ExportMany(install, ws,
            [Tex("Assets/Art/T_2.png", pathId: 9), Tex("Assets/Art/T.png", pathId: 1), Tex("Assets/Art/T.png", pathId: 2)], null, CancellationToken.None);

        Assert.Equal(3, results.Select(r => r.OutputPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
