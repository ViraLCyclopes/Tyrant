using StbImageSharp;
using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>Errors block an install; warnings are shown but do not.</summary>
public sealed record ModCheckResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool Ok => Errors.Count == 0;
}

/// <summary>Finds what would go wrong in the game before a mod is installed.</summary>
public sealed class ModChecker(Func<AssetRecord, (int Width, int Height)?> sizeOf)
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
    });

    public ModCheckResult Check(ModProject mod, AssetIndex? index)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (mod.Manifest.Replace.Count == 0 && mod.Manifest.Assembly is null)
            warnings.Add("The mod does nothing yet: add a texture replacement.");
        if (index is null && mod.Manifest.Replace.Count > 0)
            warnings.Add("There is no asset index, so the target textures were not checked. Click Index assets on the Home tab.");

        foreach (var group in mod.Manifest.Replace.GroupBy(r => r.Texture, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            warnings.Add($"{group.Key} is replaced {group.Count()} times; only the last one is used.");

        foreach (var entry in mod.Manifest.Replace)
        {
            var path = Path.GetFullPath(Path.Combine(mod.Dir, entry.File));
            if (!ModPaths.IsInside(path, mod.Dir))
            {
                errors.Add($"{entry.Texture}: \"{entry.File}\" points outside the mod folder.");
                continue;
            }
            if (!File.Exists(path))
            {
                errors.Add($"{entry.Texture}: {entry.File} is missing.");
                continue;
            }
            if (!HasPngSignature(path))
            {
                errors.Add($"{entry.Texture}: {entry.File} is not a PNG (other image formats are not supported in game).");
                continue;
            }

            ImageResult? image;
            try
            {
                using var stream = File.OpenRead(path);
                image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException)
            {
                image = null;
            }
            if (image is null)
            {
                errors.Add($"{entry.Texture}: {entry.File} is not a readable PNG.");
                continue;
            }

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
        return new ModCheckResult(errors, warnings);
    }

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    internal static bool HasPngSignature(string path)
    {
        using var stream = File.OpenRead(path);
        var head = new byte[PngSignature.Length];
        return stream.Read(head, 0, head.Length) == head.Length && head.AsSpan().SequenceEqual(PngSignature);
    }
}
