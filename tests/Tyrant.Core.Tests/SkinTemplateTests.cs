using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;
using static Tyrant.Core.Tests.SkinDumps;

namespace Tyrant.Core.Tests;

public class SkinTemplateTests
{
    private static readonly AssetIndex Index = SkinDumps.Index();

    private static (FakeGame Game, Workspace Ws, GameInstall Install) Setup(bool dump = true)
    {
        var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), install);
        if (dump) Write(ws.DataDir);
        return (game, ws, install);
    }

    [Fact]
    public void Species_and_their_vanilla_skins_come_from_the_dump_with_texture_guids()
    {
        var (game, ws, _) = Setup();
        using var _ = game;

        var species = SpeciesSkinsReader.Load(ws);

        var carch = species.Single(s => s.SpeciesId == "Carcharodontosaurus");
        Assert.False(carch.Vivarium);
        Assert.Equal(new[] { "Base", "Alt 1" }, carch.Skins.Select(s => s.Name));
        Assert.Equal(MaleDiffuse, carch.Skins[1].Male["diffuse"]);
        Assert.Equal(MaleNormal, carch.Skins[1].Male["normal"]);
        Assert.False(carch.Skins[1].Male.ContainsKey("extra")); // an empty GUID means no texture
        Assert.True(species.Single(s => s.SpeciesId == "Frog").Vivarium);
    }

    [Fact]
    public void Without_a_dump_adding_a_skin_says_how_to_make_one()
    {
        var (game, ws, _) = Setup(dump: false);
        using var _ = game;

        var ex = Assert.Throws<TyrantException>(() => SpeciesSkinsReader.Load(ws));

        Assert.Equal(TyrantErrorCode.DataMissing, ex.Code);
        Assert.Contains("Run data dump", ex.Message);
    }

    [Fact]
    public void Add_skin_writes_the_base_textures_as_a_template_and_the_entry()
    {
        var (game, ws, install) = Setup();
        using var _ = game;
        var reader = new FakeAssetReader();
        var mod = ModProject.Create(ws, "red-spot-carcharo", null, null);

        var entry = mod.AddSkin(ws, install, Index, reader, SpeciesSkinsReader.Load(ws), "carcharodontosaurus", "Red spot", "alt 1",
            new SkinTemplateOptions(Male: true, Female: true, Maps: false));

        Assert.Equal(("red-spot", "Carcharodontosaurus", "Red spot", "Alt 1"), (entry.Id, entry.Species, entry.Name, entry.Base));
        Assert.Equal("skins/red-spot/male_D.png", entry.Male!["diffuse"]);
        Assert.Equal("skins/red-spot/female_D.png", entry.Female!["diffuse"]);
        Assert.False(entry.Male.ContainsKey("normal")); // maps not asked for
        Assert.True(File.Exists(Path.Combine(mod.Dir, "skins", "red-spot", "male_D.png")));
        Assert.Equal(2, reader.Textures);
        Assert.Single(ModProject.Open(ws, "red-spot-carcharo").Manifest.Skins);
    }

    [Fact]
    public void Maps_add_normal_extra_and_pattern_when_the_base_has_them_and_one_sex_can_be_left_out()
    {
        var (game, ws, install) = Setup();
        using var _ = game;
        var mod = ModProject.Create(ws, "red-spot-carcharo", null, null);

        var entry = mod.AddSkin(ws, install, Index, new FakeAssetReader(), SpeciesSkinsReader.Load(ws), "Carcharodontosaurus", "Red spot", "1",
            new SkinTemplateOptions(Male: true, Female: false, Maps: true));

        Assert.Equal("skins/red-spot/male_N.png", entry.Male!["normal"]);
        Assert.Null(entry.Female);
    }

    [Fact]
    public void A_second_skin_with_the_same_name_gets_its_own_id()
    {
        var (game, ws, install) = Setup();
        using var _ = game;
        var mod = ModProject.Create(ws, "red-spot-carcharo", null, null);
        var species = SpeciesSkinsReader.Load(ws);
        var options = new SkinTemplateOptions(true, false, false);

        mod.AddSkin(ws, install, Index, new FakeAssetReader(), species, "Carcharodontosaurus", "Red spot", "Alt 1", options);
        var second = mod.AddSkin(ws, install, Index, new FakeAssetReader(), species, "Carcharodontosaurus", "Red spot", "Alt 1", options);

        Assert.Equal("red-spot-2", second.Id);
    }

    [Fact]
    public void Unknown_species_base_or_no_sex_are_refused()
    {
        var (game, ws, install) = Setup();
        using var _ = game;
        var mod = ModProject.Create(ws, "red-spot-carcharo", null, null);
        var species = SpeciesSkinsReader.Load(ws);
        var both = new SkinTemplateOptions(true, true, false);

        Assert.Equal(TyrantErrorCode.TargetNotFound, Assert.Throws<TyrantException>(() =>
            mod.AddSkin(ws, install, Index, new FakeAssetReader(), species, "Nessie", "X", null, both)).Code);
        Assert.Equal(TyrantErrorCode.TargetNotFound, Assert.Throws<TyrantException>(() =>
            mod.AddSkin(ws, install, Index, new FakeAssetReader(), species, "Carcharodontosaurus", "X", "Alt 9", both)).Code);
        Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() =>
            mod.AddSkin(ws, install, Index, new FakeAssetReader(), species, "Carcharodontosaurus", "X", null, new SkinTemplateOptions(false, false, false))).Code);
    }

    [Fact]
    public void A_base_skin_whose_name_repeats_is_written_by_its_number()
    {
        var (game, ws, install) = Setup();
        using var _ = game;
        var mod = ModProject.Create(ws, "red-spot-carcharo", null, null);
        var twins = new[] { new SpeciesSkins("Carcharodontosaurus", false,
        [
            new VanillaSkin(0, "Alt", new Dictionary<string, string> { ["diffuse"] = MaleDiffuse }, new Dictionary<string, string>()),
            new VanillaSkin(1, "Alt", new Dictionary<string, string> { ["diffuse"] = MaleDiffuse }, new Dictionary<string, string>()),
        ]) };

        var entry = mod.AddSkin(ws, install, Index, new FakeAssetReader(), twins, "Carcharodontosaurus", "Red", "1", new SkinTemplateOptions(true, false, false));

        Assert.Equal("1", entry.Base); // "Alt" would be ambiguous in game
    }

    [Fact]
    public void A_template_that_fails_halfway_leaves_no_files_behind()
    {
        var (game, ws, install) = Setup();
        using var _ = game;
        var mod = ModProject.Create(ws, "red-spot-carcharo", null, null);
        var maleOnly = new[] { new SpeciesSkins("Carcharodontosaurus", false,
            [new VanillaSkin(0, "Base", new Dictionary<string, string> { ["diffuse"] = MaleDiffuse }, new Dictionary<string, string>())]) }; // no female diffuse

        Assert.Throws<TyrantException>(() => mod.AddSkin(ws, install, Index, new FakeAssetReader(), maleOnly, "Carcharodontosaurus", "Red", null, new SkinTemplateOptions(true, true, false)));

        Assert.False(Directory.Exists(Path.Combine(mod.Dir, "skins", "red")));
        Assert.Empty(ModProject.Open(ws, "red-spot-carcharo").Manifest.Skins);
    }

    [Fact]
    public void A_failed_template_keeps_files_that_were_in_the_skin_folder_before()
    {
        var (game, ws, install) = Setup();
        using var _ = game;
        var mod = ModProject.Create(ws, "red-spot-carcharo", null, null);
        var folder = Path.Combine(mod.Dir, "skins", "red");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "my-art.psd"), "the user's own work"); // left from an entry removed by hand
        var maleOnly = new[] { new SpeciesSkins("Carcharodontosaurus", false,
            [new VanillaSkin(0, "Base", new Dictionary<string, string> { ["diffuse"] = MaleDiffuse }, new Dictionary<string, string>())]) };

        Assert.Throws<TyrantException>(() => mod.AddSkin(ws, install, Index, new FakeAssetReader(), maleOnly, "Carcharodontosaurus", "Red", null, new SkinTemplateOptions(true, true, false)));

        Assert.Equal(["my-art.psd"], Directory.GetFiles(folder).Select(Path.GetFileName));
    }

    private const string InfantDiffuse = "66666666666666666666666666666666", InfantNormal = "77777777777777777777777777777777";

    private static readonly AssetIndex IndexWithInfants = new()
    {
        Assets = [.. SkinDumps.Textures,
            new("carch.bundle", 6, "Texture2D", "T_carch_infant_D", "Assets/T_carch_infant_D.png", InfantDiffuse, null),
            new("carch.bundle", 7, "Texture2D", "T_carch_infant_N", "Assets/T_carch_infant_N.png", InfantNormal, null)],
    };

    private static IReadOnlyList<SpeciesSkins> SpeciesWithInfants() =>
    [
        new("Carcharodontosaurus", false, [new VanillaSkin(0, "Alt 1",
            new Dictionary<string, string> { ["diffuse"] = MaleDiffuse, ["normal"] = MaleNormal, ["infantDiffuse"] = InfantDiffuse, ["infantNormal"] = InfantNormal },
            new Dictionary<string, string> { ["diffuse"] = FemaleDiffuse })]),
    ];

    [Fact]
    public void Add_skin_also_copies_the_base_babies_textures_into_the_male_skin()
    {
        var (game, ws, install) = Setup();
        using var _ = game;
        var mod = ModProject.Create(ws, "red-spot-carcharo", null, null);

        var plain = mod.AddSkin(ws, install, IndexWithInfants, new FakeAssetReader(), SpeciesWithInfants(), "Carcharodontosaurus", "Red spot", "Alt 1",
            new SkinTemplateOptions(Male: true, Female: true, Maps: false));
        var withMaps = mod.AddSkin(ws, install, IndexWithInfants, new FakeAssetReader(), SpeciesWithInfants(), "Carcharodontosaurus", "Blue", "Alt 1",
            new SkinTemplateOptions(Male: true, Female: true, Maps: true));

        Assert.Equal("skins/red-spot/male_infant_D.png", plain.Male!["infantDiffuse"]);
        Assert.False(plain.Male.ContainsKey("infantNormal")); // maps not asked for
        Assert.False(plain.Female!.ContainsKey("infantDiffuse")); // babies wear the male skin's infant maps
        Assert.Equal("skins/blue/male_infant_N.png", withMaps.Male!["infantNormal"]);
        Assert.True(File.Exists(Path.Combine(mod.Dir, "skins", "red-spot", "male_infant_D.png")));
    }

    [Fact]
    public void A_slot_on_the_base_can_be_copied_into_the_skin_to_edit()
    {
        var (game, ws, install) = Setup();
        using var _ = game;
        var mod = ModProject.Create(ws, "red-spot-carcharo", null, null);
        var species = SpeciesWithInfants();
        mod.AddSkin(ws, install, IndexWithInfants, new FakeAssetReader(), species, "Carcharodontosaurus", "Red spot", "Alt 1",
            new SkinTemplateOptions(Male: true, Female: false, Maps: false));

        var file = mod.CopyBaseFile(install, IndexWithInfants, new FakeAssetReader(), species, "red-spot", "male", "infantNormal");

        Assert.Equal("skins/red-spot/male_infant_N.png", file);
        Assert.True(File.Exists(Path.Combine(mod.Dir, "skins", "red-spot", "male_infant_N.png")));
        Assert.Equal(file, ModProject.Open(ws, "red-spot-carcharo").Skin("red-spot").Male!["infantNormal"]);
        var none = Assert.Throws<TyrantException>(() => mod.CopyBaseFile(install, IndexWithInfants, new FakeAssetReader(), species, "red-spot", "male", "extra"));
        Assert.Contains("has no", none.Message);
    }

    [Fact]
    public void With_maps_the_fur_masks_come_too()
    {
        var (game, ws, install) = Setup();
        using var _ = game;
        var mod = ModProject.Create(ws, "woolly", null, null);
        const string Fur = "88888888888888888888888888888888", InfantFur = "99999999999999999999999999999999";
        var index = new AssetIndex { Assets = [.. IndexWithInfants.Assets,
            new("m.bundle", 8, "Texture2D", "T_mammoth_fur", "Assets/T_mammoth_fur.png", Fur, null),
            new("m.bundle", 9, "Texture2D", "T_mammoth_infant_fur", "Assets/T_mammoth_infant_fur.png", InfantFur, null)] };
        IReadOnlyList<SpeciesSkins> species = [new("Mammoth", false, [new VanillaSkin(0, "Base",
            new Dictionary<string, string> { ["diffuse"] = MaleDiffuse, ["fur"] = Fur, ["infantDiffuse"] = InfantDiffuse, ["infantFur"] = InfantFur },
            new Dictionary<string, string> { ["diffuse"] = FemaleDiffuse })])];

        var entry = mod.AddSkin(ws, install, index, new FakeAssetReader(), species, "Mammoth", "Shaggy", "Base", new SkinTemplateOptions(Male: true, Female: false, Maps: true));

        Assert.Equal("skins/shaggy/male_fur.png", entry.Male!["fur"]);
        Assert.Equal("skins/shaggy/male_infant_fur.png", entry.Male["infantFur"]);
    }
}
