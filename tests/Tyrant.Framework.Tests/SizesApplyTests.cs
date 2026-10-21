using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class SizesApplyTests
{
    private static readonly (float, float)[] Allosaurus = { (0f, 0.164f), (1f, 1f) };
    private static readonly (double, double) Incubation = (0.925, 1.075);
    private static readonly (double, double) Breeding = (0.85, 1.15);

    private static SizeTable Table(string? sizes, string extra = "")
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "mod");
        Directory.CreateDirectory(Path.Combine(dir, SizeSettings.Folder));
        if (sizes != null) File.WriteAllText(Path.Combine(dir, "sizes", "x.json"), sizes);
        var mod = new LoadedMod(ModManifest.Parse($$"""{"format":1,"id":"size-mod"{{extra}}}"""), dir);
        return SizeTable.Build(new[] { mod }, out _);
    }

    private static SizePlan Plan(SizeTable table) => new SizePlan(table, SizePlan.InEffect(table.Ranges, Incubation, Breeding));

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
        var empty = Table(null);
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
    public void Slider_range_does_not_change_random_eggs()
    {
        var ranges = Table(null, ""","sizeRanges":{"nursery":[0.5,1.5],"breeding":[0.7,1.3]}""").Ranges;

        Assert.Equal(Incubation, SizePlan.RangeFor(SizeSite.RandomEgg, ranges, Incubation, Breeding));
        Assert.Equal((0.5, 1.5), SizePlan.RangeFor(SizeSite.NurserySlider, ranges, Incubation, Breeding));
        Assert.Equal((0.7, 1.3), SizePlan.RangeFor(SizeSite.Breeding, ranges, Incubation, Breeding));
        // Only the Nursery's slider reads are swapped; the random roll lives in VirtualAnimal, not in these methods.
        Assert.Equal(new[] { "GenerateNewAnimal", "UpdateCustomizationRect", "UpdateAnimalPreview" }, SizePlan.SliderMethods);
        Assert.DoesNotContain("GenerateGeneticSize", SizePlan.SliderMethods);
        Assert.DoesNotContain("CreateVirtualAnimal", SizePlan.SliderMethods);
        // The back-off counts every range in effect, the vanilla random one included.
        Assert.Equal(new[] { Incubation, (0.5, 1.5), (0.7, 1.3) }, SizePlan.InEffect(ranges, Incubation, Breeding));

        var none = new SizeRanges();
        Assert.Equal(Incubation, SizePlan.RangeFor(SizeSite.NurserySlider, none, Incubation, Breeding));
        Assert.Equal(Breeding, SizePlan.RangeFor(SizeSite.Breeding, none, Incubation, Breeding));
        Assert.Equal(new[] { Incubation, Breeding }, SizePlan.InEffect(none, Incubation, Breeding));
    }

    [Fact]
    public void BackOff_warns_once_per_species()
    {
        var plan = Plan(Table("""{ "format": 1, "target": "Allosaurus", "adult": 2.0 }"""));
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
        var (_, max) = SizeMath.Extremes(first.Curves.Single().Curve, first.Skins.SelectMany(s => new[] { s.Male, s.Female }), SizePlan.InEffect(new SizeRanges(), Incubation, Breeding));
        Assert.True(max <= SizeMath.FinalMax);
    }

    [Fact]
    public void A_raised_limit_lets_sizes_past_two()
    {
        var table = Table("""{ "format": 1, "target": "Allosaurus", "adult": 4.0 }""", ""","sizeRanges":{"limit":[0.05,20]}""");
        object data = new object(), skin = new object();
        var species = new[] { Species(data, "Allosaurus", (skin, 1.08, 1.12)) };

        var pass = Plan(table).Next(species);

        Assert.Empty(pass.Warnings);
        var sized = Assert.Single(pass.Skins);
        Assert.Equal((1.08 * 4, 1.12 * 4), (sized.Male, sized.Female));

        // When the game's cap could not be lifted, the plan keeps to the game's limit.
        var capped = new SizePlan(table, SizePlan.InEffect(table.Ranges, Incubation, Breeding), SizeLimit.Game).Next(species);
        Assert.Matches(@"^Allosaurus: size 4\.0 would pass the game's limit; using 1\.\d\d\.$", Assert.Single(capped.Warnings));
    }

    [Fact]
    public void Past_a_raised_limit_the_warning_names_it()
    {
        var table = Table("""{ "format": 1, "target": "Allosaurus", "adult": 20 }""", ""","sizeRanges":{"limit":[0.05,10]}""");

        var pass = Plan(table).Next(new[] { Species(new object(), "Allosaurus", (new object(), 1.08, 1.12)) });

        Assert.Matches(@"^Allosaurus: size 20\.0 would pass the size limit \(0\.05 to 10\); using \d\.\d\d\.$", Assert.Single(pass.Warnings));
    }

    [Fact]
    public void Slopes_are_finite_differences()
    {
        var slopes = SizePlan.Slopes(new[] { (0f, 0f), (0.5f, 1f), (1f, 1.5f) });
        Assert.Equal(new[] { 2f, 1.5f, 1f }, slopes);
    }
}
