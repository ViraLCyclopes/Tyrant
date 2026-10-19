using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class PreparedLooksTests
{
    private static Dictionary<string, MaterialOverride> Look(string mask) => new()
    {
        ["Body"] = new MaterialOverride { Iridescence = new IridescenceSettings { Mask = new MaskSource { File = mask } } },
    };

    private static (PreparedLooks Looks, MaterialSourcePool Pool) Make()
    {
        var pool = new MaterialSourcePool(_ => "1");
        return (new PreparedLooks(block =>
        {
            var copy = MaterialTable.WithFullPaths(block, Path.GetTempPath());
            pool.Intern(copy);
            return copy;
        }), pool);
    }

    [Fact]
    public void Saving_one_animal_keeps_every_other_animals_prepared_look_and_its_sources()
    {
        var (prepared, pool) = Make();
        var store = new AnimalLooks();
        store.Set(1, Look("one.png"));
        store.Set(2, Look("two.png"));
        prepared.Use(store);
        var two = prepared.Get(2)!["Body"];

        store.Set(1, Look("one-new.png")); // the editor saves animal 1
        prepared.Use(store);
        pool.Retain(prepared.Blocks); // the sweep, before animal 2 is written again

        Assert.Same(two, prepared.Get(2)!["Body"]);
        Assert.True(pool.Contains(two.Iridescence!.Mask!));
        Assert.EndsWith("one-new.png", prepared.Get(1)!["Body"].Iridescence!.Mask!.File);
    }

    [Fact]
    public void A_new_store_is_prepared_at_once_so_unchanged_images_keep_their_pooled_sources()
    {
        var (prepared, pool) = Make();
        var first = new AnimalLooks();
        first.Set(2, Look("two.png"));
        prepared.Use(first);
        var before = prepared.Get(2)!["Body"].Iridescence!.Mask!;

        var reread = new AnimalLooks(); // the park's file read again: new objects, same content
        reread.Set(2, Look("two.png"));
        reread.Set(3, Look("three.png"));
        prepared.Use(reread);
        pool.Retain(prepared.Blocks);

        Assert.Same(before, prepared.Get(2)!["Body"].Iridescence!.Mask);
        Assert.True(pool.Contains(before));
        Assert.Equal(2, prepared.Blocks.Count());
    }

    [Fact]
    public void Animals_without_a_look_and_no_store_give_nothing()
    {
        var (prepared, _) = Make();
        var store = new AnimalLooks();
        store.Set(1, Look("one.png"));
        prepared.Use(store);

        Assert.False(prepared.IsEmpty);
        Assert.Null(prepared.Get(9));
        Assert.True(prepared.Has(1));
        Assert.False(prepared.Has(9));
        prepared.Use(null);
        Assert.Null(prepared.Get(1));
        Assert.False(prepared.Has(1));
        Assert.True(prepared.IsEmpty);
        Assert.Empty(prepared.Blocks);
    }

    [Fact]
    public void A_look_changed_in_the_store_without_use_is_prepared_again_on_get()
    {
        var (prepared, _) = Make();
        var store = new AnimalLooks();
        store.Set(1, Look("one.png"));
        prepared.Use(store);

        store.Set(1, Look("other.png"));

        Assert.EndsWith("other.png", prepared.Get(1)!["Body"].Iridescence!.Mask!.File);
    }
}
