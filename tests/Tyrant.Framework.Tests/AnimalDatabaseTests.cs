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

    [Fact]
    public void Starting_skin_name_and_swatch_roundtrip_and_change_the_revision()
    {
        var path = PathToDatabase();
        var source = Animal();
        AnimalDatabaseFile.Save(path, [source]);
        var before = AnimalDatabaseFile.Snapshot([source]);
        source.StartingSkin = 2;
        source.StartingSkinName = "Mirabilis";
        source.Swatch = "icons/new-animal.swatch.png";
        AnimalDatabaseFile.Save(path, [source], before);

        var saved = Assert.Single(AnimalDatabaseFile.Read(path));
        Assert.Equal(2, saved.StartingSkin);
        Assert.Equal("Mirabilis", saved.StartingSkinName);
        Assert.Equal("icons/new-animal.swatch.png", saved.Swatch);
        Assert.NotEqual(before, AnimalDatabaseFile.Snapshot([saved]));

        saved.StartingSkin = 0;
        saved.StartingSkinName = null;
        saved.Swatch = null;
        AnimalDatabaseFile.Save(path, [saved]);
        var reset = Assert.Single(AnimalDatabaseFile.Read(path));
        Assert.Equal(0, reset.StartingSkin);
        Assert.Null(reset.StartingSkinName);
        Assert.Null(reset.Swatch);
    }

    [Theory]
    [InlineData(-1, null, null)]
    [InlineData(0, "  ", null)]
    [InlineData(0, null, "../swatch.png")]
    [InlineData(0, null, "icons/swatch.jpg")]
    public void Invalid_starting_skin_values_are_rejected(int skin, string? name, string? swatch)
    {
        var animal = Animal();
        animal.StartingSkin = skin;
        animal.StartingSkinName = name;
        animal.Swatch = swatch;
        Assert.Throws<ManifestException>(() => AnimalDatabaseFile.Save(PathToDatabase(), [animal]));
    }

    [Fact]
    public void A_version_3_file_without_the_starting_skin_table_reads_as_skin_0_and_saves_as_version_4()
    {
        var path = PathToDatabase();
        AnimalDatabaseFile.Save(path, [Animal()]);
        Sqlite(path, "DROP TABLE animal_starting_skin; PRAGMA user_version = 3;");

        var old = Assert.Single(AnimalDatabaseFile.Read(path));
        Assert.Equal(0, old.StartingSkin);
        old.StartingSkin = 1;
        AnimalDatabaseFile.Save(path, [old]);

        Assert.Equal(1, Assert.Single(AnimalDatabaseFile.Read(path)).StartingSkin);
        Assert.Equal("4", Sqlite(path, "PRAGMA user_version;").Trim());
    }

    private static string PathToDatabase() => Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), AnimalDatabaseFile.FileName);

    /// <summary>Runs SQL against the file with Windows' SQLite and returns the first column of the last row (or "").</summary>
    private static string Sqlite(string path, string sql)
    {
        Assert.Equal(0, SqliteNative.sqlite3_open_v2(Encoding.UTF8.GetBytes(path + "\0"), out var db, 6, IntPtr.Zero));
        try
        {
            var result = "";
            foreach (var statement in sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Assert.Equal(0, SqliteNative.sqlite3_prepare_v2(db, Encoding.UTF8.GetBytes(statement + "\0"), -1, out var stmt, IntPtr.Zero));
                try
                {
                    while (SqliteNative.sqlite3_step(stmt) == 100)
                        result = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(SqliteNative.sqlite3_column_text(stmt, 0)) ?? "";
                }
                finally { SqliteNative.sqlite3_finalize(stmt); }
            }
            return result;
        }
        finally { SqliteNative.sqlite3_close_v2(db); }
    }

    private static class SqliteNative
    {
        [System.Runtime.InteropServices.DllImport("winsqlite3.dll")] internal static extern int sqlite3_open_v2(byte[] path, out IntPtr db, int flags, IntPtr vfs);
        [System.Runtime.InteropServices.DllImport("winsqlite3.dll")] internal static extern int sqlite3_close_v2(IntPtr db);
        [System.Runtime.InteropServices.DllImport("winsqlite3.dll")] internal static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int count, out IntPtr stmt, IntPtr tail);
        [System.Runtime.InteropServices.DllImport("winsqlite3.dll")] internal static extern int sqlite3_step(IntPtr stmt);
        [System.Runtime.InteropServices.DllImport("winsqlite3.dll")] internal static extern int sqlite3_finalize(IntPtr stmt);
        [System.Runtime.InteropServices.DllImport("winsqlite3.dll")] internal static extern IntPtr sqlite3_column_text(IntPtr stmt, int column);
    }
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
