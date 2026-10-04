using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Tyrant.Dumper.Serialization
{
    /// <summary>One FMOD event the game's banks hold.</summary>
    public sealed class AudioEvent
    {
        public AudioEvent(string path, string guid, int lengthMs, bool oneShot, string bank)
        {
            Path = path;
            Guid = guid;
            LengthMs = lengthMs;
            OneShot = oneShot;
            Bank = bank;
        }

        public string Path { get; }
        public string Guid { get; }
        public int LengthMs { get; }
        public bool OneShot { get; }
        public string Bank { get; }
    }

    /// <summary>
    /// The dump's audio/events.json: every FMOD event the game can play (the sound pickers list them). Tyrant reads it in Core;
    /// this side only writes, so the in-game DLL needs no JSON parser.
    /// </summary>
    public static class AudioEventList
    {
        public const int Format = 1;
        public const string Folder = "audio";
        public const string FileName = "events.json";

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        /// <summary>Writes &lt;dataDir&gt;/audio/events.json, sorted by path; an event found in several banks is listed once.</summary>
        public static void Write(string dataDir, IEnumerable<AudioEvent> events)
        {
            var w = new JsonWriter();
            w.StartObject();
            w.Name("format"); w.Number(Format);
            w.Name("events"); w.StartArray();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in events.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase))
            {
                if (!seen.Add(e.Path)) continue;
                w.StartObject();
                w.Name("path"); w.String(e.Path);
                w.Name("guid"); w.String(e.Guid);
                w.Name("lengthMs"); w.Number(e.LengthMs);
                w.Name("oneShot"); w.Bool(e.OneShot);
                w.Name("bank"); w.String(e.Bank);
                w.EndObject();
            }
            w.EndArray();
            w.EndObject();
            var dir = Path.Combine(dataDir, Folder);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, FileName), w.ToString(), Utf8);
        }
    }
}
