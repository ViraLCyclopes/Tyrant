using System.Collections.Concurrent;
using StbImageSharp;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Textures;

public sealed record PngFact(int Width, int Height, bool Opaque);

/// <summary>A PNG's size and whether it has transparency, cached until the file changes (the app asks often).</summary>
public static class PngFacts
{
    private static readonly ConcurrentDictionary<string, (long Length, DateTime Written, PngFact Fact)> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static PngFact? Read(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            var full = info.FullName;
            if (Cache.TryGetValue(full, out var cached) && cached.Length == info.Length && cached.Written == info.LastWriteTimeUtc) return cached.Fact;
            using var stream = File.OpenRead(full);
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            var fact = new PngFact(image.Width, image.Height, Pixels.IsOpaque(image.Data));
            Cache[full] = (info.Length, info.LastWriteTimeUtc, fact);
            return fact;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}
