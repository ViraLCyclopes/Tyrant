using System;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// The Nursery skin row's ‹ › buttons: the row shows <see cref="PerPage"/> swatches, and each press moves one page of them.
    /// Pages start at multiples of 5; the last page ends at the last swatch. Offsets are how far the row is scrolled, in pixels.
    /// </summary>
    public static class SkinRowPaging
    {
        public const int PerPage = 5;

        /// <summary>The row scrolls (and gets its buttons) only with more swatches than fit.</summary>
        public static bool Scrolls(int count) => count > PerPage;

        private static int LastFirst(int count) => Math.Max(0, count - PerPage);

        public static int NextFirst(int first, int count) => Math.Min((Math.Max(0, first) / PerPage + 1) * PerPage, LastFirst(count));

        public static int PreviousFirst(int first, int count)
        {
            if (first <= 0) return 0;
            return Math.Min((first - 1) / PerPage * PerPage, LastFirst(count));
        }

        /// <summary>The swatch nearest the row's left edge (scrolling by wheel or drag stops anywhere).</summary>
        public static int FirstVisible(float offset, float stride, int count)
        {
            if (stride <= 0f) return 0;
            var first = (int)Math.Round(Math.Max(0f, offset) / stride, MidpointRounding.AwayFromZero);
            return Math.Min(first, LastFirst(count));
        }

        public static float Offset(int first, float stride, float maxOffset) => Math.Max(0f, Math.Min(first * stride, maxOffset));

        // Half a pixel of slack: a clamped scroll view rarely lands exactly on its ends.
        public static bool CanGoBack(float offset) => offset > 0.5f;

        public static bool CanGoForward(float offset, float maxOffset) => offset < maxOffset - 0.5f;
    }
}
