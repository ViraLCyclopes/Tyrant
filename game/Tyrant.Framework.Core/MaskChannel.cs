using System;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// Bytes that can be read and written by index: a managed array (<see cref="ByteArrayBuffer"/>) or, in the game, a
    /// texture's own pixel buffer. Taken as a generic struct argument, so each byte is a direct call, never a delegate.
    /// </summary>
    public interface IByteBuffer
    {
        int Length { get; }
        byte this[int index] { get; set; }
    }

    /// <summary>A managed byte array as an <see cref="IByteBuffer"/>.</summary>
    public readonly struct ByteArrayBuffer : IByteBuffer
    {
        private readonly byte[] _bytes;

        public ByteArrayBuffer(byte[] bytes) => _bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));

        public int Length => _bytes.Length;

        public byte this[int index]
        {
            get => _bytes[index];
            set => _bytes[index] = value;
        }
    }

    /// <summary>A mask from one channel of an image.</summary>
    public static class MaskChannel
    {
        /// <summary>Grey RGBA (alpha 255) of <paramref name="channel"/> (r, g, b or a) of <paramref name="rgba"/>, inverted when asked.</summary>
        public static byte[] Extract(byte[] rgba, char channel, bool invert)
        {
            var grey = new byte[rgba.Length];
            Buffer.BlockCopy(rgba, 0, grey, 0, rgba.Length - rgba.Length % 4); // whole pixels; a partial one at the end stays 0
            ExtractInPlace(new ByteArrayBuffer(grey), channel, invert);
            return grey;
        }

        /// <summary>
        /// <see cref="Extract"/> in the buffer itself, with no copy (a 4K map's pixels are 64 MB): each whole RGBA pixel becomes
        /// grey of its <paramref name="channel"/> (r, g, b or a; anything else is r), inverted when asked, with alpha 255. Bytes
        /// after the last whole pixel are left as they are.
        /// </summary>
        public static void ExtractInPlace<TBuffer>(TBuffer rgba, char channel, bool invert) where TBuffer : IByteBuffer
        {
            var offset = "rgba".IndexOf(char.ToLowerInvariant(channel));
            if (offset < 0) offset = 0;
            var length = rgba.Length;
            for (var i = 0; i + 3 < length; i += 4)
            {
                var v = rgba[i + offset]; // read before the pixel is written over
                if (invert) v = (byte)(255 - v);
                rgba[i] = v;
                rgba[i + 1] = v;
                rgba[i + 2] = v;
                rgba[i + 3] = 255;
            }
        }
    }
}
