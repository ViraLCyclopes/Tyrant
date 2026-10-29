using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class SkinPickerOrderTests
{
    [Fact]
    public void Shown_without_a_saved_order_is_starting_then_by_number_then_file_order()
    {
        var shown = SkinPickerOrder.Shown(null, "s", new[] { "a", "b", "c" }, new Dictionary<string, int> { ["a"] = 3, ["c"] = 1 });
        Assert.Equal(new[] { "s", "c", "a", "b" }, shown);
    }

    [Fact]
    public void Shown_keeps_the_saved_order_and_appends_the_rest()
    {
        var shown = SkinPickerOrder.Shown(new[] { "b", "s" }, "s", new[] { "a", "b", "c" }, new Dictionary<string, int> { ["a"] = 2, ["c"] = 1 });
        Assert.Equal(new[] { "b", "s", "c", "a" }, shown);
    }

    [Fact]
    public void Shown_drops_unknown_and_repeated_ids()
    {
        var shown = SkinPickerOrder.Shown(new[] { "x", "a", "a" }, "s", new[] { "a" }, null);
        Assert.Equal(new[] { "a", "s" }, shown);
    }

    [Fact]
    public void Swapped_exchanges_two_places()
    {
        Assert.Equal(new[] { "a", "b", "s" }, SkinPickerOrder.Swapped(new[] { "a", "s", "b" }, "s", "b"));
        Assert.Equal(new[] { "a", "s", "b" }, SkinPickerOrder.Swapped(new[] { "a", "s", "b" }, "s", "zzz"));
    }

    [Fact]
    public void Display_puts_wanted_first_then_the_rest_ascending()
    {
        Assert.Equal(new[] { 3, 0, 1, 2, 4 }, SkinPickerOrder.Display(5, new[] { 3, 0, 9, 3 }));
        Assert.Equal(new[] { 0, 1, 2 }, SkinPickerOrder.Display(3, new int[0]));
    }

    [Fact]
    public void Moves_reverse_sibling_layout_lands_each_skin_in_its_slot()
    {
        var moves = SkinPickerOrder.Moves(new[] { 12, 11, 10 }, new[] { 2, 0, 1 });
        Assert.Equal(new[] { (1, 10), (0, 11), (2, 12) }, moves);
    }

    [Fact]
    public void Moves_stay_inside_the_block_with_other_siblings_around()
    {
        var parent = new List<string> { "old0", "old1", "t2", "t1", "t0", "empty" };
        foreach (var (skin, sibling) in SkinPickerOrder.Moves(new[] { 4, 3, 2 }, new[] { 2, 0, 1 }))
        {
            parent.Remove("t" + skin);
            parent.Insert(sibling, "t" + skin);
        }
        Assert.Equal(new[] { "old0", "old1", "t1", "t0", "t2", "empty" }, parent);
    }
}
