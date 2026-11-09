using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class MaterialSourcePoolTests
{
    private static MaterialOverride Block(RampSource? ramp = null, MaskSource? mask = null, MaskSource? fur = null) => new()
    {
        Iridescence = ramp == null && mask == null ? null : new IridescenceSettings { Ramp = ramp, Mask = mask },
        Fur = fur == null ? null : new FurMasks { Mask = fur },
    };

    private static RampSource Film(double nm) => new() { ThinFilm = new ThinFilmSettings { Thickness = nm } };

    [Fact]
    public void Equal_sources_become_one_object()
    {
        var pool = new MaterialSourcePool(_ => "1");
        var a = Block(Film(420), new MaskSource { From = "extra", Channel = 'g' }, new MaskSource { File = "C:/m/fur.png" });
        var b = Block(Film(420), new MaskSource { From = "extra", Channel = 'g' }, new MaskSource { File = "C:/m/fur.png" });
        var c = Block(Film(430), new MaskSource { From = "extra", Channel = 'g', Invert = true }, new MaskSource { File = "C:/m/fur.png" });

        pool.Intern(a);
        pool.Intern(b);
        pool.Intern(c);

        Assert.Same(a.Iridescence!.Ramp, b.Iridescence!.Ramp);
        Assert.Same(a.Iridescence.Mask, b.Iridescence.Mask);
        Assert.Same(a.Fur!.Mask, b.Fur!.Mask);
        Assert.Same(a.Fur.Mask, c.Fur!.Mask);
        Assert.NotSame(a.Iridescence.Ramp, c.Iridescence!.Ramp);
        Assert.NotSame(a.Iridescence.Mask, c.Iridescence.Mask);
        Assert.True(pool.Contains(a.Iridescence.Ramp!));
    }

    [Fact]
    public void A_game_ramp_or_mask_is_its_own_source()
    {
        var pool = new MaterialSourcePool(_ => "1");
        var game = Block(new RampSource { Game = true }, new MaskSource { Game = true });
        var again = Block(new RampSource { Game = true }, new MaskSource { Game = true });
        var other = Block(new RampSource { Stops = [] }, new MaskSource { From = "extra" });
        pool.Intern(game);
        pool.Intern(again);
        pool.Intern(other);

        Assert.Same(game.Iridescence!.Ramp, again.Iridescence!.Ramp);
        Assert.NotSame(game.Iridescence.Ramp, other.Iridescence!.Ramp);
        Assert.NotSame(game.Iridescence.Mask, other.Iridescence.Mask);
        Assert.True(game.Iridescence.Ramp!.Game);
    }

    [Fact]
    public void A_species_ramp_or_mask_is_one_source_per_species()
    {
        var pool = new MaterialSourcePool(_ => "1");
        var micro = Block(new RampSource { Species = "Microraptor" }, new MaskSource { Species = "Microraptor" });
        var again = Block(new RampSource { Species = "Microraptor" }, new MaskSource { Species = "Microraptor" });
        var other = Block(new RampSource { Species = "Oviraptor" }, new MaskSource { Species = "Oviraptor" });
        var game = Block(new RampSource { Game = true }, new MaskSource { Game = true });
        foreach (var block in new[] { micro, again, other, game }) pool.Intern(block);

        Assert.Same(micro.Iridescence!.Ramp, again.Iridescence!.Ramp);
        Assert.Same(micro.Iridescence.Mask, again.Iridescence.Mask);
        Assert.NotSame(micro.Iridescence.Ramp, other.Iridescence!.Ramp);
        Assert.NotSame(micro.Iridescence.Ramp, game.Iridescence!.Ramp);
        Assert.NotSame(micro.Iridescence.Mask, game.Iridescence.Mask);
    }

    [Fact]
    public void A_file_whose_stamp_changed_is_a_new_source()
    {
        var stamp = "t1";
        var now = 0.0;
        var pool = new MaterialSourcePool(_ => stamp, () => now, MaterialSourcePool.StampSeconds);
        var before = Block(new RampSource { File = "C:/m/r.png" }, new MaskSource { File = "C:/m/m.png" });
        pool.Intern(before);
        stamp = "t2";
        now = 1.0; // the stamps read before are a second old: read again
        var after = Block(new RampSource { File = "C:/m/r.png" }, new MaskSource { File = "C:/m/m.png" });
        pool.Intern(after);

        Assert.NotSame(before.Iridescence!.Ramp, after.Iridescence!.Ramp);
        Assert.NotSame(before.Iridescence.Mask, after.Iridescence.Mask);
    }

    [Fact]
    public void A_file_stamp_is_read_once_a_second_however_often_it_is_interned()
    {
        var reads = new List<string>();
        var stamp = "t1";
        var now = 10.0;
        var pool = new MaterialSourcePool(file =>
        {
            reads.Add(file);
            return stamp;
        }, () => now, 1);
        var first = Block(new RampSource { File = "C:/m/r.png" }, new MaskSource { File = "C:/m/m.png" }, new MaskSource { File = "C:/m/m.png" });
        pool.Intern(first);
        Assert.Equal(2, reads.Count); // each file once, though the mask is named twice

        stamp = "t2"; // saved again on disk: not seen until the stamps are a second old
        for (var tick = 0; tick < 50; tick++)
        {
            now += 0.016;
            var drag = Block(new RampSource { File = "C:/m/r.png" }, new MaskSource { File = "C:/m/m.png" });
            pool.Intern(drag);
            Assert.Same(first.Iridescence!.Ramp, drag.Iridescence!.Ramp);
        }
        Assert.Equal(2, reads.Count);

        now = 11.0;
        var later = Block(new RampSource { File = "C:/m/r.png" }, new MaskSource { File = "C:/m/m.png" });
        pool.Intern(later);
        Assert.Equal(4, reads.Count);
        Assert.NotSame(first.Iridescence!.Ramp, later.Iridescence!.Ramp);
        Assert.NotSame(first.Iridescence.Mask, later.Iridescence!.Mask);
    }

    [Fact]
    public void Forgotten_stamps_are_read_again_at_once()
    {
        var stamp = "t1";
        var pool = new MaterialSourcePool(_ => stamp, () => 5.0, 1);
        var before = Block(new RampSource { File = "C:/m/r.png" });
        pool.Intern(before);
        stamp = "t2";
        pool.ForgetStamps();
        var after = Block(new RampSource { File = "C:/m/r.png" });
        pool.Intern(after);

        Assert.NotSame(before.Iridescence!.Ramp, after.Iridescence!.Ramp);
    }

    [Fact]
    public void A_clock_that_goes_back_reads_the_stamp_again()
    {
        var reads = 0;
        var now = 5.0;
        var pool = new MaterialSourcePool(_ => (++reads).ToString(), () => now, 1);
        pool.Intern(Block(new RampSource { File = "C:/m/r.png" }));
        now = 4.5;
        pool.Intern(Block(new RampSource { File = "C:/m/r.png" }));

        Assert.Equal(2, reads);
    }

    [Fact]
    public void Retain_keeps_fresh_stamps_and_drops_old_ones()
    {
        var reads = 0;
        var now = 0.0;
        var pool = new MaterialSourcePool(_ => { reads++; return "t"; }, () => now, 1);
        var block = Block(new RampSource { File = "C:/m/r.png" });
        pool.Intern(block);
        now = 0.5;
        pool.Retain([block]);
        pool.Intern(Block(new RampSource { File = "C:/m/r.png" }));
        Assert.Equal(1, reads);

        now = 2;
        pool.Retain([block]);
        pool.Intern(Block(new RampSource { File = "C:/m/r.png" }));
        Assert.Equal(2, reads);
    }

    [Fact]
    public void Everywhere_and_stops_compare_by_content()
    {
        var pool = new MaterialSourcePool(_ => "1");
        var a = Block(new RampSource { Stops = [new Rgb(1, 0, 0), new Rgb(0, 0, 1)] }, new MaskSource { Everywhere = true });
        var b = Block(new RampSource { Stops = [new Rgb(1, 0, 0), new Rgb(0, 0, 1)] }, new MaskSource { Everywhere = true });
        var c = Block(new RampSource { Stops = [new Rgb(1, 0, 0), new Rgb(0, 1, 1)] });
        pool.Intern(a);
        pool.Intern(b);
        pool.Intern(c);

        Assert.Same(a.Iridescence!.Ramp, b.Iridescence!.Ramp);
        Assert.Same(a.Iridescence.Mask, b.Iridescence.Mask);
        Assert.NotSame(a.Iridescence.Ramp, c.Iridescence!.Ramp);
    }

    [Fact]
    public void Retain_forgets_the_sources_no_block_uses()
    {
        var pool = new MaterialSourcePool(_ => "1");
        var kept = Block(Film(420));
        var dropped = Block(Film(500), new MaskSource { File = "C:/m/m.png" });
        pool.Intern(kept);
        pool.Intern(dropped);

        var forgotten = pool.Retain([kept]);

        Assert.Equal(2, forgotten);
        Assert.True(pool.Contains(kept.Iridescence!.Ramp!));
        Assert.False(pool.Contains(dropped.Iridescence!.Ramp!));
        Assert.False(pool.Contains(dropped.Iridescence.Mask!));
        var again = Block(Film(500));
        pool.Intern(again);
        Assert.NotSame(dropped.Iridescence.Ramp, again.Iridescence!.Ramp); // a forgotten source is never handed out again
    }
}
