using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class SkinNumbersTests
{
    private const string Carch = "Carcharodontosaurus";

    [Fact]
    public void New_skins_get_numbers_from_15_in_order()
    {
        var numbers = new SkinNumbers();

        var changed = numbers.Assign(Carch, 3, ["a/red", "b/blue", "b/tiger"], []);

        Assert.True(changed);
        Assert.Equal(SkinNumbers.FirstNumber, 15);
        Assert.Equal(new Dictionary<string, int> { ["a/red"] = 15, ["b/blue"] = 16, ["b/tiger"] = 17 }, numbers.Of(Carch));
    }

    [Fact]
    public void A_species_with_15_or_more_vanilla_skins_starts_after_them()
    {
        var numbers = new SkinNumbers();

        numbers.Assign(Carch, 18, ["a/red"], []);

        Assert.Equal(18, numbers.Of(Carch)["a/red"]);
    }

    [Fact]
    public void Numbers_never_change_when_mods_are_removed_or_reordered()
    {
        var numbers = new SkinNumbers();
        numbers.Assign(Carch, 3, ["a/red", "b/blue"], []);

        var changed = numbers.Assign(Carch, 3, ["c/new", "b/blue"], []); // a/red's mod removed, c added, order changed

        Assert.True(changed);
        Assert.Equal(new Dictionary<string, int> { ["a/red"] = 15, ["b/blue"] = 16, ["c/new"] = 17 }, numbers.Of(Carch));
        Assert.False(numbers.Assign(Carch, 3, ["b/blue", "c/new"], [])); // nothing new: nothing changes
    }

    [Fact]
    public void Removed_skins_leave_stand_ins_so_later_numbers_stay()
    {
        var numbers = new SkinNumbers();
        numbers.Assign(Carch, 3, ["a/red", "b/blue"], []);

        var layout = SkinLayout.For(numbers.Of(Carch), 3);

        Assert.Equal(Enumerable.Range(3, 14), layout.Select(e => e.Number)); // 3..16, no gaps
        Assert.All(layout.Take(12), e => Assert.Null(e.Key)); // 3..14: hidden stand-ins before the first added skin
        Assert.Equal(new[] { (15, "a/red"), (16, "b/blue") }, layout.Skip(12).Select(e => (e.Number, e.Key!)));
        Assert.Empty(SkinLayout.For(new Dictionary<string, int>(), 3));
    }

    [Fact]
    public void Gaps_are_filled_with_stand_ins()
    {
        var layout = SkinLayout.For(new Dictionary<string, int> { ["b/blue"] = 5 }, 3);

        Assert.Equal(new (int, string?)[] { (3, null), (4, null), (5, "b/blue") }, layout.Select(e => (e.Number, e.Key)));
    }

    [Fact]
    public void Forgotten_numbers_are_reused_first()
    {
        var numbers = new SkinNumbers();
        numbers.Assign(Carch, 3, ["a/red", "b/blue"], []);

        Assert.True(numbers.Forget(Carch, "a/red"));
        numbers.Assign(Carch, 3, ["c/new"], []);

        Assert.Equal(15, numbers.Of(Carch)["c/new"]);
        Assert.False(numbers.Forget(Carch, "nope/none"));
    }

    [Fact]
    public void A_grown_vanilla_count_moves_clashing_skins_and_says_so()
    {
        var numbers = new SkinNumbers();
        numbers.Assign(Carch, 3, ["a/red", "b/blue"], []); // 15, 16
        var messages = new List<string>();

        numbers.Assign(Carch, 16, ["a/red", "b/blue"], messages); // a game update grew the vanilla list to 16: number 15 is vanilla now

        Assert.Equal(new Dictionary<string, int> { ["a/red"] = 17, ["b/blue"] = 16 }, numbers.Of(Carch));
        Assert.Contains("a/red", Assert.Single(messages));
    }

    [Fact]
    public void The_file_round_trips_and_bad_content_is_refused()
    {
        var numbers = new SkinNumbers();
        numbers.Assign(Carch, 3, ["a/red"], []);
        numbers.Assign("Stegosaurus", 4, ["b/blue"], []);

        var back = SkinNumbers.Parse(numbers.ToJson());

        Assert.Equal(15, back.Of(Carch)["a/red"]);
        Assert.Equal(15, back.Of("Stegosaurus")["b/blue"]);
        Assert.Empty(SkinNumbers.Parse(null).Species);
        Assert.Throws<FormatException>(() => SkinNumbers.Parse("""{ "format": 1, "species": [] }"""));
    }
}
