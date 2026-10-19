using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class MaterialFoldersTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteFile(string dir, string relative, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dir, relative))!);
        File.WriteAllText(Path.Combine(dir, relative), text);
    }

    private static string SkinsJson(params string[] ids) =>
        "{ \"format\": 1, \"id\": \"mod-id\", \"skins\": [ "
        + string.Join(",", ids.Select(id => "{ \"id\": \"" + id + "\", \"species\": \"Titanoboa\", \"male\": { \"diffuse\": \"a.png\" } }")) + " ] }";

    private static MaterialOverride Block() => new()
    {
        Iridescence = new IridescenceSettings
        {
            Strength = new VariantNumbers { Normal = 0.35f },
            Ramp = new RampSource { ThinFilm = new ThinFilmSettings { Thickness = 420 } },
            Mask = new MaskSource { From = "extra", Channel = 'g', Invert = true },
        },
    };

    /// <summary>A species has no skin slots to take a mask from, so its mask is a file.</summary>
    private static MaterialOverride SpeciesBlock()
    {
        var block = Block();
        block.Iridescence!.Mask = new MaskSource { File = "materials/species/Titanoboa/mask.png" };
        return block;
    }

    private static void AssertBlock(MaterialOverride m)
    {
        Assert.Equal(0.35f, m.Iridescence!.Strength!.Normal);
        Assert.Equal(420, m.Iridescence.Ramp!.ThinFilm!.Thickness);
        Assert.Equal(("extra", 'g', true), (m.Iridescence.Mask!.From, m.Iridescence.Mask.Channel, m.Iridescence.Mask.Invert));
    }

    [Fact]
    public void Skin_species_and_vanilla_skin_folders_round_trip()
    {
        var dir = TempDir();
        var saved = ModManifest.Parse(SkinsJson("rainbow"));
        saved.Skins[0].Materials = new Dictionary<string, MaterialOverride> { ["Titanoboa"] = Block() };
        saved.SpeciesMaterials["Titanoboa"] = new Dictionary<string, MaterialOverride> { ["Titanoboa"] = SpeciesBlock() };
        saved.VanillaSkinMaterials["Titanoboa/Skin A"] = new Dictionary<string, MaterialOverride> { ["Titanoboa"] = Block() };

        MaterialFolders.Save(saved, dir);
        var loaded = ModManifest.Parse(SkinsJson("rainbow"));
        var problems = new List<string>();
        MaterialFolders.Load(loaded, dir, problems);

        Assert.Empty(problems);
        AssertBlock(loaded.Skins[0].Materials!["Titanoboa"]);
        Assert.Equal("materials/species/Titanoboa/mask.png", loaded.SpeciesMaterials["titanoboa"]["Titanoboa"].Iridescence!.Mask!.File);
        AssertBlock(loaded.VanillaSkinMaterials["Titanoboa/Skin A"]["Titanoboa"]);
        var files = MaterialFolders.Files(loaded).ToArray();
        Assert.Equal(
            new[] { "materials/skins/rainbow/materials.json", "materials/species/Titanoboa/materials.json", "materials/vanilla-skins/Titanoboa-Skin A/materials.json" },
            files.Where(f => f.EndsWith("materials.json")).ToArray());
        Assert.Contains("materials/species/Titanoboa/mask.png", files);
    }

    [Fact]
    public void Files_lists_the_images_the_blocks_name()
    {
        var manifest = ModManifest.Parse(SkinsJson("rainbow"));
        manifest.Skins[0].Materials = new Dictionary<string, MaterialOverride>
        {
            ["Titanoboa"] = new()
            {
                Iridescence = new IridescenceSettings { Ramp = new RampSource { File = "materials/skins/rainbow/ramp.png" }, Mask = new MaskSource { File = "m.png" } },
                Fur = new FurMasks { Mask = new MaskSource { File = "fur.png" }, InfantMask = new MaskSource { File = "baby.png" } },
            },
        };

        Assert.Equal(
            new[] { "materials/skins/rainbow/materials.json", "materials/skins/rainbow/ramp.png", "m.png", "fur.png", "baby.png" },
            MaterialFolders.Files(manifest).ToArray());
    }

    [Fact]
    public void A_vanilla_skin_name_with_a_slash_keeps_its_identity()
    {
        var dir = TempDir();
        var saved = ModManifest.Parse(SkinsJson("rainbow"));
        saved.VanillaSkinMaterials["Titanoboa/Alt 1/2"] = new Dictionary<string, MaterialOverride> { ["Titanoboa"] = Block() };

        MaterialFolders.Save(saved, dir);
        var loaded = ModManifest.Parse(SkinsJson("rainbow"));
        MaterialFolders.Load(loaded, dir, new List<string>());

        Assert.True(File.Exists(Path.Combine(dir, "materials", "vanilla-skins", "Titanoboa-Alt 1_2", "materials.json")));
        AssertBlock(loaded.VanillaSkinMaterials["Titanoboa/Alt 1/2"]["Titanoboa"]);
    }

    [Fact]
    public void A_bad_file_is_one_problem_and_the_rest_loads()
    {
        var dir = TempDir();
        WriteFile(dir, "materials/skins/bad/materials.json", """{"format":1,"materials":{"Titanoboa":{"settings":{"_Nope":1}}}}""");
        WriteFile(dir, "materials/skins/good/materials.json", """{"format":1,"materials":{"Titanoboa":{"settings":{"_Smoothness":0.5}}}}""");
        var manifest = ModManifest.Parse(SkinsJson("bad", "good"));
        var problems = new List<string>();

        MaterialFolders.Load(manifest, dir, problems);

        var problem = Assert.Single(problems);
        Assert.Contains("materials/skins/bad/materials.json", problem);
        Assert.Contains("\"_Nope\"", problem);
        Assert.Null(manifest.Skins[0].Materials);
        Assert.Equal(0.5f, manifest.Skins[1].Materials!["Titanoboa"].Settings["_Smoothness"].Number);
    }

    [Fact]
    public void A_folder_without_a_file_is_ignored_and_the_identity_comes_from_the_file()
    {
        var dir = TempDir();
        Directory.CreateDirectory(Path.Combine(dir, "materials", "species", "empty"));
        WriteFile(dir, "materials/species/renamed-folder/materials.json", """{"format":1,"species":"Titanoboa","materials":{"M":{"settings":{"_Smoothness":0.5}}}}""");
        WriteFile(dir, "materials/species/no-species/materials.json", """{"format":1,"materials":{"M":{"settings":{"_Smoothness":0.5}}}}""");
        var manifest = ModManifest.Parse(SkinsJson("rainbow"));
        var problems = new List<string>();

        MaterialFolders.Load(manifest, dir, problems);

        Assert.Contains("materials/species/no-species/materials.json", Assert.Single(problems));
        Assert.Equal(new[] { "Titanoboa" }, manifest.SpeciesMaterials.Keys.ToArray());
    }

    [Fact]
    public void Old_materials_in_mod_json_are_refused_with_where_they_went()
    {
        var skin = Assert.Throws<ManifestException>(() => ModManifest.Parse("""
            { "format": 1, "id": "mod-id", "skins": [ { "id": "rainbow", "species": "S", "male": { "diffuse": "a.png" }, "materials": { "M": {} } } ] }
            """));
        Assert.Contains("materials/skins/rainbow/materials.json", skin.Message);

        var model = Assert.Throws<ManifestException>(() => ModManifest.Parse("""
            { "format": 1, "id": "mod-id", "models": [ { "target": "Titanoboa", "materials": { "M": {} } } ] }
            """));
        Assert.Contains("materials/species/Titanoboa/materials.json", model.Message);
    }

    [Fact]
    public void Saving_without_materials_removes_the_file_and_empty_folder()
    {
        var dir = TempDir();
        var manifest = ModManifest.Parse(SkinsJson("rainbow", "keep"));
        manifest.Skins[0].Materials = new Dictionary<string, MaterialOverride> { ["Titanoboa"] = Block() };
        manifest.Skins[1].Materials = new Dictionary<string, MaterialOverride> { ["Titanoboa"] = Block() };
        manifest.SpeciesMaterials["Titanoboa"] = new Dictionary<string, MaterialOverride> { ["Titanoboa"] = SpeciesBlock() };
        MaterialFolders.Save(manifest, dir);
        File.WriteAllText(Path.Combine(dir, "materials", "skins", "keep", "ramp.png"), "x"); // images stay
        manifest.Skins[0].Materials = null;
        manifest.Skins[1].Materials = new Dictionary<string, MaterialOverride>();
        manifest.SpeciesMaterials.Clear();

        MaterialFolders.Save(manifest, dir);

        Assert.False(Directory.Exists(Path.Combine(dir, "materials", "skins", "rainbow")));
        Assert.False(File.Exists(Path.Combine(dir, "materials", "skins", "keep", "materials.json")));
        Assert.True(File.Exists(Path.Combine(dir, "materials", "skins", "keep", "ramp.png")));
        Assert.False(Directory.Exists(Path.Combine(dir, "materials", "species")));
    }

    [Fact]
    public void Fur_masks_accept_only_a_file()
    {
        var dir = TempDir();
        WriteFile(dir, "materials/skins/sk-a/materials.json", """{"format":1,"materials":{"M":{"fur":{"mask":"everywhere"}}}}""");
        WriteFile(dir, "materials/skins/sk-b/materials.json", """{"format":1,"materials":{"M":{"fur":{"mask":"materials/skins/sk-b/fur-mask.png","infantMask":"baby.png"}}}}""");
        WriteFile(dir, "materials/skins/sk-c/materials.json", """{"format":1,"materials":{"M":{"fur":{"mask":{"from":"fur"}}}}}""");
        var manifest = ModManifest.Parse(SkinsJson("sk-a", "sk-b", "sk-c"));
        var problems = new List<string>();

        MaterialFolders.Load(manifest, dir, problems);

        Assert.Equal(2, problems.Count);
        Assert.All(problems, p => Assert.Contains("fur.mask", p));
        var fur = manifest.Skins[1].Materials!["M"].Fur!;
        Assert.Equal("materials/skins/sk-b/fur-mask.png", fur.Mask!.File);
        Assert.Equal("baby.png", fur.InfantMask!.File);
        MaterialFolders.Save(manifest, dir);
        Assert.Contains("\"infantMask\": \"baby.png\"", File.ReadAllText(Path.Combine(dir, "materials", "skins", "sk-b", "materials.json")));
    }

    [Fact]
    public void Save_leaves_files_that_Load_did_not_read()
    {
        var dir = TempDir();
        WriteFile(dir, "materials/skins/bad/materials.json", """{"format":1,"materials":{"Titanoboa":{"settings":{"_Nope":1}}}}""");
        WriteFile(dir, "materials/skins/gone/materials.json", """{"format":1,"materials":{"Titanoboa":{"settings":{"_Smoothness":0.5}}}}""");
        WriteFile(dir, "materials/skins/good/materials.json", """{"format":1,"materials":{"Titanoboa":{"settings":{"_Smoothness":0.5}}}}""");
        WriteFile(dir, "materials/species/a/materials.json", """{"format":1,"species":"Titanoboa","materials":{"M":{"settings":{"_Smoothness":0.5}}}}""");
        WriteFile(dir, "materials/species/b/materials.json", """{"format":1,"species":"titanoboa","materials":{"M":{"settings":{"_Smoothness":0.9}}}}""");
        var manifest = ModManifest.Parse(SkinsJson("bad", "good"));
        var problems = new List<string>();
        MaterialFolders.Load(manifest, dir, problems);

        Assert.Contains("materials/species/b/materials.json", Assert.Single(problems, p => p.Contains("species/b")));
        Assert.Equal(0.5f, manifest.SpeciesMaterials["Titanoboa"]["M"].Settings["_Smoothness"].Number);
        manifest.Skins[1].Materials = null; // emptied after Load read it: this one goes

        MaterialFolders.Save(manifest, dir);

        Assert.True(File.Exists(Path.Combine(dir, "materials", "skins", "bad", "materials.json")));
        Assert.True(File.Exists(Path.Combine(dir, "materials", "skins", "gone", "materials.json"))); // no such skin: not Load's
        Assert.True(File.Exists(Path.Combine(dir, "materials", "species", "b", "materials.json")));
        Assert.False(Directory.Exists(Path.Combine(dir, "materials", "skins", "good")));
    }

    [Theory]
    [InlineData("Titanoboa")]
    [InlineData("Titanoboa/")]
    [InlineData("/Skin")]
    public void A_malformed_vanilla_skin_key_is_refused(string key)
    {
        var manifest = ModManifest.Parse(SkinsJson("rainbow"));
        manifest.VanillaSkinMaterials[key] = new Dictionary<string, MaterialOverride> { ["M"] = Block() };

        var ex = Assert.Throws<ManifestException>(() => MaterialFolders.Save(manifest, TempDir()));
        Assert.Contains(key, ex.Message);
    }

    [Fact]
    public void Vanilla_skins_that_sanitize_alike_get_their_own_folders()
    {
        var dir = TempDir();
        var saved = ModManifest.Parse(SkinsJson("rainbow"));
        saved.VanillaSkinMaterials["T/Alt 1/2"] = new Dictionary<string, MaterialOverride> { ["M"] = Block() };
        saved.VanillaSkinMaterials["T/Alt 1_2"] = new Dictionary<string, MaterialOverride> { ["M"] = SpeciesBlock() };

        MaterialFolders.Save(saved, dir);
        var loaded = ModManifest.Parse(SkinsJson("rainbow"));
        var problems = new List<string>();
        MaterialFolders.Load(loaded, dir, problems);

        Assert.Empty(problems);
        Assert.Equal(2, loaded.VanillaSkinMaterials.Count);
        AssertBlock(loaded.VanillaSkinMaterials["T/Alt 1/2"]["M"]);
        Assert.NotNull(loaded.VanillaSkinMaterials["T/Alt 1_2"]["M"].Iridescence!.Mask!.File);
        Assert.Equal(2, MaterialFolders.Files(loaded).Count(f => f.EndsWith("materials.json")));
    }

    [Fact]
    public void Atomic_writes_replace_and_leave_no_temp_file()
    {
        var path = Path.Combine(TempDir(), "x.json");

        AtomicFile.Write(path, "one");
        AtomicFile.Write(path, "two");

        Assert.Equal("two", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Theory]
    [InlineData("0.5", true)]
    [InlineData("3", false)]
    public void Droopage_is_known(string value, bool accepted) => AssertSetting("_Droopage", value, accepted);

    [Theory]
    [InlineData("[0, 0, -1]", true)]
    [InlineData("[0, -10.3, 8.11]", true)] // the Velociraptor adult's own droop
    [InlineData("[0, 0, -20]", true)]
    [InlineData("[0, 0, -21]", false)]
    [InlineData("[0, 0]", false)]
    public void Droop_direction_is_known(string value, bool accepted) => AssertSetting("_DroopDirection", value, accepted);

    [Fact]
    public void Material_images_are_the_files_under_the_mods_materials_folder()
    {
        var mod = Path.Combine(Path.GetTempPath(), "tyrant-tests", "mod-a");
        Assert.True(MaterialFolders.IsMaterialImage(Path.Combine(mod, "materials", "skins", "a", "fur-mask.png"), mod));
        Assert.True(MaterialFolders.IsMaterialImage(Path.Combine(mod, "Materials", "species", "T", "m.png"), mod));
        Assert.False(MaterialFolders.IsMaterialImage(Path.Combine(mod, "textures", "materials", "m.png"), mod));
        Assert.False(MaterialFolders.IsMaterialImage(Path.Combine(mod, "materials.png"), mod));
        Assert.False(MaterialFolders.IsMaterialImage(Path.Combine(Path.GetTempPath(), "tyrant-tests", "mod-b", "materials", "m.png"), mod));
    }

    private static void AssertSetting(string key, string value, bool accepted)
    {
        var dir = TempDir();
        WriteFile(dir, "materials/skins/sk-a/materials.json", "{\"format\":1,\"materials\":{\"M\":{\"settings\":{\"" + key + "\":" + value + "}}}}");
        var manifest = ModManifest.Parse(SkinsJson("sk-a"));
        var problems = new List<string>();

        MaterialFolders.Load(manifest, dir, problems);

        if (accepted) Assert.Empty(problems);
        else Assert.Contains(key, Assert.Single(problems));
        Assert.Equal(accepted, manifest.Skins[0].Materials != null);
    }
}
