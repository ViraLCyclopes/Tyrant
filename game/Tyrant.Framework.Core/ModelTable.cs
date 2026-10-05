using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// A model chosen for an animal: its mod, its LOD files (full paths, LOD order; missing LODs reuse the last; none = the
    /// game's own mesh) and its rig edit (null for none).
    /// </summary>
    public sealed class ModelSet
    {
        public ModelSet(string modId, string target, IReadOnlyList<string> lodPaths, IReadOnlyDictionary<string, RigOffset>? rig = null)
        {
            ModId = modId;
            Target = target;
            LodPaths = lodPaths;
            Rig = rig;
        }

        public string ModId { get; }
        public string Target { get; }
        public IReadOnlyList<string> LodPaths { get; }
        public IReadOnlyDictionary<string, RigOffset>? Rig { get; }
    }

    /// <summary>An enabled mod whose species model or rig edit a later mod in the load order overrides.</summary>
    public sealed class RigClash
    {
        public RigClash(string species, string loser, string winner)
        {
            Species = species;
            Loser = loser;
            Winner = winner;
        }

        public string Species { get; }
        public string Loser { get; }
        public string Winner { get; }
        public string Message => $"{Species}: its model or rig edit is overridden by '{Winner}' (later in the load order).";
    }

    /// <summary>
    /// Which model and rig edit an animal wears: its skin's (model or rig), else its species' entry, else its own. A species'
    /// model and rig edit are one unit (a model's bind poses are built for its rig): the later mod that sets either supplies both.
    /// </summary>
    public sealed class ModelTable
    {
        private const int MaxLods = 8;
        private readonly Dictionary<string, ModelSet> _species = new Dictionary<string, ModelSet>(StringComparer.Ordinal);
        private readonly Dictionary<string, ModelSet> _skins = new Dictionary<string, ModelSet>(StringComparer.Ordinal);

        public int Count => _species.Count + _skins.Count;

        public static ModelTable Build(IReadOnlyList<LoadedMod> mods) => Build(mods, out _);

        /// <param name="messages">Species whose model or rig edit a later mod overrides, for the log.</param>
        public static ModelTable Build(IReadOnlyList<LoadedMod> mods, out List<string> messages)
        {
            var table = new ModelTable();
            messages = new List<string>();
            foreach (var mod in mods)
            {
                foreach (var model in mod.Manifest.Models)
                {
                    var set = model.File.Length == 0
                        ? model.Rig is { Count: > 0 } ? new ModelSet(mod.Manifest.Id, model.Target, Array.Empty<string>(), model.Rig) : null
                        : SetOf(mod, model.Target, model.File, model.Rig);
                    if (set == null) continue;
                    if (table._species.TryGetValue(model.Target, out var earlier) && earlier.ModId != set.ModId)
                        messages.Add($"{model.Target}: {set.ModId} sets its model or rig edit, overriding {earlier.ModId} (the later mod in the load order wins; a model and its rig edit go together).");
                    table._species[model.Target] = set;
                }
                foreach (var skin in mod.Manifest.Skins)
                {
                    var set = skin.Model != null ? SetOf(mod, skin.Species, skin.Model, skin.Rig)
                        : skin.Rig is { Count: > 0 } ? new ModelSet(mod.Manifest.Id, skin.Species, Array.Empty<string>(), skin.Rig) : null;
                    if (set != null) table._skins[skin.Key(mod.Manifest.Id)] = set;
                }
            }
            return table;
        }

        /// <summary>Enabled mods (in load order) whose species model or rig edit a later one overrides, for the Mods tab.</summary>
        public static IReadOnlyList<RigClash> Clashes(IReadOnlyList<(string ModId, ModManifest Manifest)> enabledInLoadOrder)
        {
            var owner = new Dictionary<string, string>(StringComparer.Ordinal);
            var clashes = new List<RigClash>();
            foreach (var (id, manifest) in enabledInLoadOrder)
                foreach (var target in manifest.Models.Select(m => m.Target).Distinct(StringComparer.Ordinal))
                {
                    if (owner.TryGetValue(target, out var earlier) && earlier != id) clashes.Add(new RigClash(target, earlier, id));
                    owner[target] = id;
                }
            return clashes;
        }

        public bool TryChoose(string? speciesId, string? skinKey, out ModelSet set)
        {
            if (skinKey != null && _skins.TryGetValue(skinKey, out set!)) return true;
            if (speciesId != null && _species.TryGetValue(speciesId, out set!)) return true;
            set = null!;
            return false;
        }

        /// <summary>The model's built LOD files, in order; null when it has none or names a file outside its mod.</summary>
        private static ModelSet? SetOf(LoadedMod mod, string target, string file, IReadOnlyDictionary<string, RigOffset>? rig)
        {
            var lods = new List<string>();
            for (var lod = 0; lod < MaxLods; lod++)
            {
                string path;
                try
                {
                    path = Path.GetFullPath(Path.Combine(mod.Directory, ModelFiles.Lod(file, lod)));
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                {
                    return null;
                }
                if (!ModPaths.IsInside(path, mod.Directory)) return null;
                if (!File.Exists(path)) break;
                lods.Add(path);
            }
            return lods.Count == 0 ? null : new ModelSet(mod.Manifest.Id, target, lods, rig);
        }
    }
}
