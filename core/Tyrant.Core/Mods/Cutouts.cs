using StbImageSharp;
using StbImageWriteSharp;
using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>
/// Cutouts: feathers and hair drawn as cards that a diffuse's transparency cuts out (e.g. Velociraptor). A mod PNG saved
/// without transparency turns them into solid shapes; this finds such PNGs and can copy the vanilla transparency back.
/// </summary>
public static class Cutouts
{
    /// <summary>A vanilla texture with at least this share of see-through pixels uses cutouts.</summary>
    public const double UsesCutouts = 0.01;

    /// <summary>A mod PNG with less than this share of see-through pixels has lost them.</summary>
    public const double Lost = 0.001;

    private static readonly HashSet<string> NoAlpha = new(StringComparer.OrdinalIgnoreCase)
    {
        "DXT1", "DXT1Crunched", "RGB24", "RGB565", "RGB48", "R8", "R16", "RG16", "RG32", "RFloat", "RGFloat", "RHalf", "RGHalf",
        "BC4", "BC5", "BC6H", "ETC_RGB4", "ETC_RGB4Crunched", "ETC2_RGB", "EAC_R", "EAC_R_SIGNED", "EAC_RG", "EAC_RG_SIGNED",
        "PVRTC_RGB2", "PVRTC_RGB4", "ATC_RGB4", "YUY2",
    };

    /// <summary>False for texture formats that store no alpha (DXT1, RGB24, …): such a texture cannot cut anything out.</summary>
    public static bool MayHaveAlpha(string format) => !NoAlpha.Contains(format);

    /// <summary>A colour (diffuse) texture by the game's naming: T_…_D, …_infant_D, …Diffuse.</summary>
    public static bool IsColour(string textureName) =>
        textureName.EndsWith("_D", StringComparison.OrdinalIgnoreCase) || textureName.EndsWith("Diffuse", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Targets grouped by file (one PNG can serve several, e.g. male and female; spellings like a/b.png and a\b.png are one
    /// file), so the decision uses every vanilla texture the file stands for.
    /// </summary>
    public static IEnumerable<(string File, IReadOnlyList<AssetRecord> Vanilla)> ByFile(ModProject mod, AssetIndex index, IReadOnlyList<SpeciesSkins>? species) =>
        Targets(mod, index, species)
            .GroupBy(t => t.File.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.First().File, (IReadOnlyList<AssetRecord>)g.Select(t => t.Vanilla).DistinctBy(v => v.Guid ?? $"{v.Bundle}#{v.PathId}").ToList()));

    public static double SeeThrough(byte[] rgba)
    {
        var pixels = rgba.Length / 4;
        if (pixels == 0) return 0;
        var clear = 0;
        for (var i = 3; i < rgba.Length; i += 4)
            if (rgba[i] < 128) clear++;
        return (double)clear / pixels;
    }

    /// <summary>The mod's colour PNGs and the vanilla texture each stands for: replacements (not normal maps) and skins' diffuse slots.</summary>
    public static IEnumerable<(string File, AssetRecord Vanilla)> Targets(ModProject mod, AssetIndex index, IReadOnlyList<SpeciesSkins>? species)
    {
        foreach (var entry in mod.Manifest.Replace)
        {
            if (!IsColour(entry.Texture)) continue; // normal maps and masks (extra, pattern, fur) keep data in alpha
            var target = index.Assets.FirstOrDefault(a => a.Type == "Texture2D" && string.Equals(a.Name, entry.Texture, StringComparison.OrdinalIgnoreCase));
            if (target is not null) yield return (entry.File, target);
        }
        if (species is null) yield break;
        foreach (var skin in mod.Manifest.Skins)
        {
            var target = species.FirstOrDefault(s => string.Equals(s.SpeciesId, skin.Species, StringComparison.OrdinalIgnoreCase));
            var based = target is null ? null
                : int.TryParse(skin.Base, out var number) ? target.Skins.FirstOrDefault(s => s.Index == number)
                : target.Skins.FirstOrDefault(s => string.Equals(s.Name, skin.Base, StringComparison.OrdinalIgnoreCase));
            if (based is null) continue;
            foreach (var (files, textures) in new[] { (skin.Male, based.Male), (skin.Female, based.Female) })
            {
                if (files is null) continue;
                foreach (var slot in new[] { "diffuse", "infantDiffuse" })
                    if (files.TryGetValue(slot, out var file) && textures.TryGetValue(slot, out var guid)
                        && index.Assets.FirstOrDefault(a => string.Equals(a.Guid, guid, StringComparison.OrdinalIgnoreCase)) is { } vanilla)
                        yield return (file, vanilla);
            }
        }
    }

    /// <summary>One opaque pixel: stands for a texture whose format cannot cut anything out.</summary>
    private static ImageResult Opaque() => new() { Width = 1, Height = 1, Comp = StbImageSharp.ColorComponents.RedGreenBlueAlpha, SourceComp = StbImageSharp.ColorComponents.RedGreenBlueAlpha, Data = [255, 255, 255, 255] };

    /// <summary>Decodes a vanilla texture from the game's bundles (through a temporary PNG); see <see cref="Pixels"/>.</summary>
    public static Func<AssetRecord, ImageResult?> GamePixels(GameInstall install, IAssetReader reader, bool onlyIfAlpha = true) => Pixels(
        texture =>
        {
            using var session = new AssetSession(install);
            return TextureFacts.Read(session.Open(texture).BaseField).Format;
        },
        texture =>
        {
            var temp = Path.Combine(Path.GetTempPath(), "tyrant-cutouts", Guid.NewGuid().ToString("N") + ".png");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
                reader.WriteTexture(install, texture, temp);
                using var stream = File.OpenRead(temp);
                return ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            }
            finally
            {
                try { File.Delete(temp); } catch (IOException) { }
            }
        },
        onlyIfAlpha);

