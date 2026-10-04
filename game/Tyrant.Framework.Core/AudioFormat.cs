namespace Tyrant.Framework.Core
{
    /// <summary>Tells audio files FMOD can play (WAV, OGG, MP3, FLAC) by their first bytes, not their extension.</summary>
    public static class AudioFormat
    {
        /// <summary>"wav", "ogg", "mp3" or "flac"; null for anything else.</summary>
        public static string? Sniff(byte[] header)
        {
            if (header == null || header.Length < 2) return null;
            if (Is(header, 0, "RIFF") && Is(header, 8, "WAVE")) return "wav";
            if (Is(header, 0, "OggS")) return "ogg";
            if (Is(header, 0, "fLaC")) return "flac";
            if (Is(header, 0, "ID3") || IsMpegFrame(header)) return "mp3";
            return null;
        }

        private static bool IsMpegFrame(byte[] h) => h[0] == 0xFF && (h[1] & 0xE0) == 0xE0;

        private static bool Is(byte[] h, int at, string text)
        {
            if (h.Length < at + text.Length) return false;
            for (var i = 0; i < text.Length; i++)
                if (h[at + i] != (byte)text[i]) return false;
            return true;
        }
    }
}
