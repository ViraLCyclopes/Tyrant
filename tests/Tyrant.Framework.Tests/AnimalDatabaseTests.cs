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
    public void Starting_skin_portraits_roundtrip_and_change_the_revision()
    {
        var path = PathToDatabase();
        var source = Animal();
        AnimalDatabaseFile.Save(path, [source]);
        var before = AnimalDatabaseFile.Snapshot([source]);
        source.Portraits["adultMale"] = "portraits/new-animal/adultMale.png";
        source.Portraits["infant"] = "portraits/new-animal/infant.render.png";
        AnimalDatabaseFile.Save(path, [source], before);

        var saved = Assert.Single(AnimalDatabaseFile.Read(path));
        Assert.Equal(source.Portraits, saved.Portraits);
        Assert.NotEqual(before, AnimalDatabaseFile.Snapshot([saved]));
        Assert.Equal("6", Sqlite(path, "PRAGMA user_version;"));

        saved.Portraits.Clear();
        AnimalDatabaseFile.Save(path, [saved]);
        Assert.Empty(Assert.Single(AnimalDatabaseFile.Read(path)).Portraits);
    }

    [Fact]
    public void Nursery_and_paleopedia_pictures_roundtrip_in_their_own_slots_and_change_the_revision()
    {
        var path = PathToDatabase();
        var source = Animal();
        AnimalDatabaseFile.Save(path, [source]);
        var before = AnimalDatabaseFile.Snapshot([source]);
        source.Icon = "icons/new-animal.png";
        source.PaleopediaPicture = "icons/new-animal-paleopedia.png";
        AnimalDatabaseFile.Save(path, [source], before);

        var saved = Assert.Single(AnimalDatabaseFile.Read(path));
        Assert.Equal(("icons/new-animal.png", "icons/new-animal-paleopedia.png"), (saved.Icon, saved.PaleopediaPicture));
        Assert.NotEqual(before, AnimalDatabaseFile.Snapshot([saved]));
        Assert.Equal("paleopedia", Sqlite(path, "SELECT slot FROM animal_assets WHERE file='icons/new-animal-paleopedia.png';"));
        Assert.Equal("6", Sqlite(path, "PRAGMA user_version;"));

        saved.PaleopediaPicture = null;
        AnimalDatabaseFile.Save(path, [saved]);
        var cleared = Assert.Single(AnimalDatabaseFile.Read(path));
        Assert.Equal(("icons/new-animal.png", (string?)null), (cleared.Icon, cleared.PaleopediaPicture));
    }

    [Theory]
    [InlineData("../paleopedia.png")]
    [InlineData("icons/paleopedia.jpg")]
    [InlineData("C:/paleopedia.png")]
    public void An_unsafe_paleopedia_picture_is_rejected(string file)
    {
        var animal = Animal();
        animal.PaleopediaPicture = file;
        Assert.Throws<ManifestException>(() => AnimalDatabaseFile.Save(PathToDatabase(), [animal]));
    }

    [Fact]
    public void A_version_5_file_keeps_its_icon_and_saves_as_version_6_with_a_paleopedia_picture()
    {
        var path = PathToDatabase();
        var animal = Animal();
        animal.Icon = "icons/new-animal.png";
        AnimalDatabaseFile.Save(path, [animal]);
        // Version 5's table only allowed the icon slot.
        Sqlite(path, "DROP TABLE animal_assets; CREATE TABLE animal_assets (species_id TEXT NOT NULL REFERENCES animal_species(id) ON DELETE CASCADE, slot TEXT NOT NULL CHECK(slot='icon'), file TEXT NOT NULL, PRIMARY KEY(species_id,slot)); INSERT INTO animal_assets VALUES('new-animal','icon','icons/new-animal.png'); PRAGMA user_version = 5;");

        var old = Assert.Single(AnimalDatabaseFile.Read(path));
        Assert.Equal(("icons/new-animal.png", (string?)null), (old.Icon, old.PaleopediaPicture));
        old.PaleopediaPicture = "icons/new-animal-paleopedia.png";
        AnimalDatabaseFile.Save(path, [old]);

        var upgraded = Assert.Single(AnimalDatabaseFile.Read(path));
        Assert.Equal(("icons/new-animal.png", "icons/new-animal-paleopedia.png"), (upgraded.Icon, upgraded.PaleopediaPicture));
        Assert.Equal("6", Sqlite(path, "PRAGMA user_version;"));
    }

    [Fact]
    public void An_unknown_asset_slot_written_by_hand_is_an_error_naming_the_slots()
    {
        var path = PathToDatabase();
        AnimalDatabaseFile.Save(path, [Animal()]);
        Sqlite(path, "DROP TABLE animal_assets; CREATE TABLE animal_assets (species_id TEXT NOT NULL, slot TEXT NOT NULL, file TEXT NOT NULL); INSERT INTO animal_assets VALUES('new-animal','banner','icons/a.png');");

        var ex = Assert.Throws<ManifestException>(() => AnimalDatabaseFile.Read(path));
        Assert.Contains("banner", ex.Message);
        Assert.Contains("paleopedia", ex.Message);
    }

    [Theory]
    [InlineData("adult", "portraits/a.png")]
    [InlineData("adultMale", "../a.png")]
    [InlineData("adultMale", "portraits/a.jpg")]
    public void Invalid_portraits_are_rejected(string slot, string file)
    {
        var animal = Animal();
        animal.Portraits[slot] = file;
        Assert.Throws<ManifestException>(() => AnimalDatabaseFile.Save(PathToDatabase(), [animal]));
    }

    [Fact]
    public void A_version_4_file_reads_without_portraits_and_saves_as_the_current_version()
    {
        var path = PathToDatabase();
        var animal = Animal();
        animal.StartingSkin = 1;
        AnimalDatabaseFile.Save(path, [animal]);
        Sqlite(path, "DROP TABLE animal_portraits; PRAGMA user_version = 4;");

        var old = Assert.Single(AnimalDatabaseFile.Read(path));
        Assert.Empty(old.Portraits);
        old.Portraits["adultFemale"] = "portraits/new-animal/adultFemale.png";
        AnimalDatabaseFile.Save(path, [old]);

        var upgraded = Assert.Single(AnimalDatabaseFile.Read(path));
        Assert.Equal((1, "portraits/new-animal/adultFemale.png"), (upgraded.StartingSkin, upgraded.Portraits["adultFemale"]));
        Assert.Equal("6", Sqlite(path, "PRAGMA user_version;"));
    }

    [Fact]
    public void An_unknown_slot_written_by_hand_is_an_error_naming_the_table()
    {
        var path = PathToDatabase();
        AnimalDatabaseFile.Save(path, [Animal()]);
        Sqlite(path, "INSERT INTO animal_portraits(species_id,slot,file) VALUES('new-animal','adult','portraits/a.png');");

        var ex = Assert.Throws<ManifestException>(() => AnimalDatabaseFile.Read(path));
        Assert.Contains("animal_portraits", ex.Message);
    }

    [Fact]
    public void A_version_3_file_without_the_starting_skin_table_reads_as_skin_0_and_saves_as_the_current_version()
    {
        var path = PathToDatabase();
        AnimalDatabaseFile.Save(path, [Animal()]);
        Sqlite(path, "DROP TABLE animal_starting_skin; PRAGMA user_version = 3;");

        var old = Assert.Single(AnimalDatabaseFile.Read(path));
        Assert.Equal(0, old.StartingSkin);
        old.StartingSkin = 1;
        AnimalDatabaseFile.Save(path, [old]);

        Assert.Equal(1, Assert.Single(AnimalDatabaseFile.Read(path)).StartingSkin);
        Assert.Equal("6", Sqlite(path, "PRAGMA user_version;").Trim());
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
