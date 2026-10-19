using System.Security.Cryptography;
using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Textures;

public sealed record CookTarget(string File, SlotKind Kind, TextureCompression Mode);

public sealed record TextureEstimate(string File, SlotKind Kind, TextureCompression Mode, bool Override, int Format, int Width, int Height, long Bytes);

/// <summary>A texture starting to cook (cached ones too): "Cooking textures 2/12 (skins/blue/male_N.png)...".</summary>
public sealed record CookProgress(int Number, int Total, string File, bool Slower = false)
{
    /// <summary>Textures done so far, as a fraction of all.</summary>
    public double Fraction => (double)(Number - 1) / Total;

    public string Message => $"Cooking textures {Number}/{Total} ({File}{(Slower ? ", without the graphics card: slower" : "")})...";
}

/// <summary>
/// Cooks a mod's skin-slot and replacement textures into textures.tpack (install and export). Each cooked texture is kept
/// in the workspace cache by its PNG's contents, kind, mode and cooker version, so a reinstall cooks only what changed.
/// </summary>
public sealed class ModTextureCooker(string cacheDir, Func<string, SlotKind?>? replacementKind = null, Texconv? texconv = null)
{
    internal int Cooked { get; private set; }

    public static ModTextureCooker For(Workspace ws, IReadOnlyList<AssetRecord>? assets, IReadOnlyList<SpeciesSkins>? species) =>
        new(CacheDirOf(ws), ReplacementKinds(assets, species));

    /// <summary>Where a workspace keeps its cooked textures.</summary>
    public static string CacheDirOf(Workspace ws) => Path.Combine(ws.CacheDir, "cooked-textures");

    /// <summary>The cache file a PNG with these bytes cooks to as this kind and mode.</summary>
    private static string CachePath(string cacheDir, byte[] png, SlotKind kind, TextureCompression mode)
    {
        var key = $"{Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant()}-{(int)kind}-{TextureCompressions.Name(mode)}-v{TextureCooker.Version}";
        return Path.Combine(cacheDir, key[..2], key + ".tpack");
    }

