using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class SaveOwnerTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static Dictionary<string, MaterialOverride> Block(params string[] names) =>
        names.ToDictionary(n => n, n => new MaterialOverride { Iridescence = new IridescenceSettings { Scale = 0.5f } });

    private static Dictionary<string, MaterialOverride> Read(string dir, string relative)
    {
        var map = (Dictionary<string, object?>)Json.Parse(File.ReadAllText(Path.Combine(dir, relative)))!;
        return MaterialOverrides.Parse(map["materials"], "test", true) ?? new();
    }

    [Fact]
    public void Saving_one_owner_keeps_its_other_materials_and_other_owners()
    {
        var dir = TempDir();
        var species = new MaterialOwner(MaterialOwnerKind.Species, null, "Titanoboa", null);
        var vanilla = new MaterialOwner(MaterialOwnerKind.VanillaSkin, null, "Titanoboa", "Skin A");
        MaterialFolders.SaveOwner(dir, species, Block("Body", "Eyes"));
        MaterialFolders.SaveOwner(dir, vanilla, Block("Body"));
        var otherFile = Path.Combine(dir, "materials", "vanilla-skins", "Titanoboa-Skin A", "materials.json");
        var otherText = File.ReadAllText(otherFile);

        var changed = new Dictionary<string, MaterialOverride>
        {
            ["Body"] = new MaterialOverride { Iridescence = new IridescenceSettings { Scale = -0.25f } },
            ["Eyes"] = new MaterialOverride(),
        };
        MaterialFolders.SaveOwner(dir, species, changed);

        var file = "materials/species/Titanoboa/materials.json";
        var materials = Read(dir, file);
        Assert.Equal(["Body"], materials.Keys);
        Assert.Equal(-0.25f, materials["Body"].Iridescence!.Scale);
        Assert.Equal("Titanoboa", ((Dictionary<string, object?>)Json.Parse(File.ReadAllText(Path.Combine(dir, file)))!)["species"]);
        Assert.Equal(otherText, File.ReadAllText(otherFile));

        MaterialFolders.SaveOwner(dir, species, new Dictionary<string, MaterialOverride> { ["Hair"] = Block("x")["x"] });
        Assert.Equal(["Body", "Hair"], Read(dir, file).Keys.OrderBy(k => k));

        MaterialFolders.SaveOwner(dir, species, null);
        Assert.False(Directory.Exists(Path.Combine(dir, "materials", "species")));
        Assert.True(File.Exists(otherFile));
    }
}
