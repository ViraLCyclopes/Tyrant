using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class SizesApplyTests
{
    private static readonly (float, float)[] Allosaurus = { (0f, 0.164f), (1f, 1f) };
    private static readonly (double, double) Incubation = (0.925, 1.075);
    private static readonly (double, double) Breeding = (0.85, 1.15);

    /// <summary>A table of one mod with these sizes/ files (each a whole JSON text).</summary>
    private static SizeTable Table(params string[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "mod");
        Directory.CreateDirectory(Path.Combine(dir, SizeSettings.Folder));
        for (var i = 0; i < files.Length; i++) File.WriteAllText(Path.Combine(dir, "sizes", $"s{i}.json"), files[i]);
        var mod = new LoadedMod(ModManifest.Parse("""{"format":1,"id":"size-mod"}"""), dir);
        return SizeTable.Build(new[] { mod }, out _);
    }

    private static SizePlan Plan(SizeTable table) => new SizePlan(table, Incubation, Breeding);

    private static SizeSpecies Species(object data, string id, params (object Skin, double Male, double Female)[] skins) =>
        new SizeSpecies(data, id, Allosaurus, skins.Select(s => new SizeSkin(s.Skin, s.Male, s.Female)).ToList());

    [Fact]
    public void Sizes_apply_once_to_mod_added_skins()
    {
        var plan = Plan(Table("""{ "format": 1, "target": "Allosaurus", "adult": 0.8, "male": 1.1 }"""));
        object data = new object(), vanilla = new object(), modSkin = new object();

        var first = plan.Next(new[] { Species(data, "Allosaurus", (vanilla, 1.08, 1.12), (modSkin, 1.08, 1.12)) });

        Assert.Equal(2, first.Skins.Count);
        Assert.All(first.Skins, s => Assert.Equal((1.08 * 0.8 * 1.1, 1.12 * 0.8), (s.Male, s.Female)));
        Assert.Single(first.Curves);

        // A skin copied from a multiplied one carries its sizes already; a later pass changes neither it nor the others.
        object copy = new object();
        plan.Cloned(modSkin, copy);
        var second = plan.Next(new[] { Species(data, "Allosaurus", (vanilla, 0.95, 0.99), (modSkin, 0.95, 0.99), (copy, 0.95, 0.99)) });

        Assert.Empty(second.Skins);
        Assert.Empty(second.Curves);
    }

    [Fact]
    public void No_sizes_changes_nothing()
    {
        var empty = Table();
        Assert.False(empty.HasAny);
        var pass = Plan(empty).Next(new[] { Species(new object(), "Allosaurus", (new object(), 1.08, 1.12)) });
        Assert.True(pass.IsEmpty);

        // A file with only a target changes nothing either.
        var blank = Plan(Table("""{ "format": 1, "target": "Allosaurus" }""")).Next(new[] { Species(new object(), "Allosaurus", (new object(), 1.08, 1.12)) });
        Assert.True(blank.IsEmpty);

        // Sex sizes alone leave the growth curve as the game has it.
        var sexOnly = Plan(Table("""{ "format": 1, "target": "Allosaurus", "female": 1.2 }""")).Next(new[] { Species(new object(), "Allosaurus", (new object(), 1.08, 1.12)) });
        Assert.Empty(sexOnly.Curves);
        Assert.Single(sexOnly.Skins);

        // A range alone (an old limit is ignored) changes no curve, no skin and logs no sizes note.
        var rangeOnly = Plan(Table("""{ "format": 1, "target": "Allosaurus", "nursery": [0.9, 1.1], "limit": [0.05, 5] }"""))
            .Next(new[] { Species(new object(), "Allosaurus", (new object(), 1.08, 1.12)) });
        Assert.True(rangeOnly.IsEmpty);
        Assert.Empty(rangeOnly.Notes);
    }

    [Fact]
    public void Nothing_set_patches_nothing()
    {
        var none = Table();
        Assert.Equal((false, false, false), (none.HasAny, none.SetsSlider, none.SetsBreeding));

        var sizes = Table("""{ "format": 1, "target": "Allosaurus", "adult": 0.8 }""");
        Assert.Equal((true, false, false), (sizes.HasAny, sizes.SetsSlider, sizes.SetsBreeding));

        var nursery = Table("""{ "format": 1, "target": "Allosaurus", "nursery": [0.5, 3] }""");
        Assert.Equal((true, true, false), (nursery.HasAny, nursery.SetsSlider, nursery.SetsBreeding));

        var breeding = Table("""{ "format": 1, "target": "Allosaurus", "breeding": [0.7, 1.3] }""");
        Assert.Equal((true, false, true), (breeding.HasAny, breeding.SetsSlider, breeding.SetsBreeding));

        // An old limit is ignored: nothing to patch.
        Assert.False(Table("""{ "format": 1, "target": "Allosaurus", "limit": [0.05, 5] }""").HasAny);

        // Sizes within the game's 0.05 to 2 never lift its cap.
        var lifts = 0;
        var small = new SizePlan(sizes, Incubation, Breeding, () => { lifts++; return true; }).Next(new[] { Species(new object(), "Allosaurus", (new object(), 1.08, 1.12)) });
        Assert.Empty(small.Warnings);
        Assert.Equal(0, lifts);
    }

    [Fact]
    public void Unknown_target_is_skipped_with_one_warning()
    {
        var plan = Plan(Table("""{ "format": 1, "target": "Nosuchosaurus", "adult": 0.8 }"""));
        var species = new[] { Species(new object(), "Allosaurus", (new object(), 1.08, 1.12)) };

        var first = plan.Next(species);
        var second = plan.Next(species);

        Assert.Empty(first.Skins);
        Assert.Empty(first.Curves);
        var warning = Assert.Single(first.Warnings);
        Assert.StartsWith("Nosuchosaurus: ", warning);
        Assert.Empty(second.Warnings);
    }

    [Fact]
    public void Slider_range_is_the_selected_species_and_random_eggs_keep_the_games()
    {
        var table = Table(
            """{ "format": 1, "target": "Allosaurus", "nursery": [0.5, 1.5], "breeding": [0.7, 1.3] }""",
            """{ "format": 1, "target": "Tyrannosaurus", "nursery": [0.25, 3] }""");

        Assert.Equal(Incubation, SizePlan.RangeFor(SizeSite.RandomEgg, table.For("Allosaurus"), Incubation, Breeding));
        Assert.Equal((0.5, 1.5), SizePlan.RangeFor(SizeSite.NurserySlider, table.For("Allosaurus"), Incubation, Breeding));
        Assert.Equal((0.25, 3), SizePlan.RangeFor(SizeSite.NurserySlider, table.For("Tyrannosaurus"), Incubation, Breeding));
        Assert.Equal(Incubation, SizePlan.RangeFor(SizeSite.NurserySlider, table.For("Spinosaurus"), Incubation, Breeding));
        // What the slider transpiler looks the selected species up in: only species that set a Nursery range.
        Assert.Equal(new Dictionary<string, (double, double)> { ["Allosaurus"] = (0.5, 1.5), ["Tyrannosaurus"] = (0.25, 3) }, table.SliderRanges);
        Assert.True(table.SliderRanges.ContainsKey("allosaurus"));
        // Only the Nursery's slider reads are swapped; the random roll lives in VirtualAnimal, not in these methods.
        Assert.Equal(new[] { "GenerateNewAnimal", "UpdateCustomizationRect", "UpdateAnimalPreview" }, SizePlan.SliderMethods);
        Assert.DoesNotContain("GenerateGeneticSize", SizePlan.SliderMethods);
        Assert.DoesNotContain("CreateVirtualAnimal", SizePlan.SliderMethods);
        // The back-off counts every range in effect for the species, the vanilla random one included.
        Assert.Equal(new[] { Incubation, (0.5, 1.5), (0.7, 1.3) }, SizePlan.InEffect(table.For("Allosaurus"), Incubation, Breeding));
        Assert.Equal(new[] { Incubation, (0.25, 3), Breeding }, SizePlan.InEffect(table.For("Tyrannosaurus"), Incubation, Breeding));
        Assert.Equal(new[] { Incubation, Breeding }, SizePlan.InEffect(null, Incubation, Breeding));
    }

    [Fact]
    public void Breeding_range_is_the_embryos_species()
    {
        var table = Table(
            """{ "format": 1, "target": "Allosaurus", "breeding": [0.7, 1.3] }""",
            """{ "format": 1, "target": "Tyrannosaurus", "nursery": [0.25, 3] }""");

        Assert.Equal((0.7, 1.3), SizePlan.RangeFor(SizeSite.Breeding, table.For("Allosaurus"), Incubation, Breeding));
        Assert.Equal(Breeding, SizePlan.RangeFor(SizeSite.Breeding, table.For("Tyrannosaurus"), Incubation, Breeding));
        Assert.Equal(Breeding, SizePlan.RangeFor(SizeSite.Breeding, null, Incubation, Breeding));
        Assert.Equal(new Dictionary<string, (double, double)> { ["Allosaurus"] = (0.7, 1.3) }, table.BreedingRanges);
    }

    [Fact]
    public void Derived_limits_are_laid_out_by_native_species_index()
    {
        var table = Table(
            """{ "format": 1, "target": "Allosaurus", "adult": 0.8 }""",
            """{ "format": 1, "target": "Tyrannosaurus", "limit": [0.05, 20], "adult": 4 }"""); // the old limit is ignored
        var lifts = 0;
        var plan = new SizePlan(table, Incubation, Breeding, () => { lifts++; return true; });
        plan.Next(new[] { Species(new object(), "Allosaurus", (new object(), 1.08, 1.12)), Species(new object(), "Tyrannosaurus", (new object(), 1.08, 1.12)) });

        // InitializeNativeData numbers the species by their place in its list; a null id (no data) keeps the game's.
        var limits = plan.Limits(new[] { "Tyrannosaurus", "Spinosaurus", null, "allosaurus" });

        Assert.Equal(new[] { 0.05f, 5.5f, 0.05f, 2f, 0.05f, 2f, 0.05f, 2f }, limits); // 1.12 x 4 x 1.15 = 5.15, up to 5.5
        Assert.Equal(1, lifts); // the game's cap is lifted once, when the first species needs it
        Assert.Empty(plan.Limits(Array.Empty<string?>()));
    }

    [Fact]
    public void BackOff_warns_once_per_species()
    {
        var plan = Plan(Table("""{ "format": 1, "target": "Allosaurus", "adult": 2.0 }""")); // the game's cap not lifted
        object data = new object(), a = new object(), b = new object();
        var species = new[] { Species(data, "Allosaurus", (a, 1.08, 1.12), (b, 1.0, 1.0)) };

        var first = plan.Next(species);
        var second = plan.Next(species);

        var warning = Assert.Single(first.Warnings);
        Assert.Matches(@"^Allosaurus: size 2\.0 would pass the game's limit; using 1\.\d\d\.$", warning);
        Assert.Empty(second.Warnings);
        // The backed-off adult is what the skins get: their biggest final size stays inside the limit.
        var used = first.Skins.Single(s => s.Skin == a).Male / 1.08;
        Assert.True(used < 2.0 && used > 1);
        var (_, max) = SizeMath.Extremes(first.Curves.Single().Curve, first.Skins.SelectMany(s => new[] { s.Male, s.Female }), SizePlan.InEffect(null, Incubation, Breeding));
        Assert.True(max <= SizeMath.FinalMax);
    }

    [Fact]
    public void Each_species_gets_the_limit_its_sizes_need_up_to_20()
    {
        var table = Table(
            """{ "format": 1, "target": "Allosaurus", "adult": 4.0 }""",
            """{ "format": 1, "target": "Tyrannosaurus", "adult": 20.0 }""");
        object allo = new object(), rex = new object();
        var species = new[] { Species(new object(), "Allosaurus", (allo, 1.08, 1.12)), Species(new object(), "Tyrannosaurus", (rex, 1.08, 1.12)) };

        var pass = new SizePlan(table, Incubation, Breeding, () => true).Next(species);

        // The Allosaurus fits a limit of its own (5.5) and keeps its size; the T. rex passes Tyrant's 20 and is backed off.
        Assert.Equal((1.08 * 4, 1.12 * 4), (pass.Skins.Single(s => s.Skin == allo).Male, pass.Skins.Single(s => s.Skin == allo).Female));
        Assert.Matches(@"^Tyrannosaurus: size 20\.0 would pass its size limit \(0\.05 to 20\); using \d+\.\d\d\.$", Assert.Single(pass.Warnings));

        // When the game's cap could not be lifted, every species keeps to the game's limit.
        var capped = new SizePlan(table, Incubation, Breeding, () => false).Next(species);
        Assert.Equal(2, capped.Warnings.Count);
        Assert.Contains(capped.Warnings, w => w.StartsWith("Allosaurus: size 4.0 would pass the game's limit; using 1.", StringComparison.Ordinal));
    }

    [Fact]
    public void Each_species_backs_off_against_its_own_ranges()
    {
        var table = Table(
            """{ "format": 1, "target": "Allosaurus", "adult": 10, "breeding": [0.5, 2] }""", // 10 x 1.12 x 2 = 22.4
            """{ "format": 1, "target": "Tyrannosaurus", "adult": 10 }""");                  // 10 x 1.12 x 1.15 = 12.9
        object allo = new object(), rex = new object();

        var pass = new SizePlan(table, Incubation, Breeding, () => true)
            .Next(new[] { Species(new object(), "Allosaurus", (allo, 1.08, 1.12)), Species(new object(), "Tyrannosaurus", (rex, 1.08, 1.12)) });

        Assert.StartsWith("Allosaurus: size 10.0 would pass its size limit (0.05 to 20); using 8.", Assert.Single(pass.Warnings));
        Assert.Equal(1.08 * 10, pass.Skins.Single(s => s.Skin == rex).Male, 9);
        Assert.True(pass.Skins.Single(s => s.Skin == allo).Male < 1.08 * 10);
    }

    [Fact]
    public void Ranges_alone_that_pass_the_limit_warn()
    {
        var plan = new SizePlan(Table("""{ "format": 1, "target": "Allosaurus", "nursery": [0.5, 20] }"""), Incubation, Breeding, () => true); // 1.12 x 20: past 20

        var pass = plan.Next(new[] { Species(new object(), "Allosaurus", (new object(), 1.08, 1.12)) });

        Assert.Equal("Allosaurus: its sizes pass its size limit (0.05 to 20) with the size ranges in effect; the game clamps those animals.", Assert.Single(pass.Warnings));
        Assert.Empty(pass.Skins);
        Assert.Empty(pass.Curves);
    }

    [Fact]
    public void Past_a_raised_limit_the_warning_names_it()
    {
        var table = Table("""{ "format": 1, "target": "Allosaurus", "adult": 20, "limit": [0.05, 10] }"""); // the old 10 is ignored

        var pass = new SizePlan(table, Incubation, Breeding, () => true).Next(new[] { Species(new object(), "Allosaurus", (new object(), 1.08, 1.12)) });

        Assert.Matches(@"^Allosaurus: size 20\.0 would pass its size limit \(0\.05 to 20\); using \d+\.\d\d\.$", Assert.Single(pass.Warnings));
    }

    [Fact]
    public void Slopes_are_finite_differences()
    {
        var slopes = SizePlan.Slopes(new[] { (0f, 0f), (0.5f, 1f), (1f, 1.5f) });
        Assert.Equal(new[] { 2f, 1.5f, 1f }, slopes);
    }
}
