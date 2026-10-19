using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class EditorScopesTests
{
    private static AnimalContext Placed(ulong? id = 42, string? vanillaSkin = "Titanoboa/Skin A") =>
        new("Titanoboa", null, vanillaSkin, "normal", id, "Titanoboa", "vanilla", null);

    [Fact]
    public void A_placed_animal_in_a_saved_park_offers_every_scope_and_starts_on_this_animal()
    {
        var animal = Placed();
        foreach (var scope in new[] { LookScope.Animal, LookScope.Skin, LookScope.Species })
            Assert.True(EditorScopes.Offers(EditorEntry.Park, scope, animal, "Park 1"));
        Assert.Equal(LookScope.Animal, EditorScopes.Default(EditorEntry.Park, animal, "Park 1"));
        Assert.Null(EditorScopes.WhyNot(EditorEntry.Park, LookScope.Animal, animal, "Park 1"));
    }

    [Fact]
    public void The_nursery_never_offers_this_animal_even_when_its_preview_has_an_id()
    {
        var preview = Placed(id: 7); // a vivarium preview gets a fresh id
        Assert.False(EditorScopes.Offers(EditorEntry.Nursery, LookScope.Animal, preview, "Park 1"));
        Assert.True(EditorScopes.Offers(EditorEntry.Nursery, LookScope.Skin, preview, "Park 1"));
        Assert.True(EditorScopes.Offers(EditorEntry.Nursery, LookScope.Species, preview, "Park 1"));
        Assert.Equal(LookScope.Skin, EditorScopes.Preferred(EditorEntry.Nursery));
        Assert.Equal(LookScope.Animal, EditorScopes.Preferred(EditorEntry.Park));
        Assert.Equal(LookScope.Skin, EditorScopes.Default(EditorEntry.Nursery, preview, "Park 1"));
        Assert.Equal("Place the animal in the park to give it its own look.", EditorScopes.WhyNot(EditorEntry.Nursery, LookScope.Animal, preview, "Park 1"));
    }

    [Fact]
    public void Without_a_saved_park_or_an_id_a_placed_animal_starts_on_its_skin_and_says_why()
    {
        Assert.False(EditorScopes.Offers(EditorEntry.Park, LookScope.Animal, Placed(), null));
        Assert.Equal(LookScope.Skin, EditorScopes.Default(EditorEntry.Park, Placed(), null));
        Assert.Contains("Save the park", EditorScopes.WhyNot(EditorEntry.Park, LookScope.Animal, Placed(), " "));
        Assert.False(EditorScopes.Offers(EditorEntry.Park, LookScope.Animal, Placed(id: null), "Park 1"));
        Assert.Contains("no id", EditorScopes.WhyNot(EditorEntry.Park, LookScope.Animal, Placed(id: null), "Park 1"));
    }

    [Fact]
    public void An_animal_without_a_known_skin_falls_back_to_its_species()
    {
        var unknown = Placed(id: null, vanillaSkin: null);
        Assert.False(EditorScopes.Offers(EditorEntry.Nursery, LookScope.Skin, unknown, "Park 1"));
        Assert.NotNull(EditorScopes.WhyNot(EditorEntry.Nursery, LookScope.Skin, unknown, "Park 1"));
        Assert.Equal(LookScope.Species, EditorScopes.Default(EditorEntry.Nursery, unknown, "Park 1"));
        Assert.Equal(LookScope.Species, EditorScopes.Default(EditorEntry.Park, unknown, "Park 1"));
        Assert.Null(EditorScopes.WhyNot(EditorEntry.Park, LookScope.Species, unknown, "Park 1"));
    }

    [Fact]
    public void Nothing_is_offered_without_an_animal()
    {
        foreach (var scope in new[] { LookScope.Animal, LookScope.Skin, LookScope.Species })
            Assert.False(EditorScopes.Offers(EditorEntry.Park, scope, null, "Park 1"));
        Assert.Equal(LookScope.Species, EditorScopes.Default(EditorEntry.Park, null, "Park 1"));
    }

    [Fact]
    public void The_same_look_ignores_the_id_variant_and_skin_key_case_but_not_skin_species_or_material()
    {
        var a = Placed(id: 1);
        Assert.True(EditorScopes.SameLook(a, Placed(id: 2) with { VanillaSkinKey = "titanoboa/skin a" }));
        Assert.True(EditorScopes.SameLook(a, a with { Source = "other", CustomSpeciesModId = "x" }));
        Assert.True(EditorScopes.SameLook(a, a with { Variant = "albino" })); // a new mutation keeps the skin's draft
        Assert.False(EditorScopes.SameLook(a, a with { VanillaSkinKey = "Titanoboa/Skin B" }));
        Assert.False(EditorScopes.SameLook(a, a with { VanillaSkinKey = null, ModSkin = ("mod-a", "Skin A") }));
        Assert.False(EditorScopes.SameLook(a, a with { Species = "Microraptor" }));
        Assert.False(EditorScopes.SameLook(a, a with { MaterialName = "Titanoboa_Juvenile" }));
        Assert.False(EditorScopes.SameLook(a, null));
        Assert.False(EditorScopes.SameLook(null, a));
    }
}
