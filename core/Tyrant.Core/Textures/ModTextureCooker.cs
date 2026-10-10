using System.Security.Cryptography;
using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Textures;

public sealed record CookTarget(string File, SlotKind Kind, TextureCompression Mode);

public sealed record TextureEstimate(string File, SlotKind Kind, TextureCompression Mode, bool Override, int Format, int Width, int Height, long Bytes);

/// <summary>
/// Cooks a mod's skin-slot and replacement textures into textures.tpack (install and export). Each cooked texture is kept
/// in the workspace cache by its PNG's contents, kind, mode and cooker version, so a reinstall cooks only what changed.
/// </summary>
public sealed class ModTextureCooker(string cacheDir, Func<string, SlotKind?>? replacementKind = null)
{
    internal int Cooked { get; private set; }

    public static ModTextureCooker For(Workspace ws, IReadOnlyList<AssetRecord>? assets, IReadOnlyList<SpeciesSkins>? species) =>
        new(Path.Combine(ws.CacheDir, "cooked-textures"), ReplacementKinds(assets, species));

    /// <summary>Texture name → the kind of the vanilla skin slot it fills (the property the framework sets it on); null without the index or dump.</summary>
    public static Func<string, SlotKind?>? ReplacementKinds(IReadOnlyList<AssetRecord>? assets, IReadOnlyList<SpeciesSkins>? species)
    {
        if (assets is null || species is null) return null;
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets)
            if (asset.Type == "Texture2D" && asset.Guid is { Length: > 0 } guid) names.TryAdd(guid, asset.Name);
        var kinds = new Dictionary<string, SlotKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var skin in species.SelectMany(s => s.Skins))
            foreach (var (slot, guid) in skin.Male.Concat(skin.Female))
                if (names.TryGetValue(guid, out var name)) kinds.TryAdd(name, SkinSlotNames.KindOf(slot));
        return name => kinds.TryGetValue(name, out var kind) ? kind : null;
    }

    /// <summary>The file's full path when it lies inside the mod folder; null otherwise (a stranger's mod.json may name any path, and nothing outside the mod is opened).</summary>
    internal static string? InsidePath(string modDir, string file)
    {
        try
        {
            var path = Path.GetFullPath(Path.Combine(modDir, file));
            return ModPaths.IsInside(path, modDir) ? path : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    private static string Normal(string file) => file.Replace('\\', '/');

    /// <summary>Every texture the framework builds for the mod: skin slots, then replacements; each file once.</summary>
    public static IReadOnlyList<string> TextureFiles(ModManifest manifest) =>
        manifest.Skins.SelectMany(s => (s.Male?.Values ?? Enumerable.Empty<string>()).Concat(s.Female?.Values ?? Enumerable.Empty<string>()))
            .Concat(manifest.Replace.Select(r => r.File))
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(Normal)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public IReadOnlyList<CookTarget> Targets(ModManifest manifest)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new List<CookTarget>();
        void Add(string file, SlotKind kind)
        {
            if (string.IsNullOrWhiteSpace(file)) return;
            var normal = Normal(file);
            if (seen.Add(normal + "|" + kind)) targets.Add(new CookTarget(normal, kind, manifest.Textures.For(normal)));
        }
        foreach (var skin in manifest.Skins)
            foreach (var slots in new[] { skin.Male, skin.Female })
                foreach (var (slot, file) in slots ?? new Dictionary<string, string>())
                    Add(file, SkinSlotNames.KindOf(slot));
        foreach (var r in manifest.Replace)
            Add(r.File, replacementKind?.Invoke(r.Texture) ?? (NormalMap.IsCandidate(r.Texture) ? SlotKind.Normal : MaterialSwap.KindOf(r.Texture)));
        return targets;
    }

    public IReadOnlyList<TextureEstimate> Estimate(ModManifest manifest, string modDir)
    {
        var result = new List<TextureEstimate>();
        foreach (var t in Targets(manifest))
        {
            if (InsidePath(modDir, t.File) is not { } path || PngFacts.Read(path) is not { } fact) continue;
            var format = TextureCooker.FormatFor(t.Kind, t.Mode, fact.Opaque);
            var (w, h) = TextureCooker.CookedSize(fact.Width, fact.Height);
            result.Add(new TextureEstimate(t.File, t.Kind, t.Mode, manifest.Textures.Overrides.ContainsKey(t.File), format, w, h,
                UnityTextureFormats.DataLength(format, w, h, UnityTextureFormats.FullMipCount(w, h))));
        }
        return result;
    }

    public string? WritePack(ModManifest manifest, string sourceDir, string outDir, IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        var output = Path.Combine(outDir, TexturePack.FileName);
        var targets = Targets(manifest);
        if (targets.Count == 0)
        {
            if (File.Exists(output)) File.Delete(output);
            return null;
        }
        var cached = new List<(string Path, TexturePackEntry Entry)>();
        for (var i = 0; i < targets.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var t = targets[i];
            var png = InsidePath(sourceDir, t.File);
            if (png is null || !File.Exists(png))
                throw new TyrantException(TyrantErrorCode.ModInvalid, $"{t.File} is missing, so the mod's textures could not be cooked.");
            var (path, entry) = Cache(png, t);
            if (entry.Width > TexturePack.MaxSide || entry.Height > TexturePack.MaxSide)
                throw new TyrantException(TyrantErrorCode.ModInvalid, $"{t.File} is {entry.Width}x{entry.Height}; textures can be at most {TexturePack.MaxSide} pixels on a side.");
            entry.Source = t.File;
            entry.Fingerprint = TextureFingerprint.Of(png);
            cached.Add((path, entry));
            progress?.Report((i + 1, targets.Count));
        }
        Directory.CreateDirectory(outDir);
        var temp = output + ".writing";
        try
        {
            using (var stream = File.Create(temp))
                TexturePack.Write(stream, cached.Select(c => c.Entry).ToList(), (i, s) =>
                {
                    using var source = File.OpenRead(cached[i].Path);
                    source.Position = DataOffset(source);
                    CopyExactly(source, s, cached[i].Entry.Length);
                });
            File.Move(temp, output, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        return output;
    }

    /// <summary>The cooked texture as a one-entry pack in the cache (cooked now when missing or unreadable).</summary>
    private (string Path, TexturePackEntry Entry) Cache(string png, CookTarget t)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(png); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"{t.File} could not be read ({ex.Message}); is it open in another program?");
        }
        var key = $"{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}-{(int)t.Kind}-{TextureCompressions.Name(t.Mode)}-v{TextureCooker.Version}";
        var path = Path.Combine(cacheDir, key[..2], key + ".tpack");
        if (File.Exists(path))
        {
            try
            {
                using var stream = File.OpenRead(path);
                return (path, TexturePack.Read(stream).Entries.Single());
            }
            catch (Exception ex) when (ex is TexturePackException or IOException or InvalidOperationException) { } // cook again
        }
        CookedTexture cooked;
        try
        {
            using var input = new MemoryStream(bytes);
            var image = StbImageSharp.ImageResult.FromStream(input, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            cooked = TextureCooker.Cook(image.Data, image.Width, image.Height, t.Kind, t.Mode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"{t.File} could not be cooked: it is not a readable PNG ({ex.Message}).");
        }
        Cooked++;
        var entry = new TexturePackEntry
        {
            Source = t.File, Kind = t.Kind, Mode = t.Mode, Format = cooked.Format, Linear = cooked.Linear,
            Width = cooked.Width, Height = cooked.Height, MipCount = cooked.MipCount, Length = cooked.Data.Length,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".writing";
        using (var stream = File.Create(temp)) TexturePack.Write(stream, [entry], (_, s) => s.Write(cooked.Data));
        File.Move(temp, path, overwrite: true);
        return (path, entry);
    }

    private static long DataOffset(Stream onePack)
    {
        var entry = TexturePack.Read(onePack).Entries.Single();
        return entry.Offset;
    }

    private static void CopyExactly(Stream from, Stream to, long count)
    {
        var buffer = new byte[1 << 20];
        while (count > 0)
        {
            var n = from.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
            if (n <= 0) throw new EndOfStreamException("A cooked texture in the cache is cut short.");
            to.Write(buffer, 0, n);
            count -= n;
        }
    }
}
