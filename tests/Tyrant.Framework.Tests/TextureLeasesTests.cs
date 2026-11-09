using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class TextureLeasesTests
{
    private const double Grace = 60;

    [Fact]
    public void A_texture_an_animal_wears_is_never_collected()
    {
        var leases = new TextureLeases();
        leases.Touch("a", 0);
        leases.Hold(1, ["a"], 0);

        Assert.Empty(leases.Collect(10_000, Grace));
    }

    [Fact]
    public void A_texture_is_collected_only_after_the_grace_time_since_its_last_holder_let_go()
    {
        var leases = new TextureLeases();
        leases.Hold(1, ["a", "b"], 0);
        leases.Hold(2, ["a"], 0);

        leases.Release(1, 100);
        Assert.Empty(leases.Collect(100 + Grace - 1, Grace));
        Assert.Equal(["b"], leases.Collect(100 + Grace, Grace)); // "a" is still worn by 2

        leases.Release(2, 500);
        Assert.Equal(["a"], leases.Collect(500 + Grace, Grace));
        Assert.Empty(leases.Collect(10_000, Grace)); // forgotten once handed back
    }

    [Fact]
    public void Changing_skin_lets_go_of_the_old_textures_and_keeps_the_shared_ones()
    {
        var leases = new TextureLeases();
        leases.Hold(1, ["old-diffuse", "shared-normal"], 0);

        leases.Hold(1, ["new-diffuse", "shared-normal"], 10);

        Assert.Equal(["old-diffuse"], leases.Collect(10 + Grace, Grace));
        Assert.Empty(leases.Collect(10_000, Grace));
    }

    [Fact]
    public void Wearing_nothing_releases_the_user()
    {
        var leases = new TextureLeases();
        leases.Hold(1, ["a"], 0);

        leases.Hold(1, [], 5);

        Assert.Empty(leases.Users);
        Assert.Equal(["a"], leases.Collect(5 + Grace, Grace));
    }

    [Fact]
    public void A_texture_asked_for_again_gets_a_new_grace_time()
    {
        var leases = new TextureLeases();
        leases.Touch("mask-source", 0);

        leases.Touch("mask-source", 50);

        Assert.Empty(leases.Collect(60, Grace));
        Assert.Equal(["mask-source"], leases.Collect(50 + Grace, Grace));
    }

    [Fact]
    public void A_pinned_texture_is_never_collected()
    {
        var leases = new TextureLeases();
        leases.Touch("fence", 0);
        leases.Pin("fence");

        Assert.Empty(leases.Collect(10_000, Grace));
    }

    [Fact]
    public void Holding_the_same_texture_twice_counts_once()
    {
        var leases = new TextureLeases();
        leases.Hold(1, ["a"], 0);
        leases.Hold(1, ["a"], 1);

        leases.Release(1, 2);

        Assert.Equal(["a"], leases.Collect(2 + Grace, Grace));
    }

    [Fact]
    public void Keys_ignore_case_like_windows_paths()
    {
        var leases = new TextureLeases();
        leases.Hold(1, [@"C:\Mods\a.png|Color"], 0);
        leases.Touch(@"c:\mods\A.PNG|Color", 0);

        Assert.Empty(leases.Collect(10_000, Grace));
    }
}
