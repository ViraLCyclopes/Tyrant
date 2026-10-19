using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class LiveLooksTests
{
    private static MaterialOverride Smooth(float value) => new() { Settings = { ["_Smoothness"] = MaterialValue.OfNumber(value) } };

    [Fact]
    public void Live_layers_are_kept_per_scope_key_and_material()
    {
        var live = new LiveLooks();
        Assert.True(live.IsEmpty);
        live.Set(LookScope.Animal, "42", "Titanoboa", Smooth(0.1f));
        live.Set(LookScope.Skin, "mod-a/rainbow", "Titanoboa", Smooth(0.2f));
        live.Set(LookScope.Species, "Titanoboa", "Titanoboa", Smooth(0.3f));

        Assert.False(live.IsEmpty);
        Assert.Equal(0.1f, live.Animal(42, "Titanoboa")!.Settings["_Smoothness"].Number);
        Assert.Equal(0.2f, live.Skin("mod-a/rainbow", "Titanoboa")!.Settings["_Smoothness"].Number);
        Assert.Equal(0.3f, live.Species("Titanoboa", "Titanoboa")!.Settings["_Smoothness"].Number);
        Assert.Null(live.Animal(43, "Titanoboa"));
        Assert.Null(live.Skin("mod-a/rainbow", "Eyes"));
        Assert.Null(live.Skin(null, "Titanoboa"));
        Assert.Null(live.Species("Other", "Titanoboa"));
        Assert.True(live.HasAnimals && live.HasSkins && live.HasSpecies);
        Assert.Equal(3, live.Blocks.Count());
    }

    [Fact]
    public void Null_or_an_empty_block_clears_and_animal_keys_must_be_ids()
    {
        var live = new LiveLooks();
        live.Set(LookScope.Animal, "42", "Titanoboa", Smooth(0.1f));
        live.Set(LookScope.Species, "Titanoboa", "Titanoboa", Smooth(0.3f));

        live.Set(LookScope.Animal, "42", "Titanoboa", null);
        live.Set(LookScope.Species, "Titanoboa", "Titanoboa", new MaterialOverride());

        Assert.Null(live.Animal(42, "Titanoboa"));
        Assert.Null(live.Species("Titanoboa", "Titanoboa"));
        Assert.False(live.HasAnimals);
        Assert.False(live.HasSpecies);
        Assert.True(live.IsEmpty);
        Assert.Throws<ArgumentException>(() => live.Set(LookScope.Animal, "rex", "Titanoboa", Smooth(1)));
    }

    [Fact]
    public void Vanilla_skin_keys_match_in_any_case_and_clear_removes_everything()
    {
        var live = new LiveLooks();
        live.Set(LookScope.Skin, "Titanoboa/Skin A", "Titanoboa", Smooth(0.2f));
        Assert.NotNull(live.Skin("titanoboa/skin a", "Titanoboa"));

        live.Clear();
        Assert.True(live.IsEmpty);
        Assert.Empty(live.Blocks);
    }

    [Fact]
    public void The_context_names_the_key_of_each_scope()
    {
        var vanilla = new AnimalContext("Titanoboa", null, "Titanoboa/Skin A", "albino", 42, "Titanoboa", "vanilla", null);
        var mod = new AnimalContext("Titanoboa", ("mod-a", "rainbow"), null, "normal", null, "Titanoboa", "Rainbow Mod", null);

        Assert.Equal("42", vanilla.KeyFor(LookScope.Animal));
        Assert.Equal("Titanoboa/Skin A", vanilla.KeyFor(LookScope.Skin));
        Assert.Equal("Titanoboa", vanilla.KeyFor(LookScope.Species));
        Assert.Null(mod.KeyFor(LookScope.Animal)); // no persistent id: no look of its own
        Assert.Equal("mod-a/rainbow", mod.KeyFor(LookScope.Skin));
        Assert.Equal("mod-a/rainbow", mod.SkinKey);
    }
}
