using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// The looks tuned for single animals in one park: { "format":1, "animals": { "&lt;id&gt;": &lt;materials map&gt; } }.
    /// Lives in UserData/Tyrant/Looks/&lt;park&gt;.json; nothing goes into the park save.
    /// </summary>
    public sealed class AnimalLooks
    {
        private readonly SortedDictionary<ulong, Dictionary<string, MaterialOverride>> _animals = new SortedDictionary<ulong, Dictionary<string, MaterialOverride>>();

        public int Count => _animals.Count;

        /// <summary>A park's file, or null when the park has no name.</summary>
        public static string? PathFor(string tyrantDataDir, string parkName) =>
            string.IsNullOrWhiteSpace(parkName) ? null : Path.Combine(tyrantDataDir, "Looks", MaterialFolders.Sanitize(parkName.Trim()) + ".json");

        /// <summary>A missing file is an empty store; a bad file or animal adds a problem and is skipped.</summary>
        public static AnimalLooks Read(string path, List<string> problems)
        {
            var looks = new AnimalLooks();
            if (!File.Exists(path)) return looks;
            try
            {
                if (!(Json.Parse(File.ReadAllText(path)) is Dictionary<string, object?> root)) throw new ManifestException("It must be a JSON object.");
                if (!(root.TryGetValue("format", out var format) && format is double f && f == 1)) throw new ManifestException("\"format\" must be 1.");
                if (root.TryGetValue("animals", out var animals) && animals != null)
                {
                    if (!(animals is Dictionary<string, object?> map)) throw new ManifestException("\"animals\" must be an object.");
                    foreach (var pair in map)
                    {
                        if (!ulong.TryParse(pair.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                        {
                            problems.Add($"{path}: animal \"{pair.Key}\" is not an id.");
                            continue;
                        }
                        try
                        {
                            if (MaterialOverrides.Parse(pair.Value, $"Animal {pair.Key}", true) is { } materials) looks._animals[id] = materials;
                        }
                        catch (ManifestException ex)
                        {
                            problems.Add($"{path}: {ex.Message}");
                        }
                    }
                }
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

        public void Write(string path)
        {
            var animals = new Dictionary<string, object?>();
            foreach (var pair in _animals) animals[pair.Key.ToString(CultureInfo.InvariantCulture)] = MaterialOverrides.ToJson(pair.Value);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            AtomicFile.Write(path, Json.Write(new Dictionary<string, object?> { ["format"] = 1, ["animals"] = animals }) + "\n");
        }

        public Dictionary<string, MaterialOverride>? Get(ulong id) => _animals.TryGetValue(id, out var materials) ? materials : null;

        /// <summary>Null or a map with nothing in it removes the animal's look.</summary>
        public void Set(ulong id, Dictionary<string, MaterialOverride>? materials)
        {
            if (materials == null || materials.Values.All(m => m.IsEmpty)) _animals.Remove(id);
            else _animals[id] = materials;
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
