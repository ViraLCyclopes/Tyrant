using System;
using System.Collections.Generic;
using System.IO;
using Tyrant.Framework.Core;
using Unity.Collections;
using UnityEngine;

namespace Tyrant.Framework
{
    /// <summary>
    /// Each mod's textures.tpack (written by Tyrant at install and export): the table is read once at start; a texture is
    /// streamed from the pack straight into Unity's buffer through one reused 1 MB buffer, never as a whole managed array.
    /// </summary>
    internal static class TexturePacks
    {
        private sealed class Pack
        {
            public Pack(string modId, string path, Dictionary<string, TexturePackEntry>? byKey)
            {
                ModId = modId;
                Path = path;
                ByKey = byKey;
            }

            public string ModId { get; }
            public string Path { get; }
            public Dictionary<string, TexturePackEntry>? ByKey { get; }
            public bool Noted { get; set; }
        }

        private static readonly Dictionary<string, Pack> ByMod = new Dictionary<string, Pack>(StringComparer.Ordinal);
        private static byte[]? _buffer;

        private static string Key(string fullPath, SlotKind kind) => fullPath + "|" + (int)kind;

        public static void Start(IEnumerable<LoadedMod> mods)
        {
            foreach (var mod in mods)
            {
                var path = System.IO.Path.Combine(mod.Directory, TexturePack.FileName);
                Dictionary<string, TexturePackEntry>? byKey = null;
                if (File.Exists(path))
                {
                    try
                    {
                        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
                        var pack = TexturePack.Read(stream);
                        byKey = new Dictionary<string, TexturePackEntry>(StringComparer.OrdinalIgnoreCase);
                        foreach (var entry in pack.Entries)
                        {
                            string full;
                            try { full = System.IO.Path.GetFullPath(System.IO.Path.Combine(mod.Directory, entry.Source)); }
                            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException) { continue; }
                            if (ModPaths.IsInside(full, mod.Directory)) byKey[Key(full, entry.Kind)] = entry;
                        }
                    }
                    catch (Exception ex) when (ex is TexturePackException || ex is IOException || ex is UnauthorizedAccessException)
                    {
                        FrameworkMod.Log.Warning($"{mod.Manifest.Id}: its cooked textures (textures.tpack) cannot be used ({ex.Message}); its PNGs are used instead. Reinstall it from Tyrant.");
                        byKey = null;
                    }
                }
                ByMod[mod.Manifest.Id] = new Pack(mod.Manifest.Id, path, byKey) { Noted = byKey == null && File.Exists(path) };
            }
        }

        /// <summary>The cooked texture, or null when the PNG must be used (not cooked, edited since, unsupported, unreadable).</summary>
        public static Texture2D? Load(Replacement replacement, SlotKind kind)
        {
            if (!ByMod.TryGetValue(replacement.ModId, out var pack)) return null;
            if (pack.ByKey == null || !pack.ByKey.TryGetValue(Key(replacement.FilePath, kind), out var entry))
            {
                NoteUncooked(pack);
                return null;
            }
            TextureFingerprint source;
            try { source = TextureFingerprint.Of(replacement.FilePath); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return null; } // TextureCache reports the PNG
            if (!TexturePack.Usable(entry, source, format => SystemInfo.SupportsTextureFormat((TextureFormat)format)))
            {
                NoteUncooked(pack);
                return null;
            }

            var texture = new Texture2D(entry.Width, entry.Height, (TextureFormat)entry.Format, entry.MipCount, entry.Linear) { name = replacement.Texture };
            try
            {
                var data = texture.GetRawTextureData<byte>();
                if (data.Length != entry.Length) throw new InvalidDataException($"Unity expects {data.Length} bytes, the pack has {entry.Length}");
                var buffer = _buffer ??= new byte[1 << 20];
                using (var stream = new FileStream(pack.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096))
                {
                    stream.Position = entry.Offset;
                    long done = 0;
                    while (done < entry.Length)
                    {
                        var n = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, entry.Length - done));
                        if (n <= 0) throw new EndOfStreamException("textures.tpack is cut short");
                        NativeArray<byte>.Copy(buffer, 0, data, (int)done, n);
                        done += n;
                    }
                }
                texture.Apply(false, true); // upload; no CPU copy kept
                return texture;
            }
            catch (Exception ex)
            {
                UnityEngine.Object.Destroy(texture);
                FrameworkMod.Log.Warning($"{replacement.ModId}: cooked {entry.Source} could not be loaded ({ex.Message}); its PNG is used.");
                return null;
            }
        }

        private static void NoteUncooked(Pack pack)
        {
            if (pack.Noted) return;
            pack.Noted = true;
            FrameworkMod.Log.Msg($"{pack.ModId}: textures are not cooked (install it from Tyrant for faster, lighter loading).");
        }
    }
}
