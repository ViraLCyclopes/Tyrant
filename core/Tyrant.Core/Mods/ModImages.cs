using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using StbImageSharp;
using StbImageWriteSharp;
using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>Small images for the mod editor: file thumbnails and colour previews, written under the workspace's preview cache.</summary>
public sealed class ModImages(Func<AssetRecord, ImageResult?> gamePixels)
{
    /// <summary>Shrunk maps by source (file path + size + write time, or game texture GUID) and preview size.</summary>
    private static readonly ConcurrentDictionary<string, ImageResult> Maps = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> ColorPreview(ModProject mod, string skinId, string? colorsJson, string variant, string sex, int seed, int count, int size,
        string outDir, AssetIndex? index, IReadOnlyList<SpeciesSkins>? species)
    {
        var skin = mod.Skin(skinId);
        SkinColors? colors;
        try
        {
            colors = string.IsNullOrWhiteSpace(colorsJson) ? skin.Colors : SkinColors.Parse(Json.Parse(colorsJson), skinId);
        }
        catch (Exception ex) when (ex is ManifestException or FormatException)
        {
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"These colours cannot be previewed: {ex.Message}");
        }
        var own = sex == "female" ? skin.Female : skin.Male;
        var based = BaseTextures(skin, sex, species);
        var diffuse = Load(mod, own, based, "diffuse", size, index, null)
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound,
                $"There is no {sex} diffuse to preview '{skin.Name}' with: add the skin's diffuse PNG, or index the assets (Workspace → Index assets) so the base skin's can be read.");
        var fit = (diffuse.Width, diffuse.Height);
        var pattern = Load(mod, own, based, "pattern", size, index, fit);
        var extra = Load(mod, own, based, "extra", size, index, fit);
        var maps = new PreviewMaps(diffuse.Width, diffuse.Height, diffuse.Data, pattern?.Data, extra?.Data);
        var (set, tint) = Mods.ColorPreview.For(colors, variant);

        Directory.CreateDirectory(outDir);
        var stamp = Hash($"{mod.Id}|{skinId}|{colorsJson}|{variant}|{sex}|{seed}|{size}|{DateTime.UtcNow.Ticks}");
        var files = new List<string>();
        for (var i = 0; i < Math.Clamp(count, 1, 12); i++)
        {
            var pixels = Mods.ColorPreview.Render(maps, set, tint, new Random(seed * 7919 + i));
            var path = Path.Combine(outDir, $"{stamp}-{i}.png");
            using (var stream = File.Create(path))
                new ImageWriter().WritePng(pixels, maps.Width, maps.Height, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
            files.Add(path);
        }
        return files;
    }

    public string? Thumbnail(ModProject mod, string file, int size, string outDir)
    {
        var image = LoadModFile(mod, file, size);
        if (image is null) return null;
        Directory.CreateDirectory(outDir);
        var source = Path.GetFullPath(Path.Combine(mod.Dir, file));
        var info = new FileInfo(source);
        var path = Path.Combine(outDir, Hash($"{source}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{size}") + ".png");
        if (!File.Exists(path))
            using (var stream = File.Create(path))
                new ImageWriter().WritePng(image.Data, image.Width, image.Height, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        return path;
    }

    /// <summary>Nearest-neighbour resize (previews only).</summary>
    public static ImageResult Shrink(ImageResult image, int width, int height)
    {
        var data = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var sx = x * image.Width / width;
            var sy = y * image.Height / height;
            Array.Copy(image.Data, (sy * image.Width + sx) * 4, data, (y * width + x) * 4, 4);
        }
        return new ImageResult
        {
            Width = width, Height = height, Comp = StbImageSharp.ColorComponents.RedGreenBlueAlpha, SourceComp = StbImageSharp.ColorComponents.RedGreenBlueAlpha, Data = data,
        };
    }

    private static IReadOnlyDictionary<string, string>? BaseTextures(SkinEntry skin, string sex, IReadOnlyList<SpeciesSkins>? species)
    {
        var target = species?.FirstOrDefault(s => s.SpeciesId == skin.Species);
        var based = target is null ? null
            : int.TryParse(skin.Base, out var number) ? target.Skins.FirstOrDefault(s => s.Index == number)
            : target.Skins.FirstOrDefault(s => string.Equals(s.Name, skin.Base, StringComparison.OrdinalIgnoreCase));
        return sex == "female" ? based?.Female : based?.Male;
    }

    private ImageResult? Load(ModProject mod, IReadOnlyDictionary<string, string>? own, IReadOnlyDictionary<string, string>? based, string slot, int size,
        AssetIndex? index, (int Width, int Height)? fit) =>
        own is not null && own.TryGetValue(slot, out var file) ? LoadModFile(mod, file, size, fit)
        : based is not null && based.TryGetValue(slot, out var guid) && index is not null ? LoadGame(index, guid, size, fit)
        : null;

    private static ImageResult? LoadModFile(ModProject mod, string file, int size, (int Width, int Height)? fit = null)
    {
        var path = Path.GetFullPath(Path.Combine(mod.Dir, file));
        if (!ModPaths.IsInside(path, mod.Dir) || !File.Exists(path)) return null;
        var info = new FileInfo(path);
        var key = $"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{size}|{fit}";
        if (Maps.TryGetValue(key, out var cached)) return cached;
        try
        {
            using var stream = File.OpenRead(path);
            var image = Fit(ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha), size, fit);
            Maps[key] = image;
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private ImageResult? LoadGame(AssetIndex index, string guid, int size, (int Width, int Height)? fit)
    {
        var key = $"guid:{guid}|{size}|{fit}";
        if (Maps.TryGetValue(key, out var cached)) return cached;
        var record = index.Assets.FirstOrDefault(a => string.Equals(a.Guid, guid, StringComparison.OrdinalIgnoreCase));
        if (record is null || gamePixels(record) is not { } pixels) return null;
        var image = Fit(pixels, size, fit);
        Maps[key] = image;
        return image;
    }

    /// <summary>The diffuse keeps its shape with its longest side at <paramref name="size"/>; other maps take the diffuse's size.</summary>
    private static ImageResult Fit(ImageResult image, int size, (int Width, int Height)? fit)
    {
        if (fit is { } f) return Shrink(image, f.Width, f.Height);
        var scale = (double)size / Math.Max(image.Width, image.Height);
        return Shrink(image, Math.Max(1, (int)Math.Round(image.Width * scale)), Math.Max(1, (int)Math.Round(image.Height * scale)));
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
}
