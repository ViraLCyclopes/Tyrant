using System;
using System.Collections.Generic;
using System.IO;

namespace Tyrant.Framework.Core
{
    /// <summary>A model chosen for an animal: its mod and its LOD files (full paths, LOD order; missing LODs reuse the last).</summary>
    public sealed class ModelSet
    {
        public ModelSet(string modId, string target, IReadOnlyList<string> lodPaths)
        {
            ModId = modId;
            Target = target;
            LodPaths = lodPaths;
        }

        public string ModId { get; }
        public string Target { get; }
        public IReadOnlyList<string> LodPaths { get; }
    }

    /// <summary>Which model an animal wears: its skin's model, else its species' replacement, else its own. Later mods win.</summary>
    public sealed class ModelTable
    {
        private const int MaxLods = 8;
        private readonly Dictionary<string, ModelSet> _species = new Dictionary<string, ModelSet>(StringComparer.Ordinal);
        private readonly Dictionary<string, ModelSet> _skins = new Dictionary<string, ModelSet>(StringComparer.Ordinal);

        public int Count => _species.Count + _skins.Count;

        public static ModelTable Build(IReadOnlyList<LoadedMod> mods)
        {
            var table = new ModelTable();
            foreach (var mod in mods)
            {
                foreach (var model in mod.Manifest.Models)
                    if (SetOf(mod, model.Target, model.File) is ModelSet set) table._species[model.Target] = set;
                foreach (var skin in mod.Manifest.Skins)
                    if (skin.Model != null && SetOf(mod, skin.Species, skin.Model) is ModelSet set) table._skins[skin.Key(mod.Manifest.Id)] = set;
            }
            return table;
        }

        public bool TryChoose(string? speciesId, string? skinKey, out ModelSet set)
        {
            if (skinKey != null && _skins.TryGetValue(skinKey, out set!)) return true;
            if (speciesId != null && _species.TryGetValue(speciesId, out set!)) return true;
            set = null!;
            return false;
        }

        /// <summary>The model's built LOD files, in order; null when it has none or names a file outside its mod.</summary>
        private static ModelSet? SetOf(LoadedMod mod, string target, string file)
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
            return lods.Count == 0 ? null : new ModelSet(mod.Manifest.Id, target, lods);
        }
    }
}
