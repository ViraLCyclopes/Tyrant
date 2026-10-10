using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Tyrant.Framework.Core
{
    /// <summary>The mod's real SQLite animal database. Windows supplies SQLite to both Studio and Unity Mono.</summary>
    public static class AnimalDatabaseFile
    {
        public const string FileName = "animals.db";
        public const int SchemaVersion = 5;

        public static List<SpeciesEntry> Read(string path)
        {
            if (!File.Exists(path)) return new List<SpeciesEntry>();
            using (var db = new Database(path, writable: false))
            {
                db.Execute("BEGIN");
                CheckVersion(db);
                var entries = ReadRows(db);
                db.Execute("COMMIT");
                return entries;
            }
        }

        /// <summary>Canonical data revision: also detects SQL edits committed to an external editor's WAL.</summary>
        public static string Snapshot(IEnumerable<SpeciesEntry> entries) => Json.Write(entries.OrderBy(s => s.Id, StringComparer.Ordinal)
            .Select(s => (object?)s.ToMap()).ToList());

        public static bool IsAuxiliary(string path)
        {
            var name = Path.GetFileName(path);
            return new[] { FileName + "-wal", FileName + "-shm", FileName + "-journal" }.Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>SQLite's online backup includes committed edits still in an external editor's WAL.</summary>
        public static void CopySnapshot(string source, string destination)
        {
            if (File.Exists(destination)) throw new IOException("Database snapshot destination already exists.");
            using (var input = new Database(source, writable: false))
            using (var output = new Database(destination, writable: true))
            {
                var backup = Native.sqlite3_backup_init(output.Handle, Utf8("main"), input.Handle, Utf8("main"));
                if (backup == IntPtr.Zero) throw new ManifestException("animals.db backup: " + output.Error());
                try
                {
                    if (Native.sqlite3_backup_step(backup, -1) != 101) throw new ManifestException("animals.db backup: " + output.Error());
                }
                finally { Native.sqlite3_backup_finish(backup); }
                output.Execute("PRAGMA journal_mode=DELETE");
            }
        }

        public static void Save(string path, IReadOnlyList<SpeciesEntry> entries, string? expectedSnapshot = null)
        {
            Validate(entries);
            var exists = File.Exists(path);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using (var db = new Database(path, writable: true))
            {
                db.Execute("BEGIN IMMEDIATE");
                try
                {
                    if (!exists)
                    {
                        db.Execute("CREATE TABLE animal_species (id TEXT PRIMARY KEY NOT NULL, template TEXT NOT NULL, name TEXT NOT NULL, genus TEXT NOT NULL, epithet TEXT NOT NULL, cost INTEGER CHECK(cost IS NULL OR (cost >= 0 AND cost <= 2147483647)))");
                        db.Execute("CREATE TABLE animal_stats (species_id TEXT NOT NULL REFERENCES animal_species(id) ON DELETE CASCADE, stat TEXT NOT NULL, value REAL NOT NULL, PRIMARY KEY(species_id, stat))");
                        db.Execute("PRAGMA user_version = 1");
                    }
                    CheckVersion(db);
                    var current = ReadRows(db);
                    if (expectedSnapshot != null && Snapshot(current) != expectedSnapshot)
                        throw new ManifestException("animals.db changed outside Tyrant; reload the species database before saving.");
                    db.Execute("CREATE TABLE IF NOT EXISTS animal_fields (species_id TEXT NOT NULL REFERENCES animal_species(id) ON DELETE CASCADE, path TEXT NOT NULL, kind TEXT NOT NULL, value TEXT NOT NULL, template_value TEXT NOT NULL, editable INTEGER NOT NULL CHECK(editable IN (0,1)), PRIMARY KEY(species_id,path))");
                    db.Execute("PRAGMA user_version = " + SchemaVersion);
                    db.Execute("CREATE TABLE IF NOT EXISTS animal_localization (species_id TEXT NOT NULL REFERENCES animal_species(id) ON DELETE CASCADE, slot TEXT NOT NULL, term TEXT NOT NULL, PRIMARY KEY(species_id,slot))");
                    db.Execute("CREATE TABLE IF NOT EXISTS animal_assets (species_id TEXT NOT NULL REFERENCES animal_species(id) ON DELETE CASCADE, slot TEXT NOT NULL CHECK(slot='icon'), file TEXT NOT NULL, PRIMARY KEY(species_id,slot))");
                    db.Execute("CREATE TABLE IF NOT EXISTS animal_prefabs (species_id TEXT PRIMARY KEY NOT NULL REFERENCES animal_species(id) ON DELETE CASCADE, bundle TEXT NOT NULL, asset TEXT NOT NULL)");
                    db.Execute("CREATE TABLE IF NOT EXISTS animal_starting_skin (species_id TEXT PRIMARY KEY NOT NULL REFERENCES animal_species(id) ON DELETE CASCADE, skin INTEGER NOT NULL CHECK(skin >= 0), name TEXT, swatch TEXT)");
                    db.Execute("CREATE TABLE IF NOT EXISTS animal_portraits (species_id TEXT NOT NULL REFERENCES animal_species(id) ON DELETE CASCADE, slot TEXT NOT NULL, file TEXT NOT NULL, PRIMARY KEY(species_id,slot))");
                    foreach (var old in current.Where(s => !entries.Any(e => e.Id == s.Id)))
                    {
                        db.Execute("DELETE FROM animal_fields WHERE species_id = ?", old.Id);
                        db.Execute("DELETE FROM animal_localization WHERE species_id = ?", old.Id);
                        db.Execute("DELETE FROM animal_assets WHERE species_id = ?", old.Id);
                        db.Execute("DELETE FROM animal_prefabs WHERE species_id = ?", old.Id);
                        db.Execute("DELETE FROM animal_starting_skin WHERE species_id = ?", old.Id);
                        db.Execute("DELETE FROM animal_portraits WHERE species_id = ?", old.Id);
                        db.Execute("DELETE FROM animal_stats WHERE species_id = ?", old.Id);
                        db.Execute("DELETE FROM animal_species WHERE id = ?", old.Id);
                    }
                    foreach (var entry in entries)
                    {
                        // UPDATE preserves any external editor's additional columns and unrelated tables.
                        db.Execute("UPDATE animal_species SET template=?, name=?, genus=?, epithet=?, cost=? WHERE id=?",
                            entry.Base, entry.Name, entry.Genus, entry.Epithet, entry.Cost, entry.Id);
                        db.Execute("INSERT INTO animal_species(id, template, name, genus, epithet, cost) SELECT ?,?,?,?,?,? WHERE NOT EXISTS(SELECT 1 FROM animal_species WHERE id=?)",
                            entry.Id, entry.Base, entry.Name, entry.Genus, entry.Epithet, entry.Cost, entry.Id);
                        db.Execute("DELETE FROM animal_stats WHERE species_id = ?", entry.Id);
                        foreach (var stat in entry.Stats)
                            db.Execute("INSERT INTO animal_stats(species_id, stat, value) VALUES(?,?,?)", entry.Id, stat.Key, stat.Value);
                        db.Execute("DELETE FROM animal_fields WHERE species_id = ?", entry.Id);
                        foreach (var field in entry.Fields)
                            db.Execute("INSERT INTO animal_fields(species_id,path,kind,value,template_value,editable) VALUES(?,?,?,?,?,?)",
                                entry.Id, field.Path, field.Kind, field.Value, field.TemplateValue, field.Editable ? 1 : 0);
                        db.Execute("DELETE FROM animal_localization WHERE species_id = ?", entry.Id);
                        foreach (var term in entry.Localization)
                            db.Execute("INSERT INTO animal_localization(species_id,slot,term) VALUES(?,?,?)", entry.Id, term.Key, term.Value);
                        db.Execute("DELETE FROM animal_assets WHERE species_id = ?", entry.Id);
                        if (entry.Icon != null) db.Execute("INSERT INTO animal_assets(species_id,slot,file) VALUES(?, 'icon', ?)", entry.Id, entry.Icon);
                        db.Execute("DELETE FROM animal_prefabs WHERE species_id = ?", entry.Id);
                        if (entry.Prefab != null) db.Execute("INSERT INTO animal_prefabs(species_id,bundle,asset) VALUES(?,?,?)", entry.Id, entry.Prefab.Bundle, entry.Prefab.Asset);
                        db.Execute("DELETE FROM animal_starting_skin WHERE species_id = ?", entry.Id);
                        if (entry.StartingSkin != 0 || entry.StartingSkinName != null || entry.Swatch != null)
                            db.Execute("INSERT INTO animal_starting_skin(species_id,skin,name,swatch) VALUES(?,?,?,?)", entry.Id, entry.StartingSkin, entry.StartingSkinName, entry.Swatch);
                        db.Execute("DELETE FROM animal_portraits WHERE species_id = ?", entry.Id);
                        foreach (var portrait in entry.Portraits)
                            db.Execute("INSERT INTO animal_portraits(species_id,slot,file) VALUES(?,?,?)", entry.Id, portrait.Key, portrait.Value);
                    }
                    db.Execute("COMMIT");
                }
                catch { db.Execute("ROLLBACK"); throw; }
            }
        }

        private static void CheckVersion(Database db)
        {
            using (var query = db.Query("PRAGMA user_version"))
                if (!query.Next() || query.Integer(0) < 1 || query.Integer(0) > SchemaVersion)
                    throw new ManifestException("animals.db has an unsupported schema version (expected 1 to 5).");
        }

        private static List<SpeciesEntry> ReadRows(Database db)
        {
            var entries = new List<SpeciesEntry>();
            using (var query = db.Query("SELECT id, template, name, genus, epithet, cost FROM animal_species ORDER BY id"))
            {
                while (query.Next())
                {
                    if (!query.IsNull(5) && (!query.IsInteger(5) || query.Integer(5) < 0 || query.Integer(5) > int.MaxValue))
                        throw new ManifestException("animals.db: species cost must be a nonnegative integer or NULL.");
                    entries.Add(new SpeciesEntry
                    {
                        Id = query.Text(0), Base = query.Text(1), Name = query.Text(2), Genus = query.Text(3), Epithet = query.Text(4),
                        Cost = query.IsNull(5) ? (int?)null : (int)query.Integer(5),
                    });
                }
            }
            using (var query = db.Query("SELECT species_id, stat, value FROM animal_stats ORDER BY species_id, stat"))
            {
                while (query.Next())
                {
                    var id = query.Text(0);
                    var entry = entries.FirstOrDefault(s => s.Id == id) ?? throw new ManifestException("animals.db: stat references missing species '" + id + "'.");
                    if (!query.IsNumber(2)) throw new ManifestException("animals.db: stat values must be numbers.");
                    var stat = query.Text(1);
                    if (entry.Stats.ContainsKey(stat)) throw new ManifestException("animals.db: duplicate stat '" + stat + "'.");
                    entry.Stats.Add(stat, query.Number(2));
                }
            }
            bool hasFields;
            using (var query = db.Query("SELECT 1 FROM sqlite_master WHERE type='table' AND name='animal_fields'")) hasFields = query.Next();
            if (hasFields)
                using (var query = db.Query("SELECT species_id,path,kind,value,template_value,editable FROM animal_fields ORDER BY species_id,path"))
                    while (query.Next())
                    {
                        var entry = entries.FirstOrDefault(s => s.Id == query.Text(0)) ?? throw new ManifestException("animal_fields references a missing species.");
                        if (!query.IsInteger(5) || (query.Integer(5) != 0 && query.Integer(5) != 1)) throw new ManifestException("animal_fields.editable must be 0 or 1.");
                        entry.Fields.Add(new AnimalField { Path = query.Text(1), Kind = query.Text(2), Value = query.Text(3), TemplateValue = query.Text(4), Editable = query.Integer(5) == 1 });
                    }
            bool hasText;
            using (var query = db.Query("SELECT 1 FROM sqlite_master WHERE type='table' AND name='animal_localization'")) hasText = query.Next();
            if (hasText)
                using (var query = db.Query("SELECT species_id,slot,term FROM animal_localization ORDER BY species_id,slot"))
                    while (query.Next())
                    {
                        var entry = entries.FirstOrDefault(s => s.Id == query.Text(0)) ?? throw new ManifestException("animal_localization references a missing species.");
                        entry.Localization.Add(query.Text(1), query.Text(2));
                    }
            bool hasAssets;
            using (var query = db.Query("SELECT 1 FROM sqlite_master WHERE type='table' AND name='animal_assets'")) hasAssets = query.Next();
            if (hasAssets)
                using (var query = db.Query("SELECT species_id,slot,file FROM animal_assets ORDER BY species_id,slot"))
                    while (query.Next())
                    {
                        var entry = entries.FirstOrDefault(s => s.Id == query.Text(0)) ?? throw new ManifestException("animal_assets references a missing species.");
                        if (query.Text(1) != "icon") throw new ManifestException("Unknown species asset slot: " + query.Text(1));
                        entry.Icon = query.Text(2);
                    }
            bool hasPrefabs;
            using (var query = db.Query("SELECT 1 FROM sqlite_master WHERE type='table' AND name='animal_prefabs'")) hasPrefabs = query.Next();
            if (hasPrefabs)
                using (var query = db.Query("SELECT species_id,bundle,asset FROM animal_prefabs ORDER BY species_id"))
                    while (query.Next())
                    {
                        var entry = entries.FirstOrDefault(s => s.Id == query.Text(0)) ?? throw new ManifestException("animal_prefabs references a missing species.");
                        entry.Prefab = new SpeciesPrefab { Bundle = query.Text(1), Asset = query.Text(2) };
                    }
            bool hasStartingSkins;
            using (var query = db.Query("SELECT 1 FROM sqlite_master WHERE type='table' AND name='animal_starting_skin'")) hasStartingSkins = query.Next();
            if (hasStartingSkins)
                using (var query = db.Query("SELECT species_id,skin,name,swatch FROM animal_starting_skin ORDER BY species_id"))
                    while (query.Next())
                    {
                        var entry = entries.FirstOrDefault(s => s.Id == query.Text(0)) ?? throw new ManifestException("animal_starting_skin references a missing species.");
                        if (!query.IsInteger(1) || query.Integer(1) < 0 || query.Integer(1) > int.MaxValue) throw new ManifestException("animal_starting_skin.skin must be a nonnegative integer.");
                        entry.StartingSkin = (int)query.Integer(1);
                        entry.StartingSkinName = query.IsNull(2) ? null : query.Text(2);
                        entry.Swatch = query.IsNull(3) ? null : query.Text(3);
                    }
            bool hasPortraits;
            using (var query = db.Query("SELECT 1 FROM sqlite_master WHERE type='table' AND name='animal_portraits'")) hasPortraits = query.Next();
            if (hasPortraits)
                using (var query = db.Query("SELECT species_id,slot,file FROM animal_portraits ORDER BY species_id,slot"))
                    while (query.Next())
                    {
                        var entry = entries.FirstOrDefault(s => s.Id == query.Text(0)) ?? throw new ManifestException("animal_portraits references a missing species.");
                        if (!PortraitSlots.IsSlot(query.Text(1)))
                            throw new ManifestException($"animal_portraits: unknown portrait slot '{query.Text(1)}' (use {string.Join(", ", PortraitSlots.All)}).");
                        entry.Portraits[query.Text(1)] = query.Text(2);
                    }
            Validate(entries);
            return entries;
        }

        private static void Validate(IEnumerable<SpeciesEntry> entries)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (!ModId.IsValid(entry.Id) || !ids.Add(entry.Id)) throw new ManifestException("animals.db: invalid or duplicate species id '" + entry.Id + "'.");
                if (new[] { entry.Base, entry.Name, entry.Genus, entry.Epithet }.Any(string.IsNullOrWhiteSpace))
                    throw new ManifestException("animals.db: species '" + entry.Id + "' needs template, name, genus and epithet.");
                if (entry.Cost < 0) throw new ManifestException("animals.db: cost must be nonnegative.");
                SpeciesStats.Validate(entry.Stats);
                entry.Prefab?.Validate();
                var paths = new HashSet<string>(StringComparer.Ordinal);
                foreach (var field in entry.Fields)
                {
                    field.Validate();
                    if (!paths.Add(field.Path)) throw new ManifestException("Duplicate animal field " + field.Path);
                }
                foreach (var term in entry.Localization)
                {
                    if (!AnimalText.Keys.Contains(term.Key) || string.IsNullOrWhiteSpace(term.Value)) throw new ManifestException("Invalid animal localization term: " + term.Key);
                    AnimalLocalization.ValidateTerm(term.Value);
                }
                if (entry.Icon != null && !IsModPng(entry.Icon)) throw new ManifestException("Species icon must be a relative PNG path inside the mod.");
                if (entry.Swatch != null && !IsModPng(entry.Swatch)) throw new ManifestException("Species swatch must be a relative PNG path inside the mod.");
                if (entry.StartingSkin < 0) throw new ManifestException("animals.db: species '" + entry.Id + "' starting skin must be 0 or more.");
                if (entry.StartingSkinName != null && string.IsNullOrWhiteSpace(entry.StartingSkinName))
                    throw new ManifestException("animals.db: species '" + entry.Id + "' starting skin name is empty (use NULL for the template's name).");
                foreach (var portrait in entry.Portraits)
                {
                    if (!PortraitSlots.IsSlot(portrait.Key)) throw new ManifestException($"animals.db: species '{entry.Id}' has unknown portrait slot '{portrait.Key}'.");
                    if (!IsModPng(portrait.Value)) throw new ManifestException("Species portraits must be relative PNG paths inside the mod.");
                }
            }
        }

        private static bool IsModPng(string file) =>
            !string.IsNullOrWhiteSpace(file) && !Path.IsPathRooted(file) && !file.Contains(":") &&
            !file.Replace('\\', '/').Split('/').Any(p => p == ".." || p == "." || p.Length == 0) && file.EndsWith(".png", StringComparison.OrdinalIgnoreCase);

        private sealed class Database : IDisposable
        {
            internal IntPtr Handle;
            public Database(string path, bool writable)
            {
                var result = Native.sqlite3_open_v2(Utf8(Path.GetFullPath(path)), out Handle, writable ? 6 : 1, IntPtr.Zero);
                if (result != 0) { var error = Error(); Dispose(); throw new ManifestException("animals.db: " + error); }
                Native.sqlite3_busy_timeout(Handle, 1500);
                if (writable) Execute("PRAGMA foreign_keys = ON");
            }
            internal string Error() => ReadUtf8(Native.sqlite3_errmsg(Handle));
            public Query Query(string sql, params object?[] values) => new Query(this, sql, values);
            public void Execute(string sql, params object?[] values)
            {
                using (var query = Query(sql, values)) while (query.Next()) { }
            }
            public void Dispose() { if (Handle != IntPtr.Zero) { Native.sqlite3_close_v2(Handle); Handle = IntPtr.Zero; } }
        }

        private sealed class Query : IDisposable
        {
            private readonly Database _db;
            private IntPtr _handle;
            public Query(Database db, string sql, object?[] values)
            {
                _db = db;
                var result = Native.sqlite3_prepare_v2(db.Handle, Utf8(sql), -1, out _handle, IntPtr.Zero);
                if (result != 0) { Dispose(); throw new ManifestException("animals.db: " + db.Error()); }
                try
                {
                    for (var i = 0; i < values.Length; i++)
                    {
                        var value = values[i];
                        if (value == null) result = Native.sqlite3_bind_null(_handle, i + 1);
                        else if (value is string text)
                        {
                            var bytes = Utf8(text);
                            result = Native.sqlite3_bind_text(_handle, i + 1, bytes, bytes.Length - 1, new IntPtr(-1));
                        }
                        else if (value is int integer) result = Native.sqlite3_bind_int64(_handle, i + 1, integer);
                        else result = Native.sqlite3_bind_double(_handle, i + 1, Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture));
                        if (result != 0) throw new ManifestException("animals.db: " + db.Error());
                    }
                }
                catch { Dispose(); throw; }
            }
            public bool Next()
            {
                var result = Native.sqlite3_step(_handle);
                if (result == 100) return true;
                if (result == 101) return false;
                throw new ManifestException("animals.db: " + _db.Error());
            }
            public bool IsNull(int column) => Native.sqlite3_column_type(_handle, column) == 5;
            public bool IsInteger(int column) => Native.sqlite3_column_type(_handle, column) == 1;
            public bool IsNumber(int column) => IsInteger(column) || Native.sqlite3_column_type(_handle, column) == 2;
            public long Integer(int column) => Native.sqlite3_column_int64(_handle, column);
            public double Number(int column) => Native.sqlite3_column_double(_handle, column);
            public string Text(int column)
            {
                if (Native.sqlite3_column_type(_handle, column) != 3) throw new ManifestException("animals.db: species names and ids must be text.");
                return ReadUtf8(Native.sqlite3_column_text(_handle, column), Native.sqlite3_column_bytes(_handle, column));
            }
            public void Dispose() { if (_handle != IntPtr.Zero) { Native.sqlite3_finalize(_handle); _handle = IntPtr.Zero; } }
        }

        private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value + "\0");
        private static string ReadUtf8(IntPtr pointer, int length = -1)
        {
            if (pointer == IntPtr.Zero) return "";
            if (length < 0) { length = 0; while (Marshal.ReadByte(pointer, length) != 0) length++; }
            var bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }

        private static class Native
        {
            private const string Library = "winsqlite3.dll";
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_open_v2(byte[] path, out IntPtr db, int flags, IntPtr vfs);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_close_v2(IntPtr db);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_busy_timeout(IntPtr db, int milliseconds);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_errmsg(IntPtr db);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int count, out IntPtr statement, IntPtr tail);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_step(IntPtr statement);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_finalize(IntPtr statement);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_bind_text(IntPtr statement, int column, byte[] value, int length, IntPtr destructor);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_bind_int64(IntPtr statement, int column, long value);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_bind_double(IntPtr statement, int column, double value);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_bind_null(IntPtr statement, int column);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_column_type(IntPtr statement, int column);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern long sqlite3_column_int64(IntPtr statement, int column);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern double sqlite3_column_double(IntPtr statement, int column);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_column_bytes(IntPtr statement, int column);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_backup_init(IntPtr destination, byte[] destinationName, IntPtr source, byte[] sourceName);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_backup_step(IntPtr backup, int pages);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_backup_finish(IntPtr backup);
        }
    }
}
