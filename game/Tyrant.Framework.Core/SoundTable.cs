using System;
using System.Collections.Generic;
using System.IO;

namespace Tyrant.Framework.Core
{
    /// <summary>One sound replacement chosen for a play: its mod, its files (full paths) and settings.</summary>
    public sealed class SoundChoice
    {
        private int _last = -1;

        public SoundChoice(string modId, string eventPath, IReadOnlyList<string> filePaths, double volume, double agePitch, bool unique)
        {
            ModId = modId;
            Event = eventPath;
            FilePaths = filePaths;
            Volume = volume;
            AgePitch = agePitch;
            Unique = unique;
        }

        public string ModId { get; }
        public string Event { get; }
        public IReadOnlyList<string> FilePaths { get; }
        public double Volume { get; }
        public double AgePitch { get; }
        public bool Unique { get; }

        /// <summary>A file at random, never the same one twice in a row when there are several.</summary>
        public string NextFile(Random random)
        {
            if (FilePaths.Count == 1) return FilePaths[0];
            var next = random.Next(FilePaths.Count - (_last >= 0 ? 1 : 0));
            if (_last >= 0 && next >= _last) next++;
            _last = next;
            return FilePaths[next];
        }
    }

    /// <summary>Which sound plays for a game event: a skin's, else its species', else the one for everyone. Later mods win.</summary>
    public sealed class SoundTable
    {
        public static readonly SoundTable Empty = new SoundTable();

        private readonly Dictionary<string, SoundChoice> _everyone = new Dictionary<string, SoundChoice>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(string Event, string Species), SoundChoice> _species = new Dictionary<(string, string), SoundChoice>(new KeyComparer());
        private readonly Dictionary<(string Event, string Skin), SoundChoice> _skins = new Dictionary<(string, string), SoundChoice>(new KeyComparer());
        private readonly HashSet<string> _mentioned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public int Count => _everyone.Count + _species.Count + _skins.Count;
        public int UniqueCount => _species.Count + _skins.Count;

        public static SoundTable Build(IEnumerable<LoadedMod> mods, List<string> warnings)
        {
            var table = new SoundTable();
            foreach (var mod in mods)
                foreach (var sound in mod.Manifest.Sounds)
                {
                    var files = new List<string>();
                    foreach (var file in sound.Files)
                    {
                        var path = FullPath(mod.Directory, file);
                        if (path == null || !ModPaths.IsInside(path, mod.Directory))
                            warnings.Add($"{mod.Manifest.Id}: sound {sound.Event}: {file} is outside the mod; it is ignored.");
                        else if (!File.Exists(path))
                            warnings.Add($"{mod.Manifest.Id}: sound {sound.Event}: {file} is missing; it is ignored.");
                        else files.Add(path);
                    }
                    if (files.Count == 0) continue;
                    var choice = new SoundChoice(mod.Manifest.Id, sound.Event, files, sound.Volume, sound.AgePitch, sound.IsUnique);
                    if (sound.Skin != null) table._skins[(sound.Event, sound.Skin)] = choice;
                    else if (sound.Species != null) table._species[(sound.Event, sound.Species)] = choice;
                    else table._everyone[sound.Event] = choice;
                    table._mentioned.Add(sound.Event);
                    if (sound.IsUnique) table._unique.Add(sound.Event);
                }
            return table;
        }

        /// <summary>Any replacement for this event (a fast first check for every sound the game plays).</summary>
        public bool Mentions(string eventPath) => _mentioned.Contains(eventPath);

        /// <summary>A species or skin replacement exists for this event (then the animal playing it matters).</summary>
        public bool HasUnique(string eventPath) => _unique.Contains(eventPath);

        public SoundChoice? Choose(string eventPath, string? speciesId, string? skinKey)
        {
            if (skinKey != null && _skins.TryGetValue((eventPath, skinKey), out var skin)) return skin;
            if (speciesId != null && _species.TryGetValue((eventPath, speciesId), out var species)) return species;
            return _everyone.TryGetValue(eventPath, out var everyone) ? everyone : null;
        }

        private static string? FullPath(string dir, string file)
        {
            try
            {
                return Path.GetFullPath(Path.Combine(dir, file));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return null;
            }
        }

        /// <summary>Event paths ignore case; species ids and skin keys are exact.</summary>
        private sealed class KeyComparer : IEqualityComparer<(string, string)>
        {
            public bool Equals((string, string) x, (string, string) y) =>
                string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Item2, y.Item2, StringComparison.Ordinal);

            public int GetHashCode((string, string) key) =>
                StringComparer.OrdinalIgnoreCase.GetHashCode(key.Item1) * 31 + StringComparer.Ordinal.GetHashCode(key.Item2);
        }
    }

    /// <summary>How much higher a young animal plays a replacement: 1 + agePitch × 0.5 × (1 − maturity).</summary>
    public static class AgePitch
    {
        public static float For(double agePitch, float maturity01)
        {
            var maturity = Math.Max(0f, Math.Min(1f, maturity01));
            return (float)(1 + Math.Max(0, Math.Min(1, agePitch)) * 0.5 * (1 - maturity));
        }
    }
}