    /// <summary>
    /// A texture's pixels; null only when it cannot be read. With onlyIfAlpha, a format that stores no alpha is not decoded and
    /// gives <see cref="Opaque"/> (no cutouts, and nothing went wrong).
    /// </summary>
    public static Func<AssetRecord, ImageResult?> Pixels(Func<AssetRecord, string> formatOf, Func<AssetRecord, ImageResult> decode, bool onlyIfAlpha = true) => texture =>
    {
        try
        {
            if (onlyIfAlpha && !MayHaveAlpha(formatOf(texture))) return Opaque(); // opaque by format: no decode
            return decode(texture);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    };
}

/// <summary>What Restore cutouts did: the files fixed, and the ones it had to leave (with why).</summary>
public sealed record CutoutRestore(IReadOnlyList<string> Restored, IReadOnlyList<string> Problems);

/// <summary>Copies the vanilla transparency into the mod's colour PNGs that lost it (colours are kept; sizes may differ).</summary>
public sealed class CutoutRestorer(Func<AssetRecord, ImageResult?> pixelsOf)
{
    public CutoutRestore Restore(ModProject mod, AssetIndex index, IReadOnlyList<SpeciesSkins>? species)
    {
        var restored = new List<string>();
        var problems = new List<string>();
        foreach (var (file, vanillas) in Cutouts.ByFile(mod, index, species))
        {
            var path = Path.GetFullPath(Path.Combine(mod.Dir, file));
            if (!ModPaths.IsInside(path, mod.Dir) || !File.Exists(path)) continue;
            ImageResult image;
            try
            {
                if (!ModChecker.HasPngSignature(path))
                {
                    problems.Add($"{file} is not a PNG, so it was left alone.");
                    continue;
                }
                using var stream = File.OpenRead(path);
                image = ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                problems.Add($"{file} could not be read ({ex.Message}); is it open in another program?");
                continue;
            }
            if (Cutouts.SeeThrough(image.Data) >= Cutouts.Lost) continue;

            ImageResult? source = null;
            var best = 0.0;
            var unreadable = new List<string>();
            foreach (var vanilla in vanillas)
            {
                var pixels = pixelsOf(vanilla);
                if (pixels is null) { unreadable.Add(vanilla.Name); continue; }
                var share = Cutouts.SeeThrough(pixels.Data);
                if (share > best) (best, source) = (share, pixels);
            }
            if (source is null || best < Cutouts.UsesCutouts)
            {
                if (unreadable.Count > 0) problems.Add($"{file}: the game texture {string.Join(", ", unreadable)} could not be read, so its cutouts are unknown.");
                continue;
            }

            for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
            {
                var sx = x * source.Width / image.Width;
                var sy = y * source.Height / image.Height;
                image.Data[(y * image.Width + x) * 4 + 3] = source.Data[(sy * source.Width + sx) * 4 + 3];
            }
            var temp = path + ".tyrant-tmp";
            try
            {
                using (var stream = File.Create(temp))
                    new ImageWriter().WritePng(image.Data, image.Width, image.Height, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
                File.Move(temp, path, overwrite: true); // a failure before this line leaves the PNG as it was
                restored.Add(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                try { File.Delete(temp); } catch (IOException) { }
                problems.Add($"{file} could not be written ({ex.Message}); it was left as it was.");
            }
        }
        return new CutoutRestore(restored, problems);
    }
}
