using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// One object per ramp or mask content, so the framework's textures (cached by these objects) are built once however often a
    /// block is rebuilt: a live edit on every slider move, a reload after a save. A file's content includes its stamp (from the
    /// caller, e.g. its time and size), so an image edited on disk is a new source.
    /// </summary>
    public sealed class MaterialSourcePool
    {
        private readonly Func<string, string> _fileStamp;
        private readonly Dictionary<string, RampSource> _ramps = new Dictionary<string, RampSource>(StringComparer.Ordinal);
        private readonly Dictionary<string, MaskSource> _masks = new Dictionary<string, MaskSource>(StringComparer.Ordinal);
        private readonly HashSet<object> _pooled = new HashSet<object>(ReferenceComparer.Instance);

        public MaterialSourcePool(Func<string, string> fileStamp) => _fileStamp = fileStamp;

        /// <summary>Points the block's ramp and masks at the pooled object of the same content (adding new content to the pool).</summary>
        public void Intern(MaterialOverride block)
        {
            if (block.Iridescence is IridescenceSettings i)
            {
                if (i.Ramp != null) i.Ramp = Pooled(_ramps, Key(i.Ramp), i.Ramp);
                if (i.Mask != null) i.Mask = Pooled(_masks, Key(i.Mask), i.Mask);
            }
            if (block.Fur is FurMasks fur)
            {
                if (fur.Mask != null) fur.Mask = Pooled(_masks, Key(fur.Mask), fur.Mask);
                if (fur.InfantMask != null) fur.InfantMask = Pooled(_masks, Key(fur.InfantMask), fur.InfantMask);
            }
        }

        /// <summary>True while the pool holds this very object; a source it forgot is never handed out again.</summary>
        public bool Contains(object source) => _pooled.Contains(source);

        /// <summary>Forgets the sources none of <paramref name="blocks"/> uses; returns how many.</summary>
        public int Retain(IEnumerable<MaterialOverride> blocks)
        {
            var used = new HashSet<object>(ReferenceComparer.Instance);
            foreach (var block in blocks)
            {
                if (block.Iridescence?.Ramp is RampSource ramp) used.Add(ramp);
                if (block.Iridescence?.Mask is MaskSource mask) used.Add(mask);
                if (block.Fur?.Mask is MaskSource fur) used.Add(fur);
                if (block.Fur?.InfantMask is MaskSource infant) used.Add(infant);
            }
            return Forget(_ramps, used) + Forget(_masks, used);
        }

        private int Forget<T>(Dictionary<string, T> pool, HashSet<object> used) where T : class
        {
            var gone = pool.Where(pair => !used.Contains(pair.Value)).Select(pair => pair.Key).ToList();
            foreach (var key in gone)
            {
                _pooled.Remove(pool[key]);
                pool.Remove(key);
            }
            return gone.Count;
        }

        private T Pooled<T>(Dictionary<string, T> pool, string key, T source) where T : class
        {
            if (pool.TryGetValue(key, out var pooled)) return pooled;
            pool[key] = source;
            _pooled.Add(source);
            return source;
        }

        private string Key(RampSource ramp)
        {
            if (ramp.Game) return "game";
            if (ramp.Species != null) return "species|" + ramp.Species;
            if (ramp.File != null) return "file|" + ramp.File + "|" + _fileStamp(ramp.File);
            if (ramp.ThinFilm != null)
                return "film|" + ramp.ThinFilm.Thickness.ToString("R", CultureInfo.InvariantCulture) + "|" + ramp.ThinFilm.Index.ToString("R", CultureInfo.InvariantCulture);
            var text = new StringBuilder("stops");
            foreach (var stop in ramp.Stops ?? new List<Rgb>())
                text.Append('|').Append(stop.R.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(stop.G.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(stop.B.ToString("R", CultureInfo.InvariantCulture));
            return text.ToString();
        }

        private string Key(MaskSource mask)
        {
            if (mask.Game) return "game";
            if (mask.Species != null) return "species|" + mask.Species;
            if (mask.Everywhere) return "everywhere";
            if (mask.File != null) return "file|" + mask.File + "|" + _fileStamp(mask.File);
            return "from|" + mask.From + "|" + mask.Channel + "|" + (mask.Invert ? "1" : "0");
        }
    }
}
