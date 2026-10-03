using StbImageSharp;
using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>Errors block an install; warnings are shown but do not.</summary>
public sealed record ModCheckResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool Ok => Errors.Count == 0;

    /// <summary>Mod-relative colour PNGs without the transparency their vanilla texture cuts feathers or hair out with.</summary>
    public IReadOnlyList<string> MissingCutouts { get; init; } = [];
}

/// <summary>Finds what would go wrong in the game before a mod is installed.</summary>
public sealed class ModChecker(Func<AssetRecord, (int Width, int Height)?> sizeOf, Func<AssetRecord, ImageResult?>? pixelsOf = null)
{
    /// <summary>Reads original texture sizes from the game's bundles; a texture that cannot be read just skips the size check.</summary>
    public static ModChecker ForGame(GameInstall install) => new(texture =>
    {
        try
        {
            using var session = new AssetSession(install);
            var facts = TextureFacts.Read(session.Open(texture).BaseField);
            return (facts.Width, facts.Height);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }, Cutouts.GamePixels(install, new BundleAssetReader()));

    public ModCheckResult Check(ModProject mod, AssetIndex? index, IReadOnlyList<SpeciesSkins>? species = null)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (mod.Manifest.Replace.Count == 0 && mod.Manifest.Skins.Count == 0 && mod.Manifest.Assembly is null)
            warnings.Add("The mod does nothing yet: add a texture replacement or a skin.");
        if (index is null && mod.Manifest.Replace.Count > 0)
            warnings.Add("There is no asset index, so the target textures were not checked. Click Index assets on the Home tab.");

        foreach (var group in mod.Manifest.Replace.GroupBy(r => r.Texture, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            warnings.Add($"{group.Key} is replaced {group.Count()} times; only the last one is used.");

        foreach (var entry in mod.Manifest.Replace)
        {
            var image = Image(mod, entry.Texture, entry.File, errors);
            if (image is null) continue;

            if (index is not null)
            {
                var target = index.Assets.FirstOrDefault(a => a.Type == "Texture2D" && string.Equals(a.Name, entry.Texture, StringComparison.OrdinalIgnoreCase));
                if (target is null)
                {
                    errors.Add($"{entry.Texture} is not in the asset index; check the name (the game would never use this file).");
                    continue;
                }
                if (sizeOf(target) is { } original && (original.Width, original.Height) != (image.Width, image.Height))
                    warnings.Add($"{entry.Texture}: {entry.File} is {image.Width}x{image.Height} but the original is {original.Width}x{original.Height}; it will look stretched or blurry.");
            }

            if (NormalMap.IsCandidate(entry.Texture) && NormalMaps.Classify(image.Data) == NormalMapKind.Unknown)
                warnings.Add($"{entry.Texture}: {entry.File} does not look like a normal map (neither the blue-purple standard form nor Unity's packed form).");
        }

        if (species is null && mod.Manifest.Skins.Count > 0)
            warnings.Add("There is no data dump, so the skins' species and base skins were not checked (Home → Run data dump).");
        foreach (var skin in mod.Manifest.Skins)
        {
            var key = skin.Key(mod.Id);
            VanillaSkin? based = null;
            if (species is not null)
            {
                var target = species.FirstOrDefault(s => string.Equals(s.SpeciesId, skin.Species, StringComparison.OrdinalIgnoreCase));
                if (target is null) errors.Add($"{key}: species \"{skin.Species}\" is not in the game data.");
                else
                {
                    based = int.TryParse(skin.Base, out var number) ? target.Skins.FirstOrDefault(s => s.Index == number)
                        : target.Skins.FirstOrDefault(s => string.Equals(s.Name, skin.Base, StringComparison.OrdinalIgnoreCase));
                    if (based is null) errors.Add($"{key}: base skin \"{skin.Base}\" is not one of {target.SpeciesId}'s skins ({string.Join(", ", target.Skins.Select(s => s.Name))}).");
                }
            }
            if (skin.Thumbnail is not null) Image(mod, $"{key} thumbnail", skin.Thumbnail, errors);
            foreach (var (sex, files, baseTextures) in new[] { ("male", skin.Male, based?.Male), ("female", skin.Female, based?.Female) })
            {
                if (files is null) continue;
                foreach (var (slot, file) in files)
                {
                    var image = Image(mod, $"{key} {sex} {slot}", file, errors);
                    if (image is null) continue;
                    if (index is not null && baseTextures is not null && baseTextures.TryGetValue(slot, out var guid)
                        && index.Assets.FirstOrDefault(a => string.Equals(a.Guid, guid, StringComparison.OrdinalIgnoreCase)) is { } original
                        && sizeOf(original) is { } size && (size.Width, size.Height) != (image.Width, image.Height))
                        warnings.Add($"{key} {sex} {slot}: {file} is {image.Width}x{image.Height} but the base texture is {size.Width}x{size.Height}.");
                    if (SkinSlotNames.KindOf(slot) == SlotKind.Normal && NormalMaps.Classify(image.Data) == NormalMapKind.Unknown)
                        warnings.Add($"{key} {sex} {slot}: {file} does not look like a normal map.");
                }
            }
        }
        var missing = index is null || pixelsOf is null ? [] : MissingCutouts(mod, index, species, warnings);
        return new ModCheckResult(errors, warnings) { MissingCutouts = missing };
    }

    /// <summary>Opaque colour PNGs whose vanilla texture is partly see-through; the vanilla one is only decoded for those.</summary>
    private List<string> MissingCutouts(ModProject mod, AssetIndex index, IReadOnlyList<SpeciesSkins>? species, List<string> warnings)
    {
        var missing = new List<string>();
        var vanillaShare = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (file, vanilla) in Cutouts.Targets(mod, index, species).DistinctBy(t => t.File, StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.GetFullPath(Path.Combine(mod.Dir, file));
            if (!ModPaths.IsInside(path, mod.Dir) || !File.Exists(path)) continue;
            double share;
            try
            {
                using var stream = File.OpenRead(path);
                share = Cutouts.SeeThrough(ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha).Data);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException)
            {
                continue; // already reported as unreadable
            }
            if (share >= Cutouts.Lost) continue;
            var key = vanilla.Guid ?? $"{vanilla.Bundle}#{vanilla.PathId}";
            if (!vanillaShare.TryGetValue(key, out var original))
                vanillaShare[key] = original = pixelsOf!(vanilla) is { } pixels ? Cutouts.SeeThrough(pixels.Data) : 0;
            if (original < Cutouts.UsesCutouts) continue;
            missing.Add(file);
            warnings.Add($"{file} has no see-through pixels, but {vanilla.Name} cuts feathers or hair out with them ({original:P0} of it is see-through): " +
                         "in game those parts would show as solid shapes. Keep the alpha channel when saving, or use Restore cutouts.");
        }
        return missing;
    }

    /// <summary>Checks one PNG of the mod; returns the decoded image, or null after adding an error.</summary>
    private static ImageResult? Image(ModProject mod, string label, string file, List<string> errors)
    {
        var path = Path.GetFullPath(Path.Combine(mod.Dir, file));
        if (!ModPaths.IsInside(path, mod.Dir)) { errors.Add($"{label}: \"{file}\" points outside the mod folder."); return null; }
        if (!File.Exists(path)) { errors.Add($"{label}: {file} is missing."); return null; }
        if (!HasPngSignature(path)) { errors.Add($"{label}: {file} is not a PNG (other image formats are not supported in game)."); return null; }
        try
        {
            using var stream = File.OpenRead(path);
            return ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException)
        {
            errors.Add($"{label}: {file} is not a readable PNG.");
            return null;
        }
    }

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    internal static bool HasPngSignature(string path)
    {
        using var stream = File.OpenRead(path);
        var head = new byte[PngSignature.Length];
        return stream.Read(head, 0, head.Length) == head.Length && head.AsSpan().SequenceEqual(PngSignature);
    }
}
