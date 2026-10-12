using System.Text.RegularExpressions;
using StbImageSharp;
using StbImageWriteSharp;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Textures;

/// <summary>A slim copy of a mod: the files it leaves out, the thumbnails it adds and its mod.json.</summary>
public sealed record SlimPlan(IReadOnlySet<string> Dropped, IReadOnlyDictionary<string, byte[]> Thumbnails, ModManifest Manifest);

/// <summary>
/// Install and PNG-less exports leave out the PNGs textures.tpack holds (spec: 2026-10-11-tyrant-slim-installs-design.md).
/// A file is left out only when nothing uncooked names it and the pack has it; a skin whose thumbnail the game would cut
/// from such a PNG gets that 128 px picture as thumbnail.auto.png instead.
/// </summary>
public static partial class SlimCopy
{
    public const int ThumbnailSize = 128;

    [GeneratedRegex(@"^thumbnail\.auto(-\d+)?\.png$", RegexOptions.IgnoreCase)]
    private static partial Regex AutoName();

    public static bool IsAutoThumbnail(string? path) => path is not null && AutoName().IsMatch(Path.GetFileName(path.Replace('\\', '/')));

    public static IReadOnlySet<string> Droppable(ModManifest m)
    {
        var uncooked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? file)
        {
            if (!string.IsNullOrWhiteSpace(file)) uncooked.Add(TexturePack.NormalSource(file));
        }
        Add(m.Assembly);
        foreach (var skin in m.Skins)
        {
            Add(skin.Thumbnail);
            Add(skin.Model);
            foreach (var portrait in skin.Portraits?.Values ?? Enumerable.Empty<string>()) Add(portrait);
        }
        foreach (var species in m.Species)
        {
            Add(species.Icon);
            Add(species.Swatch);
            Add(species.Prefab?.Bundle);
            foreach (var portrait in species.Portraits.Values) Add(portrait);
        }
        foreach (var model in m.Models) Add(model.File);
        foreach (var sound in m.Sounds)
            foreach (var file in sound.Files) Add(file);
        return ModTextureCooker.TextureFiles(m).Select(TexturePack.NormalSource).Where(f => !uncooked.Contains(f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static SlimPlan Plan(ModManifest manifest, string sourceDir, TexturePack pack)
    {
        var inPack = pack.Entries.Select(e => TexturePack.NormalSource(e.Source)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dropped = Droppable(manifest).Where(inPack.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var copy = ModManifest.Parse(manifest.ToJson());
        var thumbnails = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var skin in copy.Skins.Where(s => string.IsNullOrWhiteSpace(s.Thumbnail)))
        {
            var diffuse = Diffuse(skin.Male) ?? Diffuse(skin.Female); // the framework's order (SkinsModule.Thumbnail)
            if (diffuse is null || !dropped.Contains(TexturePack.NormalSource(diffuse))) continue;
            var png = ModTextureCooker.InsidePath(sourceDir, diffuse);
            if (png is null || !File.Exists(png)) continue;
            var name = FreeName(sourceDir, skin.Id, thumbnails);
            thumbnails[name] = ThumbnailPng(png);
            skin.Thumbnail = name;
        }
        return new SlimPlan(dropped, thumbnails, copy);
    }

    public static void Apply(SlimPlan plan, string dir)
    {
        foreach (var (name, png) in plan.Thumbnails)
        {
            var path = Path.Combine(dir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, png);
        }
        foreach (var file in plan.Dropped)
        {
            var path = ModTextureCooker.InsidePath(dir, file);
            if (path is null || !File.Exists(path)) continue;
            File.Delete(path);
            for (var folder = Path.GetDirectoryName(path); folder is not null && !PathsEqual(folder, dir) && !Directory.EnumerateFileSystemEntries(folder).Any();
                 folder = Path.GetDirectoryName(folder))
                Directory.Delete(folder);
        }
        File.WriteAllText(Path.Combine(dir, ModManifest.FileName), plan.Manifest.ToJson());
    }

    public static bool WithoutAutoThumbnails(ModManifest manifest)
    {
        var changed = false;
        foreach (var skin in manifest.Skins.Where(s => IsAutoThumbnail(s.Thumbnail)))
        {
            skin.Thumbnail = null;
            changed = true;
        }
        return changed;
    }

    private static string? Diffuse(Dictionary<string, string>? slots) => slots is not null && slots.TryGetValue("diffuse", out var file) ? file : null;

    private static string FreeName(string sourceDir, string skinId, Dictionary<string, byte[]> taken)
    {
        for (var n = 1; ; n++)
        {
            var name = $"skins/{skinId}/thumbnail.auto{(n == 1 ? "" : "-" + n)}.png";
            if (!taken.ContainsKey(name) && !File.Exists(Path.Combine(sourceDir, name))) return name;
        }
    }

    private static byte[] ThumbnailPng(string png)
    {
        ImageResult image;
        using (var stream = File.OpenRead(png)) image = ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        var thumb = Pixels.Thumbnail(image.Data, image.Width, image.Height, ThumbnailSize);
        using var output = new MemoryStream();
        new ImageWriter().WritePng(thumb, ThumbnailSize, ThumbnailSize, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, output);
        return output.ToArray();
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
}
