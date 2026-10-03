using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Tyrant.Framework.Core
{
    /// <summary>One folder in UserData/Tyrant/Mods: its name, full path and mod.json text (null when there is none).</summary>
    public sealed class ModSource
    {
        public ModSource(string folder, string directory, string? manifestJson)
        {
            Folder = folder;
            Directory = directory;
            ManifestJson = manifestJson;
        }

        public string Folder { get; }
        public string Directory { get; }
        public string? ManifestJson { get; }
    }

    public sealed class LoadedMod
    {
        public LoadedMod(ModManifest manifest, string directory)
        {
            Manifest = manifest;
            Directory = directory;
        }

        public ModManifest Manifest { get; }
        public string Directory { get; }
    }

    public sealed class SkippedMod
    {
        public SkippedMod(string id, string reason)
        {
            Id = id;
            Reason = reason;
        }

        public string Id { get; }
        public string Reason { get; }
    }

    public sealed class LoadPlan
    {
        public List<LoadedMod> Mods { get; } = new List<LoadedMod>();
        public List<SkippedMod> Skipped { get; } = new List<SkippedMod>();
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>Decides which mods load and in what order; a broken or unmet mod is skipped with a reason, never fatal.</summary>
    public static class ModCatalog
    {
        public static LoadPlan Plan(IEnumerable<ModSource> sources, string? modsJson, string frameworkVersion)
        {
            var plan = new LoadPlan();
            List<ModListEntry> listed;
            try
            {
                listed = ModList.Parse(modsJson);
            }
            catch (FormatException ex)
            {
                plan.Warnings.Add($"UserData/Tyrant/{ModList.FileName} could not be read ({ex.Message}); every installed mod is loaded, sorted by id.");
                listed = new List<ModListEntry>();
            }

            var parsed = new Dictionary<string, LoadedMod>(StringComparer.Ordinal);
            foreach (var source in sources)
            {
                if (source.ManifestJson == null)
                {
                    plan.Skipped.Add(new SkippedMod(source.Folder, "mod.json is missing."));
                    continue;
                }
                try
                {
                    var manifest = ModManifest.Parse(source.ManifestJson);
                    if (manifest.Id != source.Folder)
                        plan.Skipped.Add(new SkippedMod(source.Folder, $"its id \"{manifest.Id}\" does not match its folder \"{source.Folder}\"."));
                    else
                        parsed[manifest.Id] = new LoadedMod(manifest, source.Directory);
                }
                catch (ManifestException ex)
                {
                    plan.Skipped.Add(new SkippedMod(source.Folder, ex.Message));
                }
            }

            var order = listed.Where(e => parsed.ContainsKey(e.Id)).Select(e => e.Id)
                .Concat(parsed.Keys.Where(id => listed.All(e => e.Id != id)).OrderBy(id => id, StringComparer.Ordinal))
                .ToList();
            var candidates = new List<LoadedMod>();
            foreach (var id in order)
            {
                var mod = parsed[id];
                var entry = listed.FirstOrDefault(e => e.Id == id);
                if (entry != null && !entry.Enabled)
                {
                    plan.Skipped.Add(new SkippedMod(id, "turned off"));
                    continue;
                }
                var requires = mod.Manifest.RequiresTyrant?.Trim().TrimStart('>', '=').Trim();
                if (!string.IsNullOrEmpty(requires) && VersionText.Compare(frameworkVersion, requires!) < 0)
                {
                    plan.Skipped.Add(new SkippedMod(id, $"needs Tyrant framework {requires} or newer (installed: {frameworkVersion})."));
                    continue;
                }
                candidates.Add(mod);
            }

            // Drop mods whose dependencies are not loading, until nothing changes (a dropped mod can strand its dependents).
            bool dropped;
            do
            {
                dropped = false;
                foreach (var mod in candidates.ToList())
                {
                    var missing = mod.Manifest.Dependencies.FirstOrDefault(d => candidates.All(c => c.Manifest.Id != d));
                    if (missing == null) continue;
                    candidates.Remove(mod);
                    plan.Skipped.Add(new SkippedMod(mod.Manifest.Id, $"needs mod \"{missing}\", which is not installed or is turned off."));
                    dropped = true;
                }
            } while (dropped);

            plan.Mods.AddRange(candidates);
            return plan;
        }
    }

    public static class VersionText
    {
        /// <summary>Compares dotted versions number by number ("0.10" &gt; "0.9"); missing parts count as 0.</summary>
        public static int Compare(string a, string b)
        {
            var x = a.Split('.');
            var y = b.Split('.');
            for (var k = 0; k < Math.Max(x.Length, y.Length); k++)
            {
                var p = k < x.Length && int.TryParse(x[k], NumberStyles.None, CultureInfo.InvariantCulture, out var i) ? i : 0;
                var q = k < y.Length && int.TryParse(y[k], NumberStyles.None, CultureInfo.InvariantCulture, out var j) ? j : 0;
                if (p != q) return p.CompareTo(q);
            }
            return 0;
        }
    }
}
