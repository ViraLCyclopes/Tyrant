using System;
using System.Collections.Generic;
using System.IO;

namespace Tyrant.Framework.Core
{
    public static class ModPaths
    {
        /// <summary>True when path, once resolved, lies inside directory: mods may only use their own files.</summary>
        public static bool IsInside(string path, string directory)
        {
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class Replacement
    {
        public Replacement(string modId, string texture, string filePath)
        {
            ModId = modId;
            Texture = texture;
            FilePath = filePath;
        }

        public string ModId { get; }
        public string Texture { get; }

        /// <summary>The PNG's full path.</summary>
        public string FilePath { get; }
    }

    /// <summary>Texture name → the replacement that wins (later in load order).</summary>
    public sealed class ReplacementTable
    {
        private readonly Dictionary<string, Replacement> _byTexture = new Dictionary<string, Replacement>(StringComparer.OrdinalIgnoreCase);

        public static ReplacementTable Empty { get; } = new ReplacementTable();

        public int Count => _byTexture.Count;

        public bool TryGet(string textureName, out Replacement replacement) => _byTexture.TryGetValue(textureName, out replacement!);

        public static ReplacementTable Build(IEnumerable<LoadedMod> mods, out List<string> messages)
        {
            var table = new ReplacementTable();
            messages = new List<string>();
            foreach (var mod in mods)
            {
                foreach (var entry in mod.Manifest.Replace)
                {
                    string path;
                    try
                    {
                        path = Path.GetFullPath(Path.Combine(mod.Directory, entry.File));
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException)
                    {
                        messages.Add($"{mod.Manifest.Id}: \"{entry.File}\" is not a valid file path; {entry.Texture} stays vanilla.");
                        continue;
                    }
                    if (!ModPaths.IsInside(path, mod.Directory))
                    {
                        messages.Add($"{mod.Manifest.Id}: \"{entry.File}\" points outside the mod folder; {entry.Texture} stays vanilla.");
                        continue;
                    }
                    if (table._byTexture.TryGetValue(entry.Texture, out var earlier) && earlier.ModId != mod.Manifest.Id)
                        messages.Add($"{entry.Texture}: {mod.Manifest.Id} replaces it, overriding {earlier.ModId} (the later mod in load order wins).");
                    table._byTexture[entry.Texture] = new Replacement(mod.Manifest.Id, entry.Texture, path);
                }
            }
            return table;
        }
    }
}
