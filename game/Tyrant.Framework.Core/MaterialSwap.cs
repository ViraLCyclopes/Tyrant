using System;
using System.Collections.Generic;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// Which texture properties of a material to point at a mod's texture: every property whose texture has a replaced
    /// name. Each material is looked at once (FirstLook; the framework checks new materials as they load).
    /// </summary>
    public sealed class MaterialSwap
    {
        private readonly Func<string, bool> _isReplaced;
        private readonly HashSet<int> _seen = new HashSet<int>();

        public MaterialSwap(Func<string, bool> isReplaced, bool hasReplacements = true)
        {
            _isReplaced = isReplaced;
            HasWork = hasReplacements;
        }

        /// <summary>False without any texture replacement: nothing needs to be looked at.</summary>
        public bool HasWork { get; }

        /// <summary>True the first time a material is met: only then are its slots read (reading them costs Unity calls).</summary>
        public bool FirstLook(int materialId) => _seen.Add(materialId);

        /// <summary>The (property, texture name) pairs of a material to swap; empty when nothing matches.</summary>
        public IReadOnlyList<(string Property, string Texture)> Plan(IEnumerable<(string Property, string? Texture)> slots)
        {
            var result = new List<(string, string)>();
            foreach (var (property, texture) in slots)
                if (!string.IsNullOrEmpty(texture) && _isReplaced(texture!)) result.Add((property, texture!));
            return result;
        }

        /// <summary>How a mod's PNG for this property is read: normal maps and masks are linear, everything else is colour.</summary>
        public static SlotKind KindOf(string property)
        {
            var p = property.ToLowerInvariant();
            if (p.Contains("normal") || p.Contains("bump")) return SlotKind.Normal;
            if (p.Contains("mask") || p.Contains("extra") || p.Contains("pattern") || p.Contains("detail")) return SlotKind.Data;
            return SlotKind.Color;
        }
    }
}
