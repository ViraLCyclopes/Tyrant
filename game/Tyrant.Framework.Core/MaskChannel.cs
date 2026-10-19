namespace Tyrant.Framework.Core
{
    /// <summary>A mask from one channel of an image.</summary>
    public static class MaskChannel
    {
        /// <summary>Grey RGBA (alpha 255) of <paramref name="channel"/> (r, g, b or a) of <paramref name="rgba"/>, inverted when asked.</summary>
        public static byte[] Extract(byte[] rgba, char channel, bool invert)
        {
            var offset = "rgba".IndexOf(char.ToLowerInvariant(channel));
            if (offset < 0) offset = 0;
            var grey = new byte[rgba.Length];
            for (var i = 0; i + 3 < rgba.Length; i += 4)
            {
                var v = rgba[i + offset];
                if (invert) v = (byte)(255 - v);
                grey[i] = grey[i + 1] = grey[i + 2] = v;
                grey[i + 3] = 255;
            }
            return grey;
        }
    }
}
