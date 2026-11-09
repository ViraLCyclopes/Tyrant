using System.Collections.Generic;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// Bytes read in place, wherever they live: a managed array, or a texture's own buffer in the game. Used as a struct type
    /// parameter, so a loop over millions of pixels makes plain calls (no delegate per byte) and copies nothing.
    /// </summary>
    public interface IByteSource
    {
        byte At(int index);
    }

    public readonly struct ListBytes : IByteSource
    {
        private readonly IList<byte> _data;

        public ListBytes(IList<byte> data) => _data = data;

        public byte At(int index) => _data[index];
    }

    public readonly struct ArrayBytes : IByteSource
    {
        private readonly byte[] _data;

        public ArrayBytes(byte[] data) => _data = data;

        public byte At(int index) => _data[index];
    }
}
