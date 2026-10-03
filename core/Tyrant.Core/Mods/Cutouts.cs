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
            if (NormalMap.IsCandidate(entry.Texture)) continue;
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

    /// <summary>Decodes a vanilla texture from the game's bundles (through a temporary PNG); null when it cannot be read, or when
    /// onlyIfAlpha and its format stores no alpha.</summary>
    public static Func<AssetRecord, ImageResult?> GamePixels(GameInstall install, IAssetReader reader, bool onlyIfAlpha = true) => texture =>
    {
        var temp = Path.Combine(Path.GetTempPath(), "tyrant-cutouts", Guid.NewGuid().ToString("N") + ".png");
        try
        {
            if (onlyIfAlpha)
                using (var session = new AssetSession(install))
                    if (!MayHaveAlpha(TextureFacts.Read(session.Open(texture).BaseField).Format)) return null; // opaque by format: no decode
            Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
            reader.WriteTexture(install, texture, temp);
            using var stream = File.OpenRead(temp);
            return ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { }
        }
    };
}

/// <summary>Copies the vanilla transparency into the mod's colour PNGs that lost it (colours are kept; sizes may differ).</summary>
public sealed class CutoutRestorer(Func<AssetRecord, ImageResult?> pixelsOf)
{
    /// <returns>The mod-relative files that were fixed.</returns>
    public IReadOnlyList<string> Restore(ModProject mod, AssetIndex index, IReadOnlyList<SpeciesSkins>? species)
    {
        var fixedFiles = new List<string>();
        foreach (var (file, vanilla) in Cutouts.Targets(mod, index, species).DistinctBy(t => t.File, StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.GetFullPath(Path.Combine(mod.Dir, file));
            if (!ModPaths.IsInside(path, mod.Dir) || !File.Exists(path)) continue;
            ImageResult image;
            using (var stream = File.OpenRead(path)) image = ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            if (Cutouts.SeeThrough(image.Data) >= Cutouts.Lost) continue;
            var source = pixelsOf(vanilla);
            if (source is null || Cutouts.SeeThrough(source.Data) < Cutouts.UsesCutouts) continue;
            for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
            {
                var sx = x * source.Width / image.Width;
                var sy = y * source.Height / image.Height;
                image.Data[(y * image.Width + x) * 4 + 3] = source.Data[(sy * source.Width + sx) * 4 + 3];
            }
            using (var stream = File.Create(path))
                new ImageWriter().WritePng(image.Data, image.Width, image.Height, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
            fixedFiles.Add(file);
        }
        return fixedFiles;
    }
}
