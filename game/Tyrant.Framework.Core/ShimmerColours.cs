using System;
using System.Collections.Generic;

namespace Tyrant.Framework.Core
{
    /// <summary>A colour as hue, saturation and value, 0–1 each (hue 0 and 1 are both red).</summary>
    public readonly struct Hsv
    {
        public Hsv(float h, float s, float v)
        {
            H = h;
            S = s;
            V = v;
        }

        public float H { get; }
        public float S { get; }
        public float V { get; }
    }

    /// <summary>
    /// The in-game editor's Shimmer colours: an iridescence ramp of evenly spaced stops (<see cref="RampSource.Stops"/>), edited
    /// with a colour square (saturation × value at one hue) and a hue strip. The window shows and edits the first
    /// <see cref="MaxShown"/>; a look from the app with more keeps them all through every edit.
    /// </summary>
    public static class ShimmerColours
    {
        public const int MaxShown = 8;

        /// <summary>Microraptor's ramp, deep violet-blue to bright blue: what Colours starts from after a game, PNG or species ramp.</summary>
        public static IReadOnlyList<Rgb> StartStops { get; } = new[] { new Rgb(0x39 / 255f, 0x14 / 255f, 0x9c / 255f), new Rgb(0x2e / 255f, 0x6b / 255f, 0xe1 / 255f) };

        // ---- HSV ----

        public static Rgb ToRgb(Hsv hsv)
        {
            var h = hsv.H - (float)Math.Floor(hsv.H); // wraps: 1 is 0, -0.1 is 0.9
            var s = Clamp01(hsv.S);
            var v = Clamp01(hsv.V);
            var sector = h * 6;
            var i = (int)Math.Floor(sector) % 6;
            var f = sector - (float)Math.Floor(sector);
            var p = v * (1 - s);
            var q = v * (1 - s * f);
            var t = v * (1 - s * (1 - f));
            switch (i)
            {
                case 0: return new Rgb(v, t, p);
                case 1: return new Rgb(q, v, p);
                case 2: return new Rgb(p, v, t);
                case 3: return new Rgb(p, q, v);
                case 4: return new Rgb(t, p, v);
                default: return new Rgb(v, p, q);
            }
        }

        /// <summary>
        /// A colour's hue, saturation and value. A grey has no hue of its own, so it keeps <paramref name="keep"/>'s (0 without
        /// one); black has no saturation either and keeps both, so the square and strip stay where the player left them.
        /// </summary>
        public static Hsv ToHsv(Rgb rgb, Hsv? keep = null)
        {
            float r = Clamp01(rgb.R), g = Clamp01(rgb.G), b = Clamp01(rgb.B);
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var d = max - min;
            if (max <= 0) return new Hsv(keep?.H ?? 0, keep?.S ?? 0, 0);
            if (d <= 0) return new Hsv(keep?.H ?? 0, 0, max);
            float h;
            if (max == r) h = (g - b) / d;
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h /= 6;
            if (h < 0) h += 1;
            if (h >= 1) h -= 1;
            return new Hsv(h, d / max, max);
        }

        // ---- hex ----

        /// <summary>A typed colour, "#rrggbb" or "rrggbb" in either case; false for anything else.</summary>
        public static bool TryParseHex(string? text, out Rgb colour)
        {
            var trimmed = text?.Trim() ?? "";
            return Rgb.TryParse(trimmed.StartsWith("#", StringComparison.Ordinal) ? trimmed : "#" + trimmed, out colour);
        }

        /// <summary>"#rrggbb", as mod.json writes it.</summary>
        public static string Hex(Rgb colour) => colour.ToString();

        /// <summary>The colour the file keeps (whole bytes per channel), so what the window shows is what Save writes.</summary>
        public static Rgb Quantize(Rgb colour) => new Rgb(Byte(colour.R), Byte(colour.G), Byte(colour.B));

        // ---- the stop list (every edit returns a new list) ----

        public static bool CanAdd(IReadOnlyList<Rgb> stops) => stops.Count < MaxShown;

        public static bool CanRemove(IReadOnlyList<Rgb> stops) => stops.Count > 1;

        /// <summary>A copy of the colour at <paramref name="index"/> right after it; unchanged at <see cref="MaxShown"/> or more.</summary>
        public static List<Rgb> AddAfter(IReadOnlyList<Rgb> stops, int index)
        {
            var list = new List<Rgb>(stops);
            if (CanAdd(stops) && index >= 0 && index < stops.Count) list.Insert(index + 1, stops[index]);
            return list;
        }

        /// <summary>Without the colour at <paramref name="index"/>; never the last one.</summary>
        public static List<Rgb> Remove(IReadOnlyList<Rgb> stops, int index)
        {
            var list = new List<Rgb>(stops);
            if (CanRemove(stops) && index >= 0 && index < stops.Count) list.RemoveAt(index);
            return list;
        }

        public static List<Rgb> SetAt(IReadOnlyList<Rgb> stops, int index, Rgb colour)
        {
            var list = new List<Rgb>(stops);
            if (index >= 0 && index < stops.Count) list[index] = colour;
            return list;
        }

        /// <summary>The selected colour kept on one the window shows (the first <see cref="MaxShown"/>).</summary>
        public static int Selection(int selected, int count)
        {
            var last = Math.Min(count, MaxShown) - 1;
            return last <= 0 ? 0 : selected < 0 ? 0 : selected > last ? last : selected;
        }

        // ---- starting colours ----

        /// <summary>At most <paramref name="count"/> colours of the same gradient, evenly spaced, both ends kept; a short list is copied.</summary>
        public static List<Rgb> Resample(IReadOnlyList<Rgb> stops, int count)
        {
            if (stops.Count <= count) return new List<Rgb>(stops);
            var list = new List<Rgb>(count);
            for (var i = 0; i < count; i++) list.Add(ColorRamp.Sample(stops, count == 1 ? 0 : i / (double)(count - 1)));
            return list;
        }

        /// <summary>
        /// The colours Colours starts from: the stops in effect (all of them), a thin film's colours (at most <see cref="MaxShown"/>),
        /// else <see cref="StartStops"/>.
        /// </summary>
        public static List<Rgb> Starting(RampSource? ramp)
        {
            if (ramp?.Stops != null) return new List<Rgb>(ramp.Stops);
            if (ramp?.ThinFilm != null && IridescenceRamp.Stops(ramp) is IReadOnlyList<Rgb> film) return Resample(film, MaxShown);
            return new List<Rgb>(StartStops);
        }

        /// <summary>
        /// What clicking Colours puts in the draft: <see cref="Starting"/> from the ramp in effect (the draft's own, else the lower
        /// layer's), so a lower layer's stops are pinned at this scope, every one of them, as Thin film and Game's pin theirs. Null
        /// when the draft's own ramp is stops already (nothing to do).
        /// </summary>
        public static List<Rgb>? Picked(RampSource? own, RampSource? below) =>
            own != null && LookEditing.RampKindOf(own) == RampKind.Stops ? null : Starting(own ?? below);

        private static float Clamp01(float c) => c < 0 ? 0 : c > 1 ? 1 : c;

        private static float Byte(float c) => (float)Math.Round(Clamp01(c) * 255) / 255f;
    }
}
