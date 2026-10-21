using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class InfoTogglesTests
{
    [Fact]
    public void Nothing_is_open_at_first()
    {
        var infos = new InfoToggles();
        Assert.False(infos.IsOpen("iridescence", "mask"));
        Assert.Null(infos.Open("iridescence"));
    }

    [Fact]
    public void Pressing_opens_and_pressing_again_closes()
    {
        var infos = new InfoToggles();
        infos.Toggle("iridescence", "mask");
        Assert.True(infos.IsOpen("iridescence", "mask"));
        infos.Toggle("iridescence", "mask");
        Assert.False(infos.IsOpen("iridescence", "mask"));
        Assert.Null(infos.Open("iridescence"));
    }

    [Fact]
    public void Only_one_is_open_per_tab()
    {
        var infos = new InfoToggles();
        infos.Toggle("iridescence", "mask");
        infos.Toggle("iridescence", "variation");
        Assert.False(infos.IsOpen("iridescence", "mask"));
        Assert.True(infos.IsOpen("iridescence", "variation"));
        Assert.Equal("variation", infos.Open("iridescence"));
    }

    [Fact]
    public void Each_tab_keeps_its_own()
    {
        var infos = new InfoToggles();
        infos.Toggle("iridescence", "mask");
        infos.Toggle("fur", "length");
        Assert.True(infos.IsOpen("iridescence", "mask"));
        Assert.True(infos.IsOpen("fur", "length"));
        Assert.False(infos.IsOpen("fur", "mask")); // the same row name on another tab is another row
    }
}
