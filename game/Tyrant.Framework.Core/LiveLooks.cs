using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// What the in-game editor knows about one animal: its species, its skin (a mod skin, or a vanilla skin's
    /// "&lt;species&gt;/&lt;skin name&gt;"), its variant (normal, albino, melanistic or leucistic), its persistent id (null when it has
    /// none, like the Nursery preview), the material Tyrant changes and where the skin comes from ("vanilla" or the mod's name).
    /// </summary>
    public sealed record AnimalContext(string Species, (string ModId, string SkinId)? ModSkin, string? VanillaSkinKey, string Variant,
        ulong? PersistentId, string MaterialName, string Source, string? CustomSpeciesModId)
    {
        /// <summary>The skin's key: "&lt;mod&gt;/&lt;skin&gt;" for a mod skin, else the vanilla skin's key.</summary>
        public string? SkinKey => ModSkin is { } skin ? skin.ModId + "/" + skin.SkinId : VanillaSkinKey;

        /// <summary>The key a live edit of this scope is kept under; null when the animal cannot have one (no persistent id, no skin).</summary>
        public string? KeyFor(LookScope scope) => scope switch
        {
            LookScope.Animal => PersistentId?.ToString(CultureInfo.InvariantCulture),
            LookScope.Skin => SkinKey,
            _ => Species,
        };
    }

    /// <summary>
    /// The in-game editor's unsaved edits, one block per scope, key and material, on top of the table until saved. Animal keys
    /// are persistent ids; skin keys are a mod skin's or a vanilla skin's (any case); species keys are species ids.
    /// </summary>
    public sealed class LiveLooks
    {
        private readonly Dictionary<ulong, Dictionary<string, MaterialOverride>> _animals = new Dictionary<ulong, Dictionary<string, MaterialOverride>>();
        private readonly Dictionary<string, Dictionary<string, MaterialOverride>> _skins = new Dictionary<string, Dictionary<string, MaterialOverride>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, MaterialOverride>> _species = new Dictionary<string, Dictionary<string, MaterialOverride>>(StringComparer.Ordinal);

        public bool IsEmpty => _animals.Count == 0 && _skins.Count == 0 && _species.Count == 0;
        public bool HasAnimals => _animals.Count > 0;
        public bool HasSkins => _skins.Count > 0;
        public bool HasSpecies => _species.Count > 0;

        /// <summary>Null or an empty block clears that edit. An animal key that is not an id throws <see cref="ArgumentException"/>.</summary>
        public void Set(LookScope scope, string key, string materialName, MaterialOverride? block)
        {
            if (scope == LookScope.Animal)
            {
                if (!ulong.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                    throw new ArgumentException($"\"{key}\" is not an animal id.", nameof(key));
                Put(_animals, id, materialName, block);
            }
            else Put(scope == LookScope.Skin ? _skins : _species, key, materialName, block);
        }

        /// <summary>True when this animal, skin or species has a live block (any material); cheap, before a material is read.</summary>
        public bool CoversAnimal(ulong id) => _animals.ContainsKey(id);

        public bool CoversSkin(string? key) => key != null && _skins.ContainsKey(key);

        public bool CoversSpecies(string? id) => id != null && _species.ContainsKey(id);

        public MaterialOverride? Animal(ulong id, string materialName) => Find(_animals, id, materialName);

        public MaterialOverride? Skin(string? key, string materialName) => key == null ? null : Find(_skins, key, materialName);

        public MaterialOverride? Species(string? id, string materialName) => id == null ? null : Find(_species, id, materialName);

        /// <summary>Every live block, e.g. to tell which ramps and masks are still in use.</summary>
        public IEnumerable<MaterialOverride> Blocks =>
            _animals.Values.Concat(_skins.Values).Concat(_species.Values).SelectMany(materials => materials.Values);

        public void Clear()
        {
            _animals.Clear();
            _skins.Clear();
            _species.Clear();
        }

        private static void Put<TKey>(Dictionary<TKey, Dictionary<string, MaterialOverride>> map, TKey key, string material, MaterialOverride? block)
        {
            if (block == null || block.IsEmpty)
            {
                if (map.TryGetValue(key, out var existing) && existing.Remove(material) && existing.Count == 0) map.Remove(key);
                return;
            }
            if (!map.TryGetValue(key, out var materials)) map[key] = materials = new Dictionary<string, MaterialOverride>(StringComparer.Ordinal);
            materials[material] = block;
        }

        private static MaterialOverride? Find<TKey>(Dictionary<TKey, Dictionary<string, MaterialOverride>> map, TKey key, string material) =>
            map.TryGetValue(key, out var materials) && materials.TryGetValue(material, out var block) ? block : null;
    }
}
