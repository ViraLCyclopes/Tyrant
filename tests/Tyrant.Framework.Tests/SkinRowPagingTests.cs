using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class SkinRowPagingTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(5, false)]
    [InlineData(6, true)]
    [InlineData(11, true)]
    public void Arrows_show_only_when_the_row_scrolls(int count, bool expected) => Assert.Equal(expected, SkinRowPaging.Scrolls(count));

    [Theory]
    [InlineData(0, 12, 5)]
    [InlineData(3, 12, 5)] // scrolled part way with the wheel: the next page start
    [InlineData(5, 12, 7)] // the last page ends at the last skin
    [InlineData(7, 12, 7)]
    [InlineData(0, 7, 2)]
    [InlineData(0, 5, 0)]
    public void Next_goes_one_page_on_and_stops_at_the_end(int first, int count, int expected) =>
        Assert.Equal(expected, SkinRowPaging.NextFirst(first, count));

    [Theory]
    [InlineData(7, 12, 5)] // from the end back to the page start before it
    [InlineData(5, 12, 0)]
    [InlineData(3, 12, 0)]
    [InlineData(0, 12, 0)]
    [InlineData(2, 7, 0)]
    public void Previous_goes_one_page_back_and_stops_at_the_start(int first, int count, int expected) =>
        Assert.Equal(expected, SkinRowPaging.PreviousFirst(first, count));

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(89f, 1)] // nearest swatch to the left edge
    [InlineData(100f, 1)]
    [InlineData(160f, 2)]
    [InlineData(10000f, 7)] // never past the last page
    [InlineData(-30f, 0)]
    public void The_first_visible_swatch_comes_from_the_scroll_offset(float offset, int expected) =>
        Assert.Equal(expected, SkinRowPaging.FirstVisible(offset, 80f, 12));

    [Theory]
    [InlineData(0f, 560f, false, true)]
    [InlineData(0.3f, 560f, false, true)] // within half a pixel counts as the end
    [InlineData(200f, 560f, true, true)]
    [InlineData(560f, 560f, true, false)]
    [InlineData(0f, 0f, false, false)]
    public void A_button_greys_out_at_its_end(float offset, float max, bool back, bool forward)
    {
        Assert.Equal(back, SkinRowPaging.CanGoBack(offset));
        Assert.Equal(forward, SkinRowPaging.CanGoForward(offset, max));
    }

    [Fact]
    public void A_page_start_turns_into_an_offset_clamped_to_the_row()
    {
        Assert.Equal(400f, SkinRowPaging.Offset(5, 80f, 1000f));
        Assert.Equal(560f, SkinRowPaging.Offset(7, 80f, 560f));
        Assert.Equal(560f, SkinRowPaging.Offset(9, 80f, 560f));
        Assert.Equal(0f, SkinRowPaging.Offset(0, 80f, 560f));
    }
}
