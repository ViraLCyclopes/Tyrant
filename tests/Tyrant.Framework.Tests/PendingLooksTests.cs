using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class PendingLooksTests
{
    private static MaterialOverride Strength(float s) => new() { Iridescence = new IridescenceSettings { Strength = new VariantNumbers { Normal = s } } };

    private static MaterialOverride FromMap() => new() { Iridescence = new IridescenceSettings { Mask = new MaskSource { From = "extra" } } };

    [Fact]
    public void A_look_is_added_replaced_per_material_and_read_back_for_its_exact_key()
    {
        var pending = new PendingLooks();
        Assert.True(pending.IsEmpty);

        pending.Set("Allosaurus", "Allosaurus/Base", "male", "Body", Strength(0.3f));
        pending.Set("Allosaurus", "allosaurus/base", "male", "Body", Strength(0.6f)); // a later save replaces it (skin keys in any case)
        pending.Set("Allosaurus", "Allosaurus/Base", "male", "Eyes", Strength(0.1f));

        Assert.Equal(1, pending.Count);
        var look = pending.Get("Allosaurus", "Allosaurus/Base", "male")!;
        Assert.Equal(0.6f, look["Body"].Iridescence!.Strength!.Normal);
        Assert.Equal(0.1f, look["Eyes"].Iridescence!.Strength!.Normal);
        Assert.Null(pending.Get("Allosaurus", "Allosaurus/Base", "female")); // the draft starts from its own sex only
        Assert.Null(pending.Get("Allosaurus", "Allosaurus/Alt 1", "male"));
    }

    [Fact]
    public void An_empty_block_removes_that_material_and_the_last_one_the_look()
    {
        var pending = new PendingLooks();
        pending.Set("Allosaurus", "Allosaurus/Base", "male", "Body", Strength(0.3f));
        pending.Set("Allosaurus", "Allosaurus/Base", "male", "Body", new MaterialOverride());
        Assert.True(pending.IsEmpty);
        pending.Set("Allosaurus", "Allosaurus/Base", "male", "Body", Strength(0.3f));
        pending.Set("Allosaurus", "Allosaurus/Base", "male", "Body", null);
        Assert.True(pending.IsEmpty);
    }

    [Fact]
    public void Taking_matches_species_skin_and_sex_once()
    {
        var pending = new PendingLooks();
        pending.Set("Allosaurus", "Allosaurus/Base", "male", "Body", Strength(0.3f));
        pending.Set("Allosaurus", "Allosaurus/Base", "female", "Body", Strength(0.8f));

        Assert.Null(pending.Take("Allosaurus", "Allosaurus/Alt 1", "male")); // another skin
        Assert.Null(pending.Take("Ouranosaurus", "Allosaurus/Base", "male")); // another species
        Assert.Equal(0.8f, pending.Take("Allosaurus", "ALLOSAURUS/BASE", "female")!["Body"].Iridescence!.Strength!.Normal);
        Assert.Equal(0.3f, pending.Take("Allosaurus", "Allosaurus/Base", "female")!["Body"].Iridescence!.Strength!.Normal); // the male's: it does not depend on sex
        Assert.Null(pending.Take("Allosaurus", "Allosaurus/Base", "male")); // one animal each, then gone
        Assert.True(pending.IsEmpty);
    }

    [Fact]
    public void Has_tells_whether_take_would_match_without_taking()
    {
        var pending = new PendingLooks();
        Assert.False(pending.Has("Allosaurus", "Allosaurus/Base", "male"));
        pending.Set("Allosaurus", "Allosaurus/Base", "male", "Body", Strength(0.3f));
        Assert.True(pending.Has("Allosaurus", "Allosaurus/Base", "male"));
        Assert.True(pending.Has("Allosaurus", "Allosaurus/Base", "female")); // the sex-independent fallback, as Take
        Assert.False(pending.Has("Ouranosaurus", "Allosaurus/Base", "male"));
        Assert.Equal(1, pending.Count); // nothing taken
        pending.Set("Allosaurus", "Allosaurus/Base", "male", "Body", FromMap());
        Assert.False(pending.Has("Allosaurus", "Allosaurus/Base", "female"));
    }

    [Fact]
    public void A_look_with_a_mask_from_a_sexs_own_map_goes_to_that_sex_only()
    {
        var pending = new PendingLooks();
        pending.Set("Allosaurus", "Allosaurus/Base", "male", "Body", FromMap());

        Assert.True(PendingLooks.DependsOnSex(pending.Get("Allosaurus", "Allosaurus/Base", "male")!));
        Assert.False(PendingLooks.DependsOnSex(new Dictionary<string, MaterialOverride> { ["Body"] = Strength(1) }));
        Assert.Null(pending.Take("Allosaurus", "Allosaurus/Base", "female"));
        Assert.NotNull(pending.Take("Allosaurus", "Allosaurus/Base", "male"));
    }

    [Fact]
    public void A_mod_skin_and_a_species_without_a_skin_key_are_matched_too_and_clear_drops_everything()
    {
        var pending = new PendingLooks();
        pending.Set("Allosaurus", "red-spot/blue", "male", "Body", Strength(0.3f));
        pending.Set("Allosaurus", null, "male", "Body", Strength(0.5f));

        Assert.Null(pending.Take("Allosaurus", "Allosaurus/Base", "male"));
        Assert.Equal(0.5f, pending.Take("Allosaurus", null, "male")!["Body"].Iridescence!.Strength!.Normal);
        pending.Clear();
        Assert.True(pending.IsEmpty);
        Assert.Null(pending.Take("Allosaurus", "red-spot/blue", "male"));
    }

    [Fact]
    public void The_store_hands_out_copies()
    {
        var pending = new PendingLooks();
        var block = Strength(0.3f);
        pending.Set("Allosaurus", "Allosaurus/Base", "male", "Body", block);
        block.Iridescence!.Strength!.Normal = 0.9f; // the draft goes on changing after Save
        var look = pending.Get("Allosaurus", "Allosaurus/Base", "male")!;
        Assert.Equal(0.3f, look["Body"].Iridescence!.Strength!.Normal);
        look["Body"].Iridescence!.Strength!.Normal = 0.1f;
        Assert.Equal(0.3f, pending.Take("Allosaurus", "Allosaurus/Base", "male")!["Body"].Iridescence!.Strength!.Normal);
    }
}
