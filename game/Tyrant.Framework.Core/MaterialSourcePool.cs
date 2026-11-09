using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// One object per ramp or mask content, so the framework's textures (cached by these objects) are built once however often a
    /// block is rebuilt: a live edit on every slider move, a reload after a save. A file's content includes its stamp (from the
    /// caller, e.g. its time and size), so an image edited on disk is a new source. A file's stamp is read again at most once
    /// every <see cref="StampSeconds"/> (a slider drag interns the same block many times a second), so an image saved again is
    /// picked up within about that long; <see cref="ForgetStamps"/> reads them all again on the next intern.
    /// </summary>
    public sealed class MaterialSourcePool
    {
        /// <summary>How long a file's stamp is trusted before it is read again.</summary>
        public const double StampSeconds = 1;

        private readonly Func<string, string> _fileStamp;
        private readonly Func<double> _clock;
        private readonly double _stampSeconds;
        private readonly Dictionary<string, (string Stamp, double At)> _stamps = new Dictionary<string, (string, double)>(StringComparer.Ordinal);
        private readonly Dictionary<string, RampSource> _ramps = new Dictionary<string, RampSource>(StringComparer.Ordinal);
        private readonly Dictionary<string, MaskSource> _masks = new Dictionary<string, MaskSource>(StringComparer.Ordinal);
        private readonly HashSet<object> _pooled = new HashSet<object>(ReferenceComparer.Instance);

        public MaterialSourcePool(Func<string, string> fileStamp) : this(fileStamp, Seconds, StampSeconds)
        {
        }

        /// <param name="fileStamp">A file's stamp (e.g. its time and size).</param>
        /// <param name="clock">The time in seconds (from any start), for how long a stamp read is trusted.</param>
        /// <param name="stampSeconds">How long a stamp read is trusted; 0 reads it on every intern.</param>
        public MaterialSourcePool(Func<string, string> fileStamp, Func<double> clock, double stampSeconds)
        {
            _fileStamp = fileStamp;
            _clock = clock;
            _stampSeconds = stampSeconds;
        }

        private static double Seconds() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        /// <summary>The next intern reads every file's stamp again (an image was just written, or the player asked to read them again).</summary>
        public void ForgetStamps() => _stamps.Clear();

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

        /// <summary>Forgets the sources none of <paramref name="blocks"/> uses (and stamps too old to trust); returns how many sources.</summary>
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
            if (_stamps.Count > 0)
            {
                var now = _clock();
                foreach (var file in _stamps.Where(pair => !Trusted(pair.Value.At, now)).Select(pair => pair.Key).ToList()) _stamps.Remove(file);
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

        /// <summary>The file's stamp: the one read before while it is younger than <see cref="StampSeconds"/>, else read again.</summary>
        private string Stamp(string file)
        {
            var now = _clock();
            if (_stamps.TryGetValue(file, out var known) && Trusted(known.At, now)) return known.Stamp;
            var stamp = _fileStamp(file);
            _stamps[file] = (stamp, now);
            return stamp;
        }

        private bool Trusted(double at, double now) => now >= at && now - at < _stampSeconds;

        private string Key(RampSource ramp)
        {
            if (ramp.Game) return "game";
            if (ramp.Species != null) return "species|" + ramp.Species;
            if (ramp.File != null) return "file|" + ramp.File + "|" + Stamp(ramp.File);
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
            if (mask.File != null) return "file|" + mask.File + "|" + Stamp(mask.File);
            return "from|" + mask.From + "|" + mask.Channel + "|" + (mask.Invert ? "1" : "0");
        }
    }
}
