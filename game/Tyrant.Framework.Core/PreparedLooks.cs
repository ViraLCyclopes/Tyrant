using System;
using System.Collections.Generic;
using System.Linq;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// The park's per-animal looks as the framework draws them: each animal's map passed through a prepare step (files made
    /// full, ramps and masks pooled), kept while the store hands out the same map for that animal. A new store, or a save of
    /// one animal, prepares every look at once, so the looks that did not change keep their objects (and their textures)
    /// through the next sweep.
    /// </summary>
    public sealed class PreparedLooks
    {
        private readonly Func<MaterialOverride, MaterialOverride> _prepare;
        private readonly Dictionary<ulong, (Dictionary<string, MaterialOverride> Source, Dictionary<string, MaterialOverride> Prepared)> _looks =
            new Dictionary<ulong, (Dictionary<string, MaterialOverride>, Dictionary<string, MaterialOverride>)>();
        private AnimalLooks? _store;

        public PreparedLooks(Func<MaterialOverride, MaterialOverride> prepare) => _prepare = prepare;

        /// <summary>Uses this store (null: none): its looks are prepared now; an animal's look that is still the same map is kept.</summary>
        public void Use(AnimalLooks? store)
        {
            _store = store;
            if (store == null)
            {
                _looks.Clear();
                return;
            }
            foreach (var id in _looks.Keys.Where(id => store.Get(id) == null).ToList()) _looks.Remove(id);
            foreach (var id in store.Ids) Get(id);
        }

        /// <summary>True when there is no store or it holds no look; cheap, so an animal's id is read only when it can matter.</summary>
        public bool IsEmpty => _store == null || _store.Count == 0;

        /// <summary>True when the store has a look for this animal; cheap, before anything is read or built.</summary>
        public bool Has(ulong id) => _store?.Get(id) != null;

        /// <summary>The animal's prepared look; a map the store replaced since is prepared again. Null when it has none.</summary>
        public Dictionary<string, MaterialOverride>? Get(ulong id)
        {
            var source = _store?.Get(id);
            if (source == null) return null;
            if (_looks.TryGetValue(id, out var cached) && ReferenceEquals(cached.Source, source)) return cached.Prepared;
            var prepared = new Dictionary<string, MaterialOverride>(StringComparer.Ordinal);
            foreach (var pair in source) prepared[pair.Key] = _prepare(pair.Value);
            _looks[id] = (source, prepared);
            return prepared;
        }

        /// <summary>Every prepared block, e.g. to tell which ramps and masks are still in use.</summary>
        public IEnumerable<MaterialOverride> Blocks => _looks.Values.SelectMany(look => look.Prepared.Values);
    }
}
