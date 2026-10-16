using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;
using static Tyrant.Core.Tests.SkinDumps;

namespace Tyrant.Core.Tests;

public class StartingSkinTests
{
    private const string Template = "Allosaurus Anax";

    /// <summary>A textured template (skin 0 both sexes, skin 1 male only), Carch with a male-only skin 1, and a vivarium animal.</summary>
    internal static IReadOnlyList<SpeciesSkins> Species() =>
    [
        new(Template, false,
        [
            new VanillaSkin(0, "Default", new Dictionary<string, string> { ["diffuse"] = MaleDiffuse, ["normal"] = MaleNormal, ["infantDiffuse"] = FemaleDiffuse },
                new Dictionary<string, string> { ["diffuse"] = FemaleDiffuse, ["extra"] = FemaleExtra }),
            new VanillaSkin(1, "Albino", new Dictionary<string, string> { ["diffuse"] = MaleDiffuse }, new Dictionary<string, string>()),
        ]) { PrefabGuid = "template-prefab" },
        new("Carcharodontosaurus", false,
        [
            new VanillaSkin(0, "Base", new Dictionary<string, string>(), new Dictionary<string, string>()),
            new VanillaSkin(1, "Alt 1", new Dictionary<string, string> { ["diffuse"] = MaleDiffuse, ["normal"] = MaleNormal, ["pattern"] = MalePattern },
                new Dictionary<string, string>()),
        ]) { PrefabGuid = PrefabGuid },
        new("Frog", true, [new VanillaSkin(0, "Frog", new Dictionary<string, string> { ["diffuse"] = MaleDiffuse }, new Dictionary<string, string>())]),
    ];

    private sealed class Setup : IDisposable
    {
        public FakeGame Game { get; } = new();
        public Workspace Ws { get; }
        public GameInstall Install { get; }
        public FakeAssetReader Reader { get; } = new();
        public ModProject Mod { get; }

        public Setup()
        {
            Install = new GameInstall(Game.Root, null);
            Ws = Workspace.Create(Path.Combine(Game.LibraryRoot, "workspace"), Install);
            Mod = ModProject.Create(Ws, "pack", null, null);
        }

        public SpeciesEntry Create(StartingSkinSource source, string? startName = null, int startingSkin = 0) =>
            Mod.CreateSpecies(Install, Index(), Reader, Species(), "mira", Template, "Mirabilis", "Mira", "bilis", null, null, null, startingSkin, source, startName);

        public string Png(string name)
        {
            var path = Path.Combine(Ws.Dir, name);
            File.WriteAllBytes(path, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jWWQAAAAASUVORK5CYII="));
            return path;
        }

        public void Dispose() => Game.Dispose();
    }

    [Fact]
    public void Create_species_copies_the_template_skin_as_its_starting_skin()
    {
        using var s = new Setup();

        s.Create(new CopySkin(Template, 0));

        var skin = Assert.Single(s.Mod.Manifest.Skins);
        Assert.Equal(("mira-start", "tyrant.pack.mira", "0", true, "Default"), (skin.Id, skin.Species, skin.Base, skin.Starting, skin.Name));
        Assert.Null(skin.CopiedFrom);
        Assert.Equal(new[] { "diffuse", "infantDiffuse", "normal" }, skin.Male!.Keys.Order());
        Assert.Equal(new[] { "diffuse", "extra" }, skin.Female!.Keys.Order());
        foreach (var file in skin.Male.Values.Concat(skin.Female.Values)) Assert.True(File.Exists(Path.Combine(s.Mod.Dir, file)), file);
        Assert.Equal("mira-start", ModProject.Open(s.Ws, "pack").StartingSkinOf("mira")?.Id);
    }

