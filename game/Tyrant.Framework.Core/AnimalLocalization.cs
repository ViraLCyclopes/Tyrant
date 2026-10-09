using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Tyrant.Framework.Core
{
    /// <summary>Database terms point into the mod's separate language files; game CSVs are never modified.</summary>
    public static class AnimalLocalization
    {
        public static readonly string[] Codes = { "en-US", "fr", "es", "pt", "de", "ru", "zh-CN" };
        public static void ValidateTerm(string term)
        {
            if (string.IsNullOrWhiteSpace(term) || Path.IsPathRooted(term) || term.Contains(":")) throw new ManifestException("Localization key must be a relative text-file key.");
            foreach (var part in term.Replace('\\', '/').Split('/'))
                if (part == "." || part == ".." || part.Length == 0 || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new ManifestException("Invalid localization text-file key: " + term);
        }
        public static string FileFor(string directory, string code, string term)
        {
            ValidateTerm(term);
            return Path.Combine(directory, "localization", code, term.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar) + ".txt");
        }
        public static Dictionary<string, object?> Parse(string text)
        {
            var terms = new Dictionary<string, object?>(StringComparer.Ordinal);
            string? key = null;
            var value = new StringBuilder();
            void Finish()
            {
                if (key == null) return;
                if (terms.ContainsKey(key)) throw new ManifestException("Duplicate localization key: " + key);
                terms.Add(key, value.ToString().TrimEnd('\n'));
                value.Clear();
            }
            foreach (var raw in text.Replace("\r\n", "\n").TrimStart('\uFEFF').Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    Finish(); key = line.Substring(1, line.Length - 2);
                    if (string.IsNullOrWhiteSpace(key)) throw new ManifestException("Empty localization key.");
                }
                else if (key == null)
                {
                    if (!string.IsNullOrWhiteSpace(line)) throw new ManifestException("Localization text must begin with a [key] section.");
                }
                else
                {
                    if (line.StartsWith("\\[", StringComparison.Ordinal) || line.StartsWith("\\\\", StringComparison.Ordinal)) line = line.Substring(1);
                    value.Append(line).Append('\n');
                }
            }
            Finish();
            return terms;
        }
        public static string Write(IReadOnlyDictionary<string, object?> terms)
        {
            var output = new StringBuilder();
            foreach (var term in terms)
            {
                if (term.Key.Contains("\n") || term.Key.Contains("\r") || term.Key.Contains("]") || string.IsNullOrWhiteSpace(term.Key)) throw new ManifestException("Invalid localization key.");
                if (!(term.Value is string text)) throw new ManifestException("Localization text must be a string: " + term.Key);
                output.Append('[').Append(term.Key).Append("]\n");
                foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
                {
                    if (line.StartsWith("\\", StringComparison.Ordinal) || (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))) output.Append('\\');
                    output.Append(line).Append('\n');
                }
                output.Append('\n');
            }
            return output.ToString();
        }
        public static void Load(string directory, IEnumerable<SpeciesEntry> species)
        {
            var animals = species.ToList();
            foreach (var animal in animals) animal.Texts.Clear();
            for (int i = 0; i < Codes.Length; i++)
            {
                var file = Path.Combine(directory, "localization", Codes[i] + ".txt");
                // Read older Tyrant language files for compatibility; new mods write plain text only.
                var plain = File.Exists(file);
                if (!plain) file = Path.Combine(directory, "localization", Codes[i] + ".json");
                Dictionary<string, object?> terms;
                try { terms = !File.Exists(file) ? new Dictionary<string, object?>() : plain ? Parse(File.ReadAllText(file)) : Json.Parse(File.ReadAllText(file)) as Dictionary<string, object?> ?? throw new ManifestException("Expected an object of localization terms."); }
                catch (FormatException ex) { throw new ManifestException(file + ": " + ex.Message); }
                foreach (var pair in terms) if (!(pair.Value is string)) throw new ManifestException(file + ": localization text must be a string: " + pair.Key);
                foreach (var animal in animals)
                    foreach (var term in animal.Localization)
                    {
                        var separate = FileFor(directory, Codes[i], term.Value);
                        string? text = File.Exists(separate) ? File.ReadAllText(separate) : terms.TryGetValue(term.Value, out var legacy) ? (string)legacy! : null;
                        if (text != null) animal.Texts.Add(new AnimalText { Language = AnimalText.Languages[i], Key = term.Key, Text = term.Key == "name" ? text.Trim() : text });
                    }
            }
        }
    }
}
