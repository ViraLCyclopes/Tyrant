using Tyrant.Core.Blender;
using Tyrant.Core.Errors;

namespace Tyrant.Core.Tests;

public class ModelFormatsTests
{
    private static string[] Files(params string[] names)
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"))).FullName;
        return [.. names.Select(n => { var p = Path.Combine(dir, n); File.WriteAllText(p, "x"); return p; })];
    }

    [Theory]
    [InlineData(null, ModelFormat.Glb)]
    [InlineData("GLB", ModelFormat.Glb)]
    [InlineData("fbx", ModelFormat.Fbx)]
    [InlineData("both", ModelFormat.Both)]
    public void Formats_parse(string? text, ModelFormat expected) => Assert.Equal(expected, ModelFormats.Parse(text));

    [Fact]
    public void An_unknown_format_is_a_clear_error() =>
        Assert.Contains("glb, fbx or both", Assert.Throws<TyrantException>(() => ModelFormats.Parse("obj")).Message);

    [Fact]
    public void Fbx_replaces_the_glb_files_in_one_conversion_and_leaves_other_files()
    {
        var files = Files("a.glb", "b.glb", "t.png");
        var converter = new FakeModelConverter();

        var (result, notes) = ModelFormats.Apply(converter, files, ModelFormat.Fbx);

        Assert.Single(converter.Batches);
        Assert.Equal([Path.ChangeExtension(files[0], ".fbx"), Path.ChangeExtension(files[1], ".fbx"), files[2]], result);
        Assert.False(File.Exists(files[0]));
        Assert.Empty(notes);
    }

    [Fact]
    public void Both_keeps_the_glb_next_to_the_fbx()
    {
        var files = Files("a.glb");
        var (result, _) = ModelFormats.Apply(new FakeModelConverter(), files, ModelFormat.Both);
        Assert.Equal([files[0], Path.ChangeExtension(files[0], ".fbx")], result);
        Assert.True(File.Exists(files[0]));
    }

    [Fact]
    public void A_species_pack_keeps_its_glb_files_with_fbx()
    {
        var files = Files("a.glb");
        var (result, _) = ModelFormats.Apply(new FakeModelConverter(), files, ModelFormat.Fbx, keepGlb: true);
        Assert.Equal([files[0], Path.ChangeExtension(files[0], ".fbx")], result);
    }

    [Fact]
    public void One_failed_conversion_keeps_the_others_and_its_glb()
    {
        var files = Files("a.glb", "b.glb");
        var (result, notes) = ModelFormats.Apply(new FakeModelConverter("b.glb"), files, ModelFormat.Fbx);
        Assert.Equal([Path.ChangeExtension(files[0], ".fbx"), files[1]], result);
        Assert.Contains("b.glb could not be converted to FBX: no armature", Assert.Single(notes));
    }

    [Fact]
    public void Glb_converts_nothing()
    {
        var converter = new FakeModelConverter();
        var files = Files("a.glb");
        Assert.Equal(files, ModelFormats.Apply(converter, files, ModelFormat.Glb).Files);
        Assert.Empty(converter.Batches);
    }
}