    [Fact]
    public void Copying_from_another_species_records_it_and_keeps_only_the_sexes_it_has()
    {
        using var s = new Setup();

        s.Create(new CopySkin("Carcharodontosaurus", 1), startName: "Carch look");

        var skin = Assert.Single(s.Mod.Manifest.Skins);
        Assert.Equal(("Carcharodontosaurus", 1), (skin.CopiedFrom!.Species, skin.CopiedFrom.Skin));
        Assert.Equal("Carch look", skin.Name);
        Assert.Equal(new[] { "diffuse", "normal", "pattern" }, skin.Male!.Keys.Order());
        Assert.Null(skin.Female);
    }

    [Fact]
    public void Own_pngs_give_the_diffuse_and_the_template_gives_the_other_maps()
    {
        using var s = new Setup();
        var male = s.Png("mine.png");

        s.Create(new OwnPngs(male, null));

        var skin = Assert.Single(s.Mod.Manifest.Skins);
        Assert.Equal("Mirabilis", skin.Name);
        Assert.Equal(new[] { "diffuse", "normal" }, skin.Male!.Keys.Order()); // no infantDiffuse: babies wear the modder's diffuse
        Assert.Equal(new[] { "diffuse", "extra" }, skin.Female!.Keys.Order());
        Assert.Equal(File.ReadAllBytes(male), File.ReadAllBytes(Path.Combine(s.Mod.Dir, skin.Male["diffuse"])));
        Assert.Equal(File.ReadAllBytes(male), File.ReadAllBytes(Path.Combine(s.Mod.Dir, skin.Female["diffuse"])));
        Assert.Null(skin.CopiedFrom);
    }

    [Fact]
    public void A_failed_starting_skin_leaves_no_species_skin_or_files()
    {
        using var s = new Setup();

        Assert.Throws<TyrantException>(() => s.Create(new OwnPngs(Path.Combine(s.Ws.Dir, "missing.png"), null)));

        var reopened = ModProject.Open(s.Ws, "pack");
        Assert.Empty(reopened.Manifest.Species);
        Assert.Empty(reopened.Manifest.Skins);
        Assert.False(Directory.Exists(Path.Combine(s.Mod.Dir, "skins")));
        foreach (var code in AnimalLocalization.Codes) Assert.False(Directory.Exists(Path.Combine(s.Mod.Dir, "localization", code, "mira")), code);
    }

    [Theory]
    [InlineData("Frog")]
    [InlineData("tyrant.other.thing")]
    [InlineData("Nobody")]
    public void A_vivarium_custom_or_unknown_copy_source_is_refused(string species)
    {
        using var s = new Setup();

        var ex = Assert.Throws<TyrantException>(() => s.Create(new CopySkin(species, 0)));

        Assert.Equal(TyrantErrorCode.TargetNotFound, ex.Code);
        Assert.Contains("base-game", ex.Message);
    }

    [Fact]
    public void Copying_another_template_skin_makes_it_the_one_the_species_is_copied_from()
    {
        using var s = new Setup();
        s.Create(new CopySkin(Template, 0));
        s.Mod.RemoveSkin("mira-start", deleteFiles: true);

        s.Mod.AddStartingSkin(s.Install, Index(), s.Reader, Species(), "mira", new CopySkin(Template, 1));

        Assert.Equal(1, ModProject.Open(s.Ws, "pack").Manifest.Species.Single().StartingSkin);
    }

    [Fact]
    public void A_second_starting_skin_is_refused_and_after_removal_a_new_one_gets_a_fresh_id()
    {
        using var s = new Setup();
        s.Create(new CopySkin(Template, 0));

        var twice = Assert.Throws<TyrantException>(() => s.Mod.AddStartingSkin(s.Install, Index(), s.Reader, Species(), "mira", new CopySkin(Template, 0)));
        Assert.Equal(TyrantErrorCode.ModInvalid, twice.Code);
        Assert.Contains("already has a starting skin (mira-start)", twice.Message);

        s.Mod.RemoveSkin("mira-start", deleteFiles: false);
        var again = s.Mod.AddStartingSkin(s.Install, Index(), s.Reader, Species(), "mira", new CopySkin(Template, 1));

        Assert.Equal("mira-start-2", again.Id);
        Assert.Null(again.Female); // skin 1 has no female textures
    }
}
