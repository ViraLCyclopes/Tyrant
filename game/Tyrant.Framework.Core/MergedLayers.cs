using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// <see cref="MaterialTable.MergeLayers"/>' results, kept by the very layer objects that went in, so the hooks (which ask for
    /// every covered animal several times per update) build one merged block per set of layers instead of one per call. The
    /// framework's layers are its own prepared copies, never changed once handed over, so the same objects always merge to
    /// the same result; the owner still clears the cache whenever a layer store changes (table, live edits, saved looks,
    /// preview), and it clears itself past <see cref="Capacity"/> results, so replaced layers are let go. The result is shared
    /// between calls: callers must not change it.
    /// </summary>
    public sealed class MergedLayers
    {
        /// <summary>The most results kept; one more clears them all (sets of layers are few: one per species, skin and look).</summary>
        public const int Capacity = 512;

        private readonly Dictionary<Layers, MaterialOverride?> _merged = new Dictionary<Layers, MaterialOverride?>();

        /// <summary>How many results are kept.</summary>
        public int Count => _merged.Count;

        /// <summary>
        /// The merged block for these layers (highest first, as <see cref="MaterialTable.MergeLayers"/>): the same object as the
        /// last call with the same layer objects, until <see cref="Clear"/>. Null when no layer is set or nothing is set.
        /// </summary>
        public MaterialOverride? Get(MaterialOverride? liveAnimal, MaterialOverride? savedAnimal, MaterialOverride? liveSkin,
            MaterialOverride? tableSkin, MaterialOverride? baseSkin, MaterialOverride? liveSpecies, MaterialOverride? tableSpecies)
        {
            if (liveAnimal == null && savedAnimal == null && liveSkin == null && tableSkin == null && baseSkin == null && liveSpecies == null && tableSpecies == null)
                return null; // nothing kept, nothing allocated
            var key = new Layers(liveAnimal, savedAnimal, liveSkin, tableSkin, baseSkin, liveSpecies, tableSpecies);
            if (_merged.TryGetValue(key, out var merged)) return merged;
            if (_merged.Count >= Capacity) _merged.Clear();
            merged = MaterialTable.MergeLayers(liveAnimal, savedAnimal, liveSkin, tableSkin, baseSkin, liveSpecies, tableSpecies);
            _merged[key] = merged;
            return merged;
        }

        /// <summary>Forgets every result (a layer store changed).</summary>
        public void Clear() => _merged.Clear();

        /// <summary>Seven layers compared by reference, whatever MaterialOverride's own Equals does.</summary>
        private readonly struct Layers : IEquatable<Layers>
        {
            private readonly MaterialOverride? _a, _b, _c, _d, _e, _f, _g;

            public Layers(MaterialOverride? a, MaterialOverride? b, MaterialOverride? c, MaterialOverride? d, MaterialOverride? e, MaterialOverride? f, MaterialOverride? g)
            {
                (_a, _b, _c, _d, _e, _f, _g) = (a, b, c, d, e, f, g);
            }

            public bool Equals(Layers other) =>
                ReferenceEquals(_a, other._a) && ReferenceEquals(_b, other._b) && ReferenceEquals(_c, other._c) && ReferenceEquals(_d, other._d)
                && ReferenceEquals(_e, other._e) && ReferenceEquals(_f, other._f) && ReferenceEquals(_g, other._g);

            public override bool Equals(object? obj) => obj is Layers other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 31 + Hash(_a);
                    hash = hash * 31 + Hash(_b);
                    hash = hash * 31 + Hash(_c);
                    hash = hash * 31 + Hash(_d);
                    hash = hash * 31 + Hash(_e);
                    hash = hash * 31 + Hash(_f);
                    hash = hash * 31 + Hash(_g);
                    return hash;
                }
            }

            private static int Hash(object? layer) => layer == null ? 0 : RuntimeHelpers.GetHashCode(layer);
        }
    }
}
