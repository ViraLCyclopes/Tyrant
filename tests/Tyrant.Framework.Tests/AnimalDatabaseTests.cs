using System.Text;
using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class AnimalDatabaseTests
{
    [Fact]
    public void Prefab_reference_roundtrips_and_changes_the_database_revision()
    {
        var path = PathToDatabase();
        var source = Animal();
        AnimalDatabaseFile.Save(path, [source]);
        var previous = AnimalDatabaseFile.Snapshot([source]);
        source.Prefab = new SpeciesPrefab { Bundle = "prefabs/animal.bundle", Asset = "assets/animals/animal.prefab" };
        AnimalDatabaseFile.Save(path, [source], previous);
        var saved = Assert.Single(AnimalDatabaseFile.Read(path));
        Assert.Equal(source.Prefab.Bundle, saved.Prefab!.Bundle);
        Assert.Equal(source.Prefab.Asset, saved.Prefab.Asset);
        Assert.NotEqual(previous, AnimalDatabaseFile.Snapshot([saved]));
        saved.Prefab = null;
        AnimalDatabaseFile.Save(path, [saved]);
        Assert.Null(Assert.Single(AnimalDatabaseFile.Read(path)).Prefab);
    }

    [Theory]
    [InlineData("../animal.bundle", "assets/animal.prefab")]
    [InlineData("C:/animal.bundle", "assets/animal.prefab")]
    [InlineData("animal.fbx", "assets/animal.prefab")]
    [InlineData("animal.bundle", "Assets/animal.prefab")]
    [InlineData("animal.bundle", "assets/../animal.prefab")]
    public void Unsafe_or_ambiguous_prefab_references_are_rejected(string bundle, string asset)
    {
        var animal = Animal();
        animal.Prefab = new SpeciesPrefab { Bundle = bundle, Asset = asset };
        Assert.Throws<ManifestException>(() => AnimalDatabaseFile.Save(PathToDatabase(), [animal]));
    }

    private static string PathToDatabase() => Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), AnimalDatabaseFile.FileName);
    private static SpeciesEntry Animal() => new()
    {
        Id = "new-animal", Base = "Allosaurus Anax", Name = "Dinosaur's café 🦖", Genus = "Example", Epithet = "animal", Cost = 1234,
        Stats = new() { ["adultMassKg"] = 4500, ["maxAgeYears"] = 45 },
    };

    [Fact]
    public void Stores_real_sqlite_and_roundtrips_unicode_and_nullable_inheritance()
    {
        var path = PathToDatabase();
        var source = Animal();
        AnimalDatabaseFile.Save(path, [source]);
        Assert.Equal("SQLite format 3\0", Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 16));
        var saved = Assert.Single(AnimalDatabaseFile.Read(path));
        Assert.Equal(source.Name, saved.Name);
        Assert.Equal(source.Stats, saved.Stats);
        Assert.Equal(1234, saved.Cost);
        saved.Cost = null;
        saved.Stats.Clear();
        AnimalDatabaseFile.Save(path, [saved]);
        saved = Assert.Single(AnimalDatabaseFile.Read(path));
        Assert.Null(saved.Cost);
        Assert.Empty(saved.Stats);
    }

    [Fact]
    public void Outside_changes_reject_stale_save_without_overwriting_data()
    {
        var path = PathToDatabase();
        AnimalDatabaseFile.Save(path, [Animal()]);
        var original = AnimalDatabaseFile.Read(path);
        var revision = AnimalDatabaseFile.Snapshot(original);
        var outside = Animal();
        outside.Name = "Changed externally";
        AnimalDatabaseFile.Save(path, [outside]);
        Assert.Throws<ManifestException>(() => AnimalDatabaseFile.Save(path, original, revision));
        Assert.Equal("Changed externally", Assert.Single(AnimalDatabaseFile.Read(path)).Name);
    }

    [Fact]
    public void Backup_can_be_read_without_source_and_preserves_identity()
    {
        var source = PathToDatabase();
        var target = PathToDatabase();
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        AnimalDatabaseFile.Save(source, [Animal()]);
        AnimalDatabaseFile.CopySnapshot(source, target);
        File.Delete(source);
        var saved = Assert.Single(AnimalDatabaseFile.Read(target));
        var id = saved.Key("animal-pack");
        saved.Name = "Renamed";
        Assert.Equal(id, saved.Key("animal-pack"));
        Assert.NotEqual(id, saved.Key("another-pack"));
    }

    [Fact]
    public void Database_does_not_serialize_into_manifest()
    {
        var mod = new ModManifest { Id = "animal-pack" };
        mod.Species.Add(Animal());
        Assert.DoesNotContain("species", mod.ToJson());
        Assert.Throws<ManifestException>(() => ModManifest.Parse("""{ "format": 1, "id": "animal-pack", "species": [] }"""));
    }

    [Fact]
    public void Invalid_stats_and_duplicate_species_are_refused_before_writing()
    {
        var path = PathToDatabase();
        var source = Animal();
        Assert.Throws<ManifestException>(() => AnimalDatabaseFile.Save(path, [source, source]));
        source.Stats["adultMassKg"] = double.NaN;
        Assert.Throws<ManifestException>(() => AnimalDatabaseFile.Save(path, [source]));
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("adultMassKg", -1)]
    [InlineData("foliageMin", 2)]
    [InlineData("unknown", 1)]
    public void Stat_schema_rejects_unsupported_values(string key, double value) =>
        Assert.Throws<ManifestException>(() => SpeciesStats.Validate(new Dictionary<string, double> { [key] = value }));
}
