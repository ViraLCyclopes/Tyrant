using System.Text;
using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class AudioFormatTests
{
    private static byte[] Bytes(string ascii, params byte[] more) => [.. Encoding.ASCII.GetBytes(ascii), .. more];

    [Fact]
    public void Audio_files_are_known_by_their_first_bytes()
    {
        Assert.Equal("wav", AudioFormat.Sniff(Bytes("RIFF\x24\0\0\0WAVEfmt ")));
        Assert.Equal("ogg", AudioFormat.Sniff(Bytes("OggS\0\x02")));
        Assert.Equal("mp3", AudioFormat.Sniff(Bytes("ID3\x04\0")));
        Assert.Equal("mp3", AudioFormat.Sniff([0xFF, 0xFB, 0x90, 0x64]));
        Assert.Equal("flac", AudioFormat.Sniff(Bytes("fLaC\0\0")));
    }

    [Fact]
    public void Other_files_are_not_audio()
    {
        Assert.Null(AudioFormat.Sniff(Bytes("PK\x03\x04")));
        Assert.Null(AudioFormat.Sniff(Bytes("hello, this is text")));
        Assert.Null(AudioFormat.Sniff(Bytes("RIFF\x24\0\0\0AVI ")));
        Assert.Null(AudioFormat.Sniff([]));
    }
}
