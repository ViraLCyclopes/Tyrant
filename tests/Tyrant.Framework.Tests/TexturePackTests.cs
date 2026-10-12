using System;
using System.Collections.Generic;
using System.IO;
using Tyrant.Framework.Core;
using Xunit;

namespace Tyrant.Framework.Tests;

public class TexturePackTests
{
    private static TexturePackEntry Entry(string source, SlotKind kind, int format, int width, int height) => new()
    {
        Source = source, Kind = kind, Mode = TextureCompression.Auto, Fingerprint = new TextureFingerprint(123, 456),
        Format = format, Linear = kind != SlotKind.Color, Width = width, Height = height,
        MipCount = UnityTextureFormats.FullMipCount(width, height),
        Length = UnityTextureFormats.DataLength(format, width, height, UnityTextureFormats.FullMipCount(width, height)),
    };

    private static byte[] Written(params TexturePackEntry[] entries)
    {
        using var stream = new MemoryStream();
        TexturePack.Write(stream, entries, (i, output) =>
        {
            var data = new byte[entries[i].Length];
            for (var k = 0; k < data.Length; k++) data[k] = (byte)(i + 1);
            output.Write(data, 0, data.Length);
        });
        return stream.ToArray();
    }

    [Fact]
    public void Data_lengths_follow_unitys_block_layout()
    {
        Assert.Equal(4, UnityTextureFormats.FullMipCount(8, 8));
        Assert.Equal(4, UnityTextureFormats.FullMipCount(8, 4)); // 8x4, 4x2, 2x1, 1x1
        Assert.Equal(13, UnityTextureFormats.FullMipCount(4096, 4096));
        Assert.Equal(8 * 16 + 8 * 4 + 8 + 8, UnityTextureFormats.DataLength(UnityTextureFormats.Dxt1, 16, 16, 4)); // 4x4 blocks, then 2x2, 1, 1
        Assert.Equal(16 * 16 + 16 * 4 + 16 + 16, UnityTextureFormats.DataLength(UnityTextureFormats.Bc7, 16, 16, 4));
        Assert.Equal(16 * 2 + 16 + 16, UnityTextureFormats.DataLength(UnityTextureFormats.Dxt5, 8, 4, 3));
        Assert.Equal("DXT1", UnityTextureFormats.Name(10));
        Assert.Equal("BC7", UnityTextureFormats.Name(25));
    }

    [Fact]
    public void A_pack_round_trips_its_table_and_data_offsets()
    {
        var bytes = Written(Entry("skins/blue/male_D.png", SlotKind.Color, UnityTextureFormats.Dxt1, 16, 16),
                            Entry("skins/blue/male_N.png", SlotKind.Normal, UnityTextureFormats.Dxt5, 8, 4));
        var pack = TexturePack.Read(new MemoryStream(bytes));

        Assert.Equal(2, pack.Entries.Count);
        var normal = pack.Find("skins\\BLUE\\male_N.png", SlotKind.Normal)!; // '/' or '\', any case
        Assert.Equal((8, 4, 4, true), (normal.Width, normal.Height, normal.MipCount, normal.Linear));
        Assert.Equal(new TextureFingerprint(123, 456), normal.Fingerprint);
        Assert.Equal(2, bytes[normal.Offset]); // the second entry's data
        Assert.Equal(1, bytes[pack.Entries[0].Offset]);
        Assert.Equal(bytes.Length, normal.Offset + normal.Length);
        Assert.Null(pack.Find("skins/blue/male_N.png", SlotKind.Color)); // same file, other kind
    }

    [Fact]
    public void A_bad_pack_is_refused_with_a_reason_never_read_past()
    {
        var good = Written(Entry("a.png", SlotKind.Color, UnityTextureFormats.Dxt1, 8, 8));
        Assert.Throws<TexturePackException>(() => TexturePack.Read(new MemoryStream(new byte[] { (byte)'N', (byte)'O', (byte)'P', (byte)'E', 1, 0, 0, 0, 0, 0, 0, 0 })));

        var newer = (byte[])good.Clone();
        newer[4] = 2;
        Assert.Contains("newer", Assert.Throws<TexturePackException>(() => TexturePack.Read(new MemoryStream(newer))).Message);

        Assert.Throws<TexturePackException>(() => TexturePack.Read(new MemoryStream(good, 0, good.Length - 1))); // data cut short
        Assert.Throws<TexturePackException>(() => TexturePack.Read(new MemoryStream(good, 0, 20))); // table cut short
    }

