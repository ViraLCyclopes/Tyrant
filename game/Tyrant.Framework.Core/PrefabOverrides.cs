using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tyrant.Framework.Core
{
    /// <summary>One overridden field of a prefab component: where it is, and its new value (a Json value).</summary>
    public sealed class PrefabField
    {
        /// <summary>The GameObject's path from the prefab root, names joined by '/'; "" is the root.</summary>
        public string Object { get; set; } = "";

        /// <summary>The component's simple type name, with "#n" (0-based) for the n-th of that type on the object.</summary>
        public string Component { get; set; } = "";

        /// <summary>Serialized field names joined by '.', with "[i]" for list or array elements.</summary>
        public string Field { get; set; } = "";

        /// <summary>double, bool, string, or a Dictionary&lt;string, object?&gt; for vectors and colours.</summary>
        public object? Value { get; set; }

        public string Label => (Object.Length == 0 ? "" : Object + " ") + Component + "." + Field;

        internal (string, string, string) Key => (Object, Component, Field);
    }

    /// <summary>A mod's prefab overrides for one species (prefabs/&lt;file&gt;.json); mod.json does not list them.</summary>
    public sealed class PrefabOverrides
    {
        public const string Folder = "prefabs";
        public const int Format = 1;

        public string Target { get; set; } = "";
        public List<PrefabField> Fields { get; } = new List<PrefabField>();

        public static PrefabOverrides Parse(string json)
        {
            object? root;
            try
            {
                root = Json.Parse(json);
            }
            catch (FormatException ex)
            {
                throw new ManifestException("It is not valid JSON: " + ex.Message);
            }
            if (!(root is Dictionary<string, object?> map)) throw new ManifestException("It must be a JSON object.");
            if (!(map.TryGetValue("format", out var format) && format is double f && f == Format))
                throw new ManifestException($"\"format\" must be {Format}.");
            if (!(map.TryGetValue("target", out var target) && target is string t && t.Trim().Length > 0))
                throw new ManifestException("\"target\" must name a species.");
            var result = new PrefabOverrides { Target = t.Trim() };
            if (map.TryGetValue("fields", out var fields) && fields != null)
            {
                if (!(fields is List<object?> list)) throw new ManifestException("\"fields\" must be a list.");
                var n = 0;
                foreach (var item in list)
                {
                    n++;
                    if (!(item is Dictionary<string, object?> entry)) throw new ManifestException($"Field {n} must be an object.");
                    string Text(string key, bool allowEmpty) =>
                        entry.TryGetValue(key, out var v) && v is string s && (allowEmpty || s.Length > 0)
                            ? s : throw new ManifestException($"Field {n} needs \"{key}\".");
                    if (!entry.TryGetValue("value", out var value)) throw new ManifestException($"Field {n} needs \"value\".");
                    result.Fields.Add(new PrefabField { Object = Text("object", true), Component = Text("component", false), Field = Text("field", false), Value = value });
                }
            }
            return result;
        }

        public string ToJson()
        {
            var map = new Dictionary<string, object?>
            {
                ["format"] = Format,
                ["target"] = Target,
                ["fields"] = Fields.Select(f => (object?)new Dictionary<string, object?>
                {
                    ["object"] = f.Object, ["component"] = f.Component, ["field"] = f.Field, ["value"] = f.Value,
                }).ToList(),
            };
            return Json.Write(map) + "\n";
        }

        /// <summary>The file a target's overrides go in: its lowercased name made safe for a file name.</summary>
        public static string FileName(string target)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var safe = new string(target.ToLowerInvariant().Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray()).TrimEnd('.', ' ');
            return (safe.Length == 0 ? "_" : safe) + ".json";
        }

        /// <summary>Every prefabs/*.json of a mod, by file name; a broken file or a second file for a target is skipped and noted.</summary>
        public static List<(string File, PrefabOverrides Overrides)> ReadFolder(string modDir, List<string>? problems)
        {
            var result = new List<(string, PrefabOverrides)>();
            var folder = Path.Combine(modDir, Folder);
            if (!Directory.Exists(folder)) return result;
            foreach (var path in Directory.GetFiles(folder, "*.json").OrderBy(p => p, StringComparer.Ordinal))
            {
                var file = Path.GetFileName(path);
                try
                {
                    var overrides = Parse(File.ReadAllText(path));
                    var first = result.FirstOrDefault(r => string.Equals(r.Item2.Target, overrides.Target, StringComparison.OrdinalIgnoreCase));
                    if (first.Item2 != null)
                    {
                        problems?.Add($"prefabs/{file} also sets {overrides.Target}, which prefabs/{first.Item1} already sets; it was skipped. Merge them into one file.");
                        continue;
                    }
                    result.Add((file, overrides));
                }
                catch (Exception ex) when (ex is ManifestException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    problems?.Add($"prefabs/{file} was skipped: {ex.Message}");
                }
            }
            return result;
        }
    }

    public sealed class PrefabClash
    {
        public PrefabClash(string target, string label, string loser, string winner)
        {
            Target = target;
            Label = label;
            Loser = loser;
            Winner = winner;
        }

        public string Target { get; }
        public string Label { get; }
        public string Loser { get; }
        public string Winner { get; }
        public string Message => $"{Label} of {Target} is set by both {Loser} and {Winner}; {Winner} wins.";
    }

    /// <summary>Every enabled mod's prefab overrides by target; the later mod in load order wins per field.</summary>
    public sealed class PrefabTable
    {
        private readonly Dictionary<string, List<PrefabField>> _targets = new Dictionary<string, List<PrefabField>>(StringComparer.OrdinalIgnoreCase);

        public bool HasAny => _targets.Count > 0;

        public IReadOnlyList<PrefabField>? For(string? target) =>
            target != null && _targets.TryGetValue(target, out var fields) ? fields : null;

        public static PrefabTable Build(IReadOnlyList<LoadedMod> mods, out List<string> messages)
        {
            var table = new PrefabTable();
            var problems = new List<string>();
            var clashes = Merge(mods.Select(m => (m.Manifest.Id, m.Directory)).ToList(), table._targets, problems);
            messages = problems.Concat(clashes.Select(c => c.Message)).ToList();
            return table;
        }

        public static IReadOnlyList<PrefabClash> Clashes(IReadOnlyList<(string ModId, string Directory)> enabledInLoadOrder) =>
            Merge(enabledInLoadOrder, new Dictionary<string, List<PrefabField>>(StringComparer.OrdinalIgnoreCase), new List<string>());

        private static List<PrefabClash> Merge(IReadOnlyList<(string ModId, string Directory)> mods, Dictionary<string, List<PrefabField>> into, List<string> problems)
        {
            var owner = new Dictionary<(string, string, string, string), string>();
            var clashes = new List<PrefabClash>();
            foreach (var (modId, directory) in mods)
            {
                var modProblems = new List<string>();
                foreach (var (_, overrides) in PrefabOverrides.ReadFolder(directory, modProblems))
                {
                    if (!into.TryGetValue(overrides.Target, out var fields)) into[overrides.Target] = fields = new List<PrefabField>();
                    foreach (var field in overrides.Fields)
                    {
                        var key = (overrides.Target.ToLowerInvariant(), field.Object, field.Component, field.Field);
                        if (owner.TryGetValue(key, out var earlier) && earlier != modId)
                            clashes.Add(new PrefabClash(overrides.Target, field.Label, earlier, modId));
                        owner[key] = modId;
                        fields.RemoveAll(f => f.Key == field.Key);
                        fields.Add(field);
                    }
                }
                problems.AddRange(modProblems.Select(p => $"{modId}: {p}"));
            }
            return clashes;
        }
    }
}
