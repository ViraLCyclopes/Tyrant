using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// A look bound in the Nursery to one incubating (or held) animal: which animal it was made for (species, skin key, sex, as
    /// the editor keys them) and its materials. A virtual animal's id can be handed out again (the game counts them from the
    /// highest one loaded), so the look is applied only to an animal that still <see cref="Matches"/>.
    /// </summary>
    public sealed record IncubatingLook(string Species, string? Skin, string? Sex, Dictionary<string, MaterialOverride> Materials)
    {
        /// <summary>True for the same species and sex and the same skin (any case).</summary>
        public bool Matches(string species, string? skin, string? sex) =>
            string.Equals(Species, species, StringComparison.Ordinal)
            && string.Equals(Skin, skin, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Sex, sex, StringComparison.Ordinal);
    }

    /// <summary>
    /// The looks tuned for single animals in one save: { "format":1, "animals": { "&lt;id&gt;": &lt;materials map&gt; },
    /// "incubating": { "&lt;virtual animal id&gt;": { "species", "skin", "sex", "materials": &lt;materials map&gt; } } }.
    /// "incubating" holds the looks saved in the Nursery for an animal being incubated (or waiting in the holder), by its
    /// VirtualAnimal.UniqueID, which the save keeps, with the animal they are for; the look is given to that animal when it is
    /// placed. A file without "incubating" reads as before.
    /// Lives in UserData/Tyrant/Looks/&lt;save&gt;.json, named after the save file (persistent ids are counted per save, and a
    /// park's name is not unique), with the images exported for an animal in Looks/&lt;save&gt;/&lt;id&gt;/; nothing goes into the save.
    /// </summary>
    public sealed class AnimalLooks
    {
        private const string LooksFolder = "Looks";

        private readonly SortedDictionary<ulong, Dictionary<string, MaterialOverride>> _animals = new SortedDictionary<ulong, Dictionary<string, MaterialOverride>>();
        private readonly SortedDictionary<ulong, IncubatingLook> _incubating = new SortedDictionary<ulong, IncubatingLook>();

        /// <summary>How many placed animals have a look (the incubating ones are <see cref="IncubatingCount"/>).</summary>
        public int Count => _animals.Count;

        public int IncubatingCount => _incubating.Count;

        /// <summary>The save's name: the last part of the path the game saved to or loaded from (".../Saves/My Park"); null when none.</summary>
        public static string? SaveNameOf(string? savePath)
        {
            if (string.IsNullOrWhiteSpace(savePath)) return null;
            var name = Path.GetFileName(savePath!.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        /// <summary>A save's looks file, or null when there is no save (a new park not saved yet).</summary>
        public static string? PathFor(string tyrantDataDir, string? saveName) =>
            Segment(saveName) is string name ? Path.Combine(tyrantDataDir, LooksFolder, name + ".json") : null;

        /// <summary>The folder of a save's per-animal images (one folder per animal id), or null when there is no save.</summary>
        public static string? FolderFor(string tyrantDataDir, string? saveName) =>
            Segment(saveName) is string name ? Path.Combine(tyrantDataDir, LooksFolder, name) : null;

        // A save's name as one path segment; never one starting with '.' (Looks/.pending is the editor's own).
        private static string? Segment(string? saveName)
        {
            if (string.IsNullOrWhiteSpace(saveName)) return null;
            var name = MaterialFolders.Sanitize(saveName!.Trim());
            return name.StartsWith(".", StringComparison.Ordinal) ? "_" + name : name;
        }

        /// <summary>A missing file is an empty store; a bad file or animal adds a problem and is skipped.</summary>
        public static AnimalLooks Read(string path, List<string> problems)
        {
            var looks = new AnimalLooks();
            if (!File.Exists(path)) return looks;
            try
            {
                if (!(Json.Parse(File.ReadAllText(path)) is Dictionary<string, object?> root)) throw new ManifestException("It must be a JSON object.");
                if (!(root.TryGetValue("format", out var format) && format is double f && f == 1)) throw new ManifestException("\"format\" must be 1.");
                ReadSection(root, "animals", "animal", looks._animals, path, problems, (value, id) => MaterialOverrides.Parse(value, $"Animal {id}", true));
                ReadSection(root, "incubating", "incubating animal", looks._incubating, path, problems, ReadBinding);
            }
            catch (Exception ex) when (ex is ManifestException || ex is FormatException)
            {
                problems.Add($"{path}: {ex.Message}");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                problems.Add($"{path}: could not be read ({ex.Message})");
            }
            return looks;
        }

        /// <summary>One id-keyed section ("animals", "incubating"); a missing one is empty, a bad id or look a problem that is skipped.</summary>
        private static void ReadSection<T>(Dictionary<string, object?> root, string key, string what, SortedDictionary<ulong, T> into,
            string path, List<string> problems, Func<object?, string, T?> parse) where T : class
        {
            if (!root.TryGetValue(key, out var section) || section == null) return;
            if (!(section is Dictionary<string, object?> map)) throw new ManifestException($"\"{key}\" must be an object.");
            foreach (var pair in map)
            {
                if (!ulong.TryParse(pair.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                {
                    problems.Add($"{path}: {what} \"{pair.Key}\" is not an id.");
                    continue;
                }
                try
                {
                    if (parse(pair.Value, pair.Key) is T value) into[id] = value;
                }
                catch (ManifestException ex)
                {
                    problems.Add($"{path}: {ex.Message}");
                }
            }
        }

        /// <summary>One binding: the animal it is for ("species" needed; "skin", "sex" when known) and its "materials".</summary>
        private static IncubatingLook? ReadBinding(object? value, string id)
        {
            var owner = $"Incubating animal {id}";
            if (!(value is Dictionary<string, object?> map) || !(map.TryGetValue("species", out var s) && s is string species && species.Length > 0))
                throw new ManifestException($"{owner} needs \"species\" (the animal the look is for); fix or delete this looks file (Tyrant never writes over a file with problems).");
            string? Text(string key) => map.TryGetValue(key, out var v) ? v as string : null;
            var materials = map.TryGetValue("materials", out var m) ? MaterialOverrides.Parse(m, owner, true) : null;
            return materials == null ? null : new IncubatingLook(species, Text("skin"), Text("sex"), materials);
        }

        /// <summary>Throws <see cref="ManifestException"/>, writing nothing, when an animal's look is one <see cref="Read"/> would refuse.</summary>
        public void Write(string path)
        {
            var root = new Dictionary<string, object?> { ["format"] = 1, ["animals"] = Section(_animals, (materials, key) => Materials(materials, $"Animal {key}")) };
            if (_incubating.Count > 0) // left out when empty: older Tyrants read the file as before
                root["incubating"] = Section(_incubating, (bound, key) =>
                {
                    var entry = new Dictionary<string, object?> { ["species"] = bound.Species };
                    if (bound.Skin != null) entry["skin"] = bound.Skin;
                    if (bound.Sex != null) entry["sex"] = bound.Sex;
                    entry["materials"] = Materials(bound.Materials, $"Incubating animal {key}");
                    return entry;
                });
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            AtomicFile.Write(path, Json.Write(root) + "\n");
        }

        private static Dictionary<string, object?> Section<T>(SortedDictionary<ulong, T> looks, Func<T, string, object?> write)
        {
            var map = new Dictionary<string, object?>();
            foreach (var pair in looks)
            {
                var key = pair.Key.ToString(CultureInfo.InvariantCulture);
                map[key] = write(pair.Value, key);
            }
            return map;
        }

        private static Dictionary<string, object?> Materials(Dictionary<string, MaterialOverride> materials, string owner) =>
            MaterialOverrides.Problem(materials, owner, true) is string problem ? throw new ManifestException(problem) : MaterialOverrides.ToJson(materials);

        /// <summary>
        /// Writes the store as this save's looks. Images in another save's animal folder (Looks/&lt;other&gt;/&lt;id&gt;/, after Save
        /// As or an autosave) are copied into this save's folder first and the store points at the copies; the other save's
        /// files are never touched. An empty store deletes the save's file rather than writing one (a park saved over this name
        /// must not get the old looks). With <paramref name="alive"/>, the folders of this save's animals that have no look and
        /// are not in the park go too (their looks were pruned).
        /// </summary>
        public void WriteFor(string tyrantDataDir, string saveName, IReadOnlyCollection<ulong>? alive = null)
        {
            var path = PathFor(tyrantDataDir, saveName) ?? throw new ArgumentException("A save name is needed.", nameof(saveName));
            var folder = FolderFor(tyrantDataDir, saveName)!;
            var target = LooksFolder + "/" + Path.GetFileName(folder) + "/";
            var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // source folders already copied by this write
            var replaced = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // destination folders emptied by this write
            foreach (var id in _animals.Keys.ToList())
            {
                var into = target + id.ToString(CultureInfo.InvariantCulture) + "/";
                _animals[id] = _animals[id].ToDictionary(p => p.Key, p => MaterialTable.WithPaths(p.Value, file =>
                {
                    if (!(OtherSaveFolder(file, target) is string from)) return file;
                    var source = Path.Combine(tyrantDataDir, from.Replace('/', Path.DirectorySeparatorChar));
                    if (!Directory.Exists(source)) return file; // nothing to carry: left as it is
                    var destination = Path.Combine(tyrantDataDir, into.Replace('/', Path.DirectorySeparatorChar));
                    if (copied.Add(source)) CopyFolder(source, destination, replace: replaced.Add(destination));
                    return into + file.Substring(from.Length + 1);
                }), StringComparer.Ordinal);
            }
            if (_animals.Count == 0 && _incubating.Count == 0)
            {
                if (File.Exists(path)) File.Delete(path);
            }
            else Write(path);
            if (alive == null || alive.Count == 0 || !Directory.Exists(folder)) return; // nothing is deleted on a guess
            var live = new HashSet<ulong>(alive);
            foreach (var sub in Directory.GetDirectories(folder))
                if (ulong.TryParse(Path.GetFileName(sub), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && !_animals.ContainsKey(id) && !live.Contains(id))
                    Directory.Delete(sub, true);
        }

        /// <summary>Deletes a save's looks file and its image folder (the game removed the save); other saves are left alone.</summary>
        public static void DeleteSave(string tyrantDataDir, string saveName)
        {
            if (PathFor(tyrantDataDir, saveName) is string path && File.Exists(path)) File.Delete(path);
            if (FolderFor(tyrantDataDir, saveName) is string folder && Directory.Exists(folder)) Directory.Delete(folder, true);
        }

        // "Looks/<other save>/<id>" when the file is inside another save's animal folder, else null.
        private static string? OtherSaveFolder(string file, string target)
        {
            var parts = file.Replace('\\', '/').Split('/');
            if (parts.Length < 4 || !string.Equals(parts[0], LooksFolder, StringComparison.OrdinalIgnoreCase)) return null;
            if (!ulong.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out _)) return null;
            if (string.Equals(parts[0] + "/" + parts[1] + "/", target, StringComparison.OrdinalIgnoreCase)) return null;
            return file.Substring(0, parts[0].Length + parts[1].Length + parts[2].Length + 2);
        }

        private static void CopyFolder(string source, string destination, bool replace)
        {
            if (replace && Directory.Exists(destination)) Directory.Delete(destination, true);
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            foreach (var sub in Directory.GetDirectories(source)) CopyFolder(sub, Path.Combine(destination, Path.GetFileName(sub)), replace: false);
        }

        public Dictionary<string, MaterialOverride>? Get(ulong id) => _animals.TryGetValue(id, out var materials) ? materials : null;

        /// <summary>The ids of the animals that have a look.</summary>
        public IEnumerable<ulong> Ids => _animals.Keys;

        /// <summary>Null or a map with nothing in it removes the animal's look.</summary>
        public void Set(ulong id, Dictionary<string, MaterialOverride>? materials)
        {
            if (materials == null || materials.Values.All(m => m.IsEmpty)) _animals.Remove(id);
            else _animals[id] = materials;
        }

        /// <summary>The look bound to an incubating (or held) animal, by its VirtualAnimal.UniqueID; null when none.</summary>
        public IncubatingLook? GetIncubating(ulong virtualId) => _incubating.TryGetValue(virtualId, out var bound) ? bound : null;

        /// <summary>The virtual animal ids that have a look bound.</summary>
        public IEnumerable<ulong> IncubatingIds => _incubating.Keys;

        /// <summary>Binds a look to an incubating animal; null or a look with nothing in it removes the binding.</summary>
        public void SetIncubating(ulong virtualId, IncubatingLook? look)
        {
            if (look == null || look.Materials.Values.All(m => m.IsEmpty)) _incubating.Remove(virtualId);
            else _incubating[virtualId] = look;
        }

        /// <summary>Forgets bindings whose virtual animal is no longer in the park (cancelled, sold, placed); returns how many.</summary>
        public int PruneIncubating(IReadOnlyCollection<ulong> alive)
        {
            var set = new HashSet<ulong>(alive);
            var gone = _incubating.Keys.Where(id => !set.Contains(id)).ToList();
            foreach (var id in gone) _incubating.Remove(id);
            return gone.Count;
        }

        /// <summary>Forgets animals that are no longer in the park; returns how many.</summary>
        public int Prune(IReadOnlyCollection<ulong> alive)
        {
            var set = new HashSet<ulong>(alive);
            var gone = _animals.Keys.Where(id => !set.Contains(id)).ToList();
            foreach (var id in gone) _animals.Remove(id);
            return gone.Count;
        }
    }
}