    [Fact]
    public void A_cooked_texture_is_used_only_when_its_png_and_the_gpu_still_match()
    {
        var entry = Entry("a.png", SlotKind.Color, UnityTextureFormats.Bc7, 8, 8);
        Assert.True(TexturePack.Usable(entry, new TextureFingerprint(123, 456), _ => true));
        Assert.False(TexturePack.Usable(null, new TextureFingerprint(123, 456), _ => true));
        Assert.False(TexturePack.Usable(entry, new TextureFingerprint(124, 456), _ => true)); // edited: other size
        Assert.False(TexturePack.Usable(entry, new TextureFingerprint(123, 457), _ => true)); // edited: same size
        Assert.False(TexturePack.Usable(entry, new TextureFingerprint(123, 456), f => f != UnityTextureFormats.Bc7));
    }

    [Fact]
    public void Without_its_png_an_entry_is_usable_when_present_and_the_gpu_takes_its_format()
    {
        var entry = Entry("a.png", SlotKind.Color, UnityTextureFormats.Bc7, 8, 8);
        Assert.True(TexturePack.UsableWithoutSource(entry, _ => true));
        Assert.False(TexturePack.UsableWithoutSource(null, _ => true));
        Assert.False(TexturePack.UsableWithoutSource(entry, f => f != UnityTextureFormats.Bc7));
    }

    [Fact]
    public void A_fingerprint_reads_only_the_head_and_tail_and_changes_with_either()
    {
        var data = new byte[300_000];
        new Random(4).NextBytes(data);
        var original = TextureFingerprint.Of(new MemoryStream(data));
        Assert.Equal(300_000, original.Size);

        var middle = (byte[])data.Clone();
        middle[150_000] ^= 1; // outside both 64 KB windows
        Assert.Equal(original, TextureFingerprint.Of(new MemoryStream(middle)));

        var head = (byte[])data.Clone();
        head[10] ^= 1;
        Assert.NotEqual(original, TextureFingerprint.Of(new MemoryStream(head)));

        var tail = (byte[])data.Clone();
        tail[^5] ^= 1;
        Assert.NotEqual(original, TextureFingerprint.Of(new MemoryStream(tail)));

        Assert.NotEqual(TextureFingerprint.Of(new MemoryStream(new byte[] { 1, 2 })), TextureFingerprint.Of(new MemoryStream(new byte[] { 2, 1 }))); // small file: whole
    }

    [Fact]
    public void Compression_names_are_auto_and_bc7()
    {
        Assert.Equal("bc7", TextureCompressions.Name(TextureCompression.Bc7));
        Assert.Equal(TextureCompression.Auto, TextureCompressions.Parse("auto"));
        Assert.Null(TextureCompressions.Parse("BC7")); // exact, as written in mod.json
        Assert.Null(TextureCompressions.Parse(null));
    }

    [Fact]
    public void An_entry_whose_offset_overflows_is_refused()
    {
        var good = Written(Entry("a.png", SlotKind.Color, UnityTextureFormats.Dxt1, 8, 8));
        var offsetField = (int)TexturePack.Read(new MemoryStream(good)).Entries[0].Offset - 16; // Offset, then Length, end the table row
        using var patched = new MemoryStream();
        using (var w = new BinaryWriter(patched, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(good, 0, offsetField);
            w.Write(long.MaxValue - 10);
            w.Write(good, offsetField + 8, good.Length - offsetField - 8);
        }
        Assert.Throws<TexturePackException>(() => TexturePack.Read(new MemoryStream(patched.ToArray())));
    }
}
