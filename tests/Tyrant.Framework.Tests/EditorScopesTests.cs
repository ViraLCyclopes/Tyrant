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
    public void The_nursery_offers_next_created_in_a_saved_park_with_or_without_an_id()
    {
        foreach (var preview in new[] { Placed(id: 7), Placed(id: null) }) // a vivarium preview gets a fresh id; a park one none
        {
            Assert.True(EditorScopes.Offers(EditorEntry.Nursery, LookScope.Animal, preview, "Park 1"));
            Assert.Null(EditorScopes.WhyNot(EditorEntry.Nursery, LookScope.Animal, preview, "Park 1"));
            Assert.True(EditorScopes.Offers(EditorEntry.Nursery, LookScope.Skin, preview, "Park 1"));
            Assert.True(EditorScopes.Offers(EditorEntry.Nursery, LookScope.Species, preview, "Park 1"));
            Assert.Equal(LookScope.Skin, EditorScopes.Default(EditorEntry.Nursery, preview, "Park 1")); // the Nursery still starts on the skin
        }
        Assert.Equal(LookScope.Skin, EditorScopes.Preferred(EditorEntry.Nursery));
        Assert.Equal(LookScope.Animal, EditorScopes.Preferred(EditorEntry.Park));
    }

    [Fact]
    public void Next_created_needs_a_saved_park_and_says_so()
    {
        foreach (var save in new[] { null, " " })
        {
            Assert.False(EditorScopes.Offers(EditorEntry.Nursery, LookScope.Animal, Placed(id: null), save));
            Assert.Equal("Save the park once to give a new animal its own look.", EditorScopes.WhyNot(EditorEntry.Nursery, LookScope.Animal, Placed(id: null), save));
        }
    }

    [Fact]
    public void The_animal_scope_is_called_next_created_in_the_nursery()
    {
        Assert.Equal("Next created", EditorScopes.Label(EditorEntry.Nursery, LookScope.Animal));
        Assert.Equal("This animal", EditorScopes.Label(EditorEntry.Park, LookScope.Animal));
        Assert.Equal("Whole skin", EditorScopes.Label(EditorEntry.Nursery, LookScope.Skin));
        Assert.Equal("Whole species", EditorScopes.Label(EditorEntry.Park, LookScope.Species));
        Assert.Equal("Saved for the next Allosaurus you create.", EditorScopes.SavedForNext("Allosaurus"));
    }

    [Fact]
    public void Next_created_treats_another_sex_as_another_look()
    {
        var male = Placed(id: null) with { Sex = "male" };
        Assert.True(EditorScopes.SameLook(male, male with { Sex = "female" })); // the skin and species drafts follow
        Assert.False(EditorScopes.SameLook(male, male with { Sex = "female" }, LookScope.Animal));
        Assert.True(EditorScopes.SameLook(male, male with { Variant = "albino" }, LookScope.Animal));
        Assert.True(EditorScopes.SameLook(male, male with { Sex = "female" }, LookScope.Skin));
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
    public void Closing_the_nursery_keeps_unsaved_changes_and_lets_a_clean_draft_go()
    {
        var kept = EditorScopes.NurseryClosed(dirty: true, out var keep);
        Assert.True(keep);
        Assert.Contains("kept", kept);
        Assert.Contains("Save or Reset", kept);
        var gone = EditorScopes.NurseryClosed(dirty: false, out keep);
        Assert.False(keep);
        Assert.Contains("open the editor", gone);
        Assert.NotEqual(kept, gone);
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
