using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class AnimalFieldEditorTests
{
    private enum Diet { Herbivore, Carnivore }
    private sealed class Animal
    {
        public Brain brain = new();
        public Diet diet = Diet.Carnivore;
        public float[] sizes = [1, 2];
        public Dictionary<string, string> names = new() { ["English"] = "Dinosaur" };
    }
    private sealed class Brain
    {
        public Animal? animalData;
        public Needs needs = new();
        public Config config = new();
    }
    private sealed class Needs { public Brain? Parent; public float hunger = 250; public uint count = 10; }
    private sealed class Config { public float damage = 20; public IntPair groupSize = new() { x = 1, y = 4 }; }
    /// <summary>Like Unity's Vector2Int: numbers in private fields, set through x/y properties.</summary>
    private struct IntPair { private int m_X; private int m_Y; public int x { get => m_X; set => m_X = value; } public int y { get => m_Y; set => m_Y = value; } }
    private static AnimalField Edit(string path, string value, string original, string kind = "number") =>
        new() { Path = path, Kind = kind, Value = value, TemplateValue = original, Editable = true };

    [Fact]
    public void Nested_AI_clones_assets_and_rebinds_parents_without_changing_template()
    {
        var source = new Animal();
        source.brain.animalData = source;
        source.brain.needs.Parent = source.brain;
        var copy = (Animal)AnimalFieldEditor.ShallowClone(source);
        var editor = new AnimalFieldEditor(source, copy, o => o is Brain or Config, AnimalFieldEditor.ShallowClone);
        editor.Apply(copy, [Edit("/brain/needs/hunger", "400", "250"), Edit("/brain/config/damage", "50", "20")]);
        Assert.Equal(400, copy.brain.needs.hunger);
        Assert.Equal(250, source.brain.needs.hunger);
        Assert.Equal(50, copy.brain.config.damage);
        Assert.Equal(20, source.brain.config.damage);
        Assert.Same(copy, copy.brain.animalData);
        Assert.Same(copy.brain, copy.brain.needs.Parent);
    }

    [Fact]
    public void An_integer_pair_like_vector2int_is_set_through_its_properties_and_written_back()
    {
        var source = new Animal();
        var copy = (Animal)AnimalFieldEditor.ShallowClone(source);
        var editor = new AnimalFieldEditor(source, copy, o => o is Brain or Config, AnimalFieldEditor.ShallowClone);
        editor.Apply(copy, [Edit("/brain/config/groupSize/x", "3", "1"), Edit("/brain/config/groupSize/y", "8", "4")]);
        Assert.Equal(3, copy.brain.config.groupSize.x);
        Assert.Equal(8, copy.brain.config.groupSize.y);
        Assert.Equal(1, source.brain.config.groupSize.x);
        Assert.Throws<ManifestException>(() => editor.Apply(copy, [Edit("/brain/config/groupSize/x", "2.5", "1")]));
    }

    [Fact]
    public void Enum_array_dictionary_and_uint_edits_use_actual_field_types()
    {
        var source = new Animal();
        var copy = (Animal)AnimalFieldEditor.ShallowClone(source);
        var editor = new AnimalFieldEditor(source, copy, _ => false, AnimalFieldEditor.ShallowClone);
        editor.Apply(copy, [Edit("/diet", "Herbivore", "Carnivore", "text"), Edit("/sizes/1", "3.5", "2"),
            Edit("/names/English", "Mirabilis", "Dinosaur", "text"), Edit("/brain/needs/count", "4294967295", "10")]);
        Assert.Equal(Diet.Herbivore, copy.diet);
        Assert.Equal(3.5, copy.sizes[1]);
        Assert.Equal(2, source.sizes[1]);
        Assert.Equal("Mirabilis", copy.names["English"]);
        Assert.Equal("Dinosaur", source.names["English"]);
        Assert.Equal(uint.MaxValue, copy.brain.needs.count);
        Assert.Throws<ManifestException>(() => editor.Apply(copy, [Edit("/diet", "Invalid", "Carnivore", "text")]));
        Assert.Throws<ManifestException>(() => editor.Apply(copy, [Edit("/brain/needs/count", "1.2", "10")]));
        Assert.Throws<ManifestException>(() => editor.Apply(copy, [Edit("/sizes/100", "1", "2")]));
    }

    [Fact]
    public void Full_fields_and_localization_keys_roundtrip_and_change_revision()
    {
        var path = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "animals.db");
        var animal = new SpeciesEntry { Id = "mirabilis", Base = "Spinosaurus", Name = "Spinosaurus mirabilis", Genus = "Spinosaurus", Epithet = "mirabilis",
            Fields = [Edit("/brain/needs/hunger", "250", "250")], Localization = new() { ["description"] = "tyrant.pack.mirabilis.description" } };
        AnimalDatabaseFile.Save(path, [animal]);
        var read = AnimalDatabaseFile.Read(path);
        var snapshot = AnimalDatabaseFile.Snapshot(read);
        Assert.Equal("tyrant.pack.mirabilis.description", read[0].Localization["description"]);
        read[0].Fields[0].Value = "400";
        AnimalDatabaseFile.Save(path, read, snapshot);
        Assert.NotEqual(snapshot, AnimalDatabaseFile.Snapshot(AnimalDatabaseFile.Read(path)));
        Assert.Throws<ManifestException>(() => AnimalDatabaseFile.Save(path, [animal], snapshot));
    }

    [Fact]
    public void Language_files_resolve_terms_and_fall_back_to_English()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "localization", "en-US", "mirabilis"));
        Directory.CreateDirectory(Path.Combine(dir, "localization", "fr", "mirabilis"));
        var description = "English description\n\n[Anatomy]\n<b>More text</b>";
        File.WriteAllText(AnimalLocalization.FileFor(dir, "en-US", "mirabilis/name"), "Mirabilis");
        File.WriteAllText(AnimalLocalization.FileFor(dir, "en-US", "mirabilis/description"), description);
        File.WriteAllText(AnimalLocalization.FileFor(dir, "fr", "mirabilis/name"), "Nom français");
        var animal = new SpeciesEntry { Localization = new() { ["name"] = "mirabilis/name", ["description"] = "mirabilis/description" } };
        AnimalLocalization.Load(dir, [animal]);
        Assert.Equal("Nom français", AnimalText.Resolve(animal.Texts, "name", "French"));
        Assert.Equal(description, AnimalText.Resolve(animal.Texts, "description", "French"));
        Assert.DoesNotContain("English description", AnimalDatabaseFile.Snapshot([animal]));
        Assert.Throws<ManifestException>(() => AnimalLocalization.FileFor(dir, "en-US", "../outside"));
    }

    [Fact]
    public void Plain_text_preserves_paragraphs_unicode_markup_and_literal_section_lines()
    {
        var text = "Français 🦕\n\n<b>Paragraph</b>\n[not a key]\n\\literal";
        var source = new Dictionary<string, object?> { ["pack.description"] = text, ["pack.empty"] = "" };
        Assert.Equal(text, AnimalLocalization.Parse(AnimalLocalization.Write(source))["pack.description"]);
        Assert.Equal("", AnimalLocalization.Parse(AnimalLocalization.Write(source))["pack.empty"]);
        Assert.Throws<ManifestException>(() => AnimalLocalization.Parse("[duplicate]\nA\n[duplicate]\nB"));
    }
}