    /// <summary>
    /// Puts a pack entry in the cache as the cooked form of <paramref name="png"/> (a PNG extracted from that pack), so
    /// cooking it again reuses the original instead of compressing an already compressed texture a second time.
    /// </summary>
    public static void Seed(string cacheDir, string png, Stream pack, TexturePackEntry entry)
    {
        var path = CachePath(cacheDir, File.ReadAllBytes(png), entry.Kind, entry.Mode);
        var copy = new TexturePackEntry
        {
            Source = entry.Source, Kind = entry.Kind, Mode = entry.Mode, Format = entry.Format, Linear = entry.Linear,
            Width = entry.Width, Height = entry.Height, MipCount = entry.MipCount, Length = entry.Length,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".writing";
        using (var stream = File.Create(temp))
            TexturePack.Write(stream, [copy], (_, s) =>
            {
                pack.Position = entry.Offset;
                CopyExactly(pack, s, entry.Length);
            });
        File.Move(temp, path, overwrite: true);
    }

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
            .Concat(MaskFiles(manifest))
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(Normal)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// The iridescence and fur mask files of the skins', species' and vanilla skins' material settings: data textures the
    /// framework loads from the pack. Ramps are not here: the game samples them on the CPU and reads their PNGs directly.
    /// </summary>
    private static IEnumerable<string> MaskFiles(ModManifest manifest) =>
        manifest.Skins.Select(s => s.Materials).Concat(manifest.SpeciesMaterials.Values).Concat(manifest.VanillaSkinMaterials.Values)
            .Where(materials => materials is not null)
            .SelectMany(materials => materials!.Values)
            .SelectMany(material => new[] { material.Iridescence?.Mask?.File, material.Fur?.Mask?.File, material.Fur?.InfantMask?.File })
            .Where(file => !string.IsNullOrWhiteSpace(file))!;

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
        foreach (var mask in MaskFiles(manifest)) Add(mask, SlotKind.Data);
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

    public string? WritePack(ModManifest manifest, string sourceDir, string outDir, IProgress<CookProgress>? progress = null, CancellationToken ct = default)
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
            var number = i + 1;
            progress?.Report(new CookProgress(number, targets.Count, t.File));
            var png = InsidePath(sourceDir, t.File);
            if (png is null || !File.Exists(png))
                throw new TyrantException(TyrantErrorCode.ModInvalid, $"{t.File} is missing, so the mod's textures could not be cooked.");
            var (path, entry) = Cache(png, t, () => progress?.Report(new CookProgress(number, targets.Count, t.File, Slower: true)), ct);
            if (entry.Width > TexturePack.MaxSide || entry.Height > TexturePack.MaxSide)
                throw new TyrantException(TyrantErrorCode.ModInvalid, $"{t.File} is {entry.Width}x{entry.Height}; textures can be at most {TexturePack.MaxSide} pixels on a side.");
            entry.Source = t.File;
            entry.Fingerprint = TextureFingerprint.Of(png);
            cached.Add((path, entry));
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

    /// <summary>Cooks the mod's textures.tpack into its workspace folder (an old one is removed when nothing is cooked); null when there is nothing to cook.</summary>
    public string? CookInPlace(ModProject mod, IProgress<CookProgress>? progress = null, CancellationToken ct = default) =>
        WritePack(mod.Manifest, mod.Dir, mod.Dir, progress, ct);

    /// <summary>
    /// Install's last step before the copy goes into the game: the workspace pack, cooked now, the same file in the staged
    /// copy, and the staged copy made slim (the PNGs the pack holds left out; auto thumbnails; its mod.json).
    /// </summary>
    public void CookForInstall(ModProject mod, string staging, IProgress<CookProgress>? progress = null, CancellationToken ct = default)
    {
        var staged = Path.Combine(staging, TexturePack.FileName);
        if (CookInPlace(mod, progress, ct) is not { } pack)
        {
            if (File.Exists(staged)) File.Delete(staged);
            return;
        }
        File.Copy(pack, staged, overwrite: true);
        TexturePack read;
        using (var stream = File.OpenRead(staged)) read = TexturePack.Read(stream);
        SlimCopy.Apply(SlimCopy.Plan(mod.Manifest, staging, read), staging);
    }

    /// <summary>
    /// The pack in a mod folder against its PNGs: "none" (nothing to cook), "missing", "stale" (unreadable, or a texture
    /// missing, edited since, or due another format) or "current".
    /// </summary>
    public string PackState(ModManifest manifest, string modDir)
    {
        if (Targets(manifest).Count == 0) return "none";
        var path = Path.Combine(modDir, TexturePack.FileName);
        if (!File.Exists(path)) return "missing";
        TexturePack pack;
        try
        {
            using var stream = File.OpenRead(path);
            pack = TexturePack.Read(stream);
        }
        catch (Exception ex) when (ex is TexturePackException or IOException or UnauthorizedAccessException)
        {
            return "stale";
        }
        foreach (var e in Estimate(manifest, modDir))
        {
            var print = CookedPacks.Print(InsidePath(modDir, e.File)!);
            if (!pack.Entries.Any(p => string.Equals(p.Source, e.File, StringComparison.OrdinalIgnoreCase) && p.Kind == e.Kind && p.Format == e.Format
                                       && p.Fingerprint.Equals(print)))
                return "stale";
        }
        return "current";
    }

    /// <summary>The cooked texture as a one-entry pack in the cache (cooked now when missing or unreadable).</summary>
    private (string Path, TexturePackEntry Entry) Cache(string png, CookTarget t, Action slower, CancellationToken ct)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(png); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"{t.File} could not be read ({ex.Message}); is it open in another program?");
        }
        var path = CachePath(cacheDir, bytes, t.Kind, t.Mode);
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
        var fellBack = false;
        try
        {
            using var input = new MemoryStream(bytes);
            var image = StbImageSharp.ImageResult.FromStream(input, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            cooked = TextureCooker.Cook(image.Data, image.Width, image.Height, t.Kind, t.Mode, texconv, () => { fellBack = true; slower(); }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"{t.File} could not be cooked: it is not a readable PNG ({ex.Message}).");
        }
        Cooked++;
        // BC7 cooked without texconv is kept under another name, so the next install tries texconv again instead of reusing it.
        if (fellBack) path = path[..^".tpack".Length] + ".fallback.tpack";
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
