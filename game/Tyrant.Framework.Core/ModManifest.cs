using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Tyrant.Framework.Core
{
    public static class FrameworkInfo
    {
        /// <summary>The framework version mods compare their "requires": { "tyrant": ... } against.</summary>
        public const string Version = "0.1.0";
    }

    public static class ModId
    {
        /// <summary>3–64 characters of a-z, 0-9 and '-', not starting or ending with '-'; also the mod's folder name.</summary>
        public static bool IsValid(string? id) =>
            id != null && id.Length >= 3 && id.Length <= 64 && id[0] != '-' && id[id.Length - 1] != '-'
            && id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
    }

    public sealed class ManifestException : Exception
    {
        public ManifestException(string message) : base(message) { }
    }

    /// <summary>One "replace" entry: swap the game texture named Texture for the mod's PNG at File.</summary>
    public sealed class TextureReplacement
    {
        /// <summary>The texture's Unity object name; what the framework matches on.</summary>
        public string Texture { get; set; } = "";

        /// <summary>Addressables key (container path), recorded for an Addressables-level hook later.</summary>
        public string? Key { get; set; }

        /// <summary>Addressables GUID, recorded for an Addressables-level hook later.</summary>
        public string? Guid { get; set; }

        /// <summary>The PNG, relative to the mod folder.</summary>
        public string File { get; set; } = "";
    }

    /// <summary>A mod's mod.json: who it is, what it needs, and what each content module should do.</summary>
    public sealed class ModManifest
    {
        public const string FileName = "mod.json";
        public const int CurrentFormat = 1;

        public int Format { get; set; } = CurrentFormat;
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "1.0.0";
        public string? Author { get; set; }
        public string? Description { get; set; }

        /// <summary>The lowest framework version this mod works with, e.g. "0.1" or ">=0.1".</summary>
        public string? RequiresTyrant { get; set; }

        public List<string> Dependencies { get; } = new List<string>();

        /// <summary>A code mod's DLL, relative to the mod folder.</summary>
        public string? Assembly { get; set; }

        public List<TextureReplacement> Replace { get; } = new List<TextureReplacement>();

        public static ModManifest Parse(string json)
        {
            object? root;
            try
            {
                root = Json.Parse(json);
            }
            catch (FormatException ex)
            {
                throw new ManifestException("mod.json is not valid JSON: " + ex.Message);
            }
            if (!(root is Dictionary<string, object?> map)) throw new ManifestException("mod.json must be a JSON object.");

            var format = Integer(map, "format") ?? throw new ManifestException("mod.json has no \"format\" (use 1).");
            if (format > CurrentFormat)
                throw new ManifestException($"mod.json format {format} needs a newer Tyrant framework (this one reads format {CurrentFormat}).");
            if (format < 1) throw new ManifestException($"mod.json format {format} is not valid (use 1).");

            var id = Text(map, "id") ?? "";
            if (!ModId.IsValid(id))
                throw new ManifestException($"\"id\" must be 3–64 lowercase letters, digits or '-' (got \"{id}\").");

            var manifest = new ModManifest
            {
                Format = format,
                Id = id,
                Name = Text(map, "name") is { Length: > 0 } name ? name : id,
                Version = Text(map, "version") is { Length: > 0 } version ? version : "1.0.0",
                Author = Text(map, "author"),
                Description = Text(map, "description"),
                Assembly = Text(map, "assembly"),
                RequiresTyrant = map.TryGetValue("requires", out var requires) && requires is Dictionary<string, object?> r ? Text(r, "tyrant") : null,
            };

            foreach (var dependency in Array(map, "dependencies"))
                manifest.Dependencies.Add(dependency as string ?? throw new ManifestException("\"dependencies\" must list mod ids as strings."));

            var n = 0;
            foreach (var item in Array(map, "replace"))
            {
                n++;
                if (!(item is Dictionary<string, object?> entry)) throw new ManifestException($"\"replace\" entry {n} must be an object.");
                var texture = Text(entry, "texture");
                if (string.IsNullOrWhiteSpace(texture)) throw new ManifestException($"\"replace\" entry {n} has no \"texture\".");
                var file = Text(entry, "file");
                if (string.IsNullOrWhiteSpace(file)) throw new ManifestException($"\"replace\" entry {n} ({texture}) has no \"file\".");
                manifest.Replace.Add(new TextureReplacement { Texture = texture!, File = file!, Key = Text(entry, "key"), Guid = Text(entry, "guid") });
            }
            return manifest;
        }

        public string ToJson()
        {
            var map = new Dictionary<string, object?> { ["format"] = Format, ["id"] = Id, ["name"] = Name, ["version"] = Version };
            if (Author != null) map["author"] = Author;
            if (Description != null) map["description"] = Description;
            if (RequiresTyrant != null) map["requires"] = new Dictionary<string, object?> { ["tyrant"] = RequiresTyrant };
            if (Dependencies.Count > 0) map["dependencies"] = Dependencies.Cast<object?>().ToList();
            if (Assembly != null) map["assembly"] = Assembly;
            map["replace"] = Replace.Select(r =>
            {
                var entry = new Dictionary<string, object?> { ["texture"] = r.Texture };
                if (r.Key != null) entry["key"] = r.Key;
                if (r.Guid != null) entry["guid"] = r.Guid;
                entry["file"] = r.File;
                return (object?)entry;
            }).ToList();
            return Json.Write(map) + "\n";
        }

        private static string? Text(Dictionary<string, object?> map, string key) =>
            map.TryGetValue(key, out var value) ? value as string : null;

        private static int? Integer(Dictionary<string, object?> map, string key) =>
            map.TryGetValue(key, out var value) && value is double d && d == Math.Floor(d) ? (int)d : (int?)null;

        private static IEnumerable<object?> Array(Dictionary<string, object?> map, string key)
        {
            if (!map.TryGetValue(key, out var value) || value == null) return System.Array.Empty<object?>();
            return value as List<object?> ?? throw new ManifestException($"\"{key}\" must be a list.");
        }
    }
}
