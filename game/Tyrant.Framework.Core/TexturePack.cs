using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Tyrant.Framework.Core
{
    /// <summary>How a mod texture is compressed when Tyrant cooks it: auto matches the game (DXT1/DXT5), Bc7 is best quality.</summary>
    public enum TextureCompression
    {
        Auto,
        Bc7,
    }

    public static class TextureCompressions
    {
        public static string Name(TextureCompression mode) => mode == TextureCompression.Bc7 ? "bc7" : "auto";

        public static TextureCompression? Parse(string? text) =>
            text == "auto" ? TextureCompression.Auto : text == "bc7" ? TextureCompression.Bc7 : (TextureCompression?)null;
    }

    /// <summary>
    /// A PNG's identity in a pack: its size and an FNV-1a 64 hash of its first and last 64 KB. File times are not used: zips
    /// keep them only to 2 seconds, in local time.
    /// </summary>
    public readonly struct TextureFingerprint : IEquatable<TextureFingerprint>
    {
        public const int Window = 64 * 1024;

        public TextureFingerprint(long size, ulong hash)
        {
            Size = size;
            Hash = hash;
        }

        public long Size { get; }
        public ulong Hash { get; }

        public static TextureFingerprint Of(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
            return Of(stream);
        }

        public static TextureFingerprint Of(Stream stream)
        {
            var size = stream.Length;
            var buffer = new byte[Window];
            var hash = 14695981039346656037UL;
            var head = (int)Math.Min(size, Window);
            stream.Position = 0;
            hash = Mix(hash, buffer, ReadFully(stream, buffer, head));
            var tailStart = Math.Max(head, size - Window);
            stream.Position = tailStart;
            hash = Mix(hash, buffer, ReadFully(stream, buffer, (int)(size - tailStart)));
            return new TextureFingerprint(size, hash);
        }

        private static int ReadFully(Stream stream, byte[] buffer, int count)
        {
            var done = 0;
            while (done < count)
            {
                var n = stream.Read(buffer, done, count - done);
                if (n <= 0) break;
                done += n;
            }
            return done;
        }

        private static ulong Mix(ulong hash, byte[] buffer, int count)
        {
            for (var i = 0; i < count; i++)
            {
                hash ^= buffer[i];
                hash *= 1099511628211UL;
            }
            return hash;
        }

        public bool Equals(TextureFingerprint other) => Size == other.Size && Hash == other.Hash;
        public override bool Equals(object? obj) => obj is TextureFingerprint other && Equals(other);
        public override int GetHashCode() => unchecked((int)Hash ^ (int)Size);
        public override string ToString() => $"{Size}:{Hash:x16}";
    }

    /// <summary>The compressed Unity texture formats a pack holds, and their raw sizes (LoadRawTextureData's layout).</summary>
    public static class UnityTextureFormats
    {
        public const int Dxt1 = 10;
        public const int Dxt5 = 12;
        public const int Bc7 = 25;

        public static bool IsKnown(int format) => format == Dxt1 || format == Dxt5 || format == Bc7;

        public static int BlockBytes(int format) => format == Dxt1 ? 8 : 16;

        public static string Name(int format) => format == Dxt1 ? "DXT1" : format == Dxt5 ? "DXT5" : format == Bc7 ? "BC7" : format.ToString();

        public static int FullMipCount(int width, int height)
        {
            var count = 1;
            while (width > 1 || height > 1)
            {
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
                count++;
            }
            return count;
        }

        /// <summary>Bytes of every mip, largest first; each level takes whole 4×4 blocks (a 2×2 or 1×1 level takes one).</summary>
        public static long DataLength(int format, int width, int height, int mipCount)
        {
            long total = 0;
            for (var level = 0; level < mipCount; level++)
            {
                total += (long)Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * BlockBytes(format);
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
            }
            return total;
        }
    }

    public sealed class TexturePackEntry
    {
        /// <summary>The PNG, relative to the mod, '/' separators, as mod.json names it.</summary>
        public string Source { get; set; } = "";
        public SlotKind Kind { get; set; }
        public TextureCompression Mode { get; set; }
        public TextureFingerprint Fingerprint { get; set; }

        /// <summary>Unity TextureFormat value (UnityTextureFormats).</summary>
        public int Format { get; set; }

        public bool Linear { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int MipCount { get; set; }

        /// <summary>From the start of the pack file.</summary>
        public long Offset { get; set; }

        public long Length { get; set; }
    }

    public sealed class TexturePackException : Exception
    {
        public TexturePackException(string message) : base(message) { }
    }

    /// <summary>
    /// textures.tpack: a mod's cooked textures, written by Tyrant at install and export. Header "TPAK", version, entry
    /// count, then each entry's table row, then every entry's data back to back. Read reads the table only.
    /// </summary>
    public sealed class TexturePack
    {
        public const string FileName = "textures.tpack";
        public const int Version = 1;
        private const int MaxEntries = 65536;
        public const int MaxSide = 16384;
        private static readonly byte[] Magic = { (byte)'T', (byte)'P', (byte)'A', (byte)'K' };

        private readonly Dictionary<string, TexturePackEntry> _byKey;

        private TexturePack(List<TexturePackEntry> entries)
        {
            Entries = entries;
            _byKey = new Dictionary<string, TexturePackEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries) _byKey[Key(entry.Source, entry.Kind)] = entry;
        }

        public IReadOnlyList<TexturePackEntry> Entries { get; }

        public static string NormalSource(string source) => source.Replace('\\', '/').TrimStart('/');

        private static string Key(string source, SlotKind kind) => NormalSource(source) + "|" + (int)kind;

        public TexturePackEntry? Find(string source, SlotKind kind) => _byKey.TryGetValue(Key(source, kind), out var entry) ? entry : null;

        /// <summary>True when the PNG is the one that was cooked and this GPU takes the format.</summary>
        public static bool Usable(TexturePackEntry? entry, TextureFingerprint source, Func<int, bool> supportsFormat) =>
            entry != null && entry.Fingerprint.Equals(source) && supportsFormat(entry.Format);

        /// <summary>Writes the table (offsets filled in from each entry's Length), then asks writeData for entry i's bytes in order.</summary>
        public static void Write(Stream output, IReadOnlyList<TexturePackEntry> entries, Action<int, Stream> writeData)
        {
            using var table = new MemoryStream();
            WriteTable(table, entries); // once to learn its length
            var offset = 12 + table.Length;
            foreach (var entry in entries)
            {
                entry.Offset = offset;
                offset += entry.Length;
            }
            using (var w = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
            {
                w.Write(Magic);
                w.Write((uint)Version);
                w.Write((uint)entries.Count);
            }
            WriteTable(output, entries);
            for (var i = 0; i < entries.Count; i++)
            {
                var start = output.Position;
                writeData(i, output);
                if (output.Position - start != entries[i].Length)
                    throw new InvalidOperationException($"{entries[i].Source}: wrote {output.Position - start} bytes, expected {entries[i].Length}.");
            }
        }

        private static void WriteTable(Stream output, IReadOnlyList<TexturePackEntry> entries)
        {
            using var w = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
            foreach (var e in entries)
            {
                var source = Encoding.UTF8.GetBytes(NormalSource(e.Source));
                if (source.Length > ushort.MaxValue) throw new InvalidOperationException("Texture path too long: " + e.Source);
                w.Write((ushort)source.Length);
                w.Write(source);
                w.Write((byte)e.Kind);
                w.Write((byte)e.Mode);
                w.Write(e.Fingerprint.Size);
                w.Write(e.Fingerprint.Hash);
                w.Write(e.Format);
                w.Write((byte)(e.Linear ? 1 : 0));
                w.Write(e.Width);
                w.Write(e.Height);
                w.Write(e.MipCount);
                w.Write(e.Offset);
                w.Write(e.Length);
            }
        }

        public static TexturePack Read(Stream input)
        {
            try
            {
                using var r = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
                var magic = r.ReadBytes(4);
                if (magic.Length != 4 || magic[0] != Magic[0] || magic[1] != Magic[1] || magic[2] != Magic[2] || magic[3] != Magic[3])
                    throw new TexturePackException("not a Tyrant texture pack");
                var version = r.ReadUInt32();
                if (version > Version) throw new TexturePackException($"made by a newer Tyrant (pack version {version}; this framework reads {Version})");
                if (version < 1) throw new TexturePackException($"pack version {version} is not valid");
                var count = r.ReadUInt32();
                if (count > MaxEntries) throw new TexturePackException($"{count} entries is too many");
                var entries = new List<TexturePackEntry>((int)count);
                for (var i = 0; i < count; i++)
                {
                    var length = r.ReadUInt16();
                    var source = r.ReadBytes(length);
                    if (source.Length != length) throw new EndOfStreamException();
                    var entry = new TexturePackEntry
                    {
                        Source = Encoding.UTF8.GetString(source),
                        Kind = (SlotKind)r.ReadByte(),
                        Mode = (TextureCompression)r.ReadByte(),
                        Fingerprint = new TextureFingerprint(r.ReadInt64(), r.ReadUInt64()),
                        Format = r.ReadInt32(),
                        Linear = r.ReadByte() != 0,
                        Width = r.ReadInt32(),
                        Height = r.ReadInt32(),
                        MipCount = r.ReadInt32(),
                        Offset = r.ReadInt64(),
                        Length = r.ReadInt64(),
                    };
                    entries.Add(entry);
                }
                var tableEnd = input.Position;
                foreach (var e in entries) Validate(e, tableEnd, input.Length);
                return new TexturePack(entries);
            }
            catch (EndOfStreamException)
            {
                throw new TexturePackException("the file is cut short");
            }
        }

        private static void Validate(TexturePackEntry e, long tableEnd, long fileLength)
        {
            var where = e.Source.Length == 0 ? "an entry" : e.Source;
            if ((int)e.Kind < 0 || (int)e.Kind > 2 || (int)e.Mode < 0 || (int)e.Mode > 1) throw new TexturePackException($"{where}: unknown kind or mode");
            if (!UnityTextureFormats.IsKnown(e.Format)) throw new TexturePackException($"{where}: unknown format {e.Format}");
            if (e.Width < 1 || e.Height < 1 || e.Width > MaxSide || e.Height > MaxSide) throw new TexturePackException($"{where}: size {e.Width}x{e.Height} is not valid");
            if (e.MipCount < 1 || e.MipCount > UnityTextureFormats.FullMipCount(e.Width, e.Height)) throw new TexturePackException($"{where}: {e.MipCount} mips is not valid");
            if (e.Length != UnityTextureFormats.DataLength(e.Format, e.Width, e.Height, e.MipCount)) throw new TexturePackException($"{where}: data length does not match its size");
            if (e.Offset < tableEnd || e.Offset > fileLength || e.Length > fileLength - e.Offset) throw new TexturePackException($"{where}: data lies outside the file (cut short?)");
        }
    }
}
