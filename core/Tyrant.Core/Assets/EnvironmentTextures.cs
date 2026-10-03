using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using StbImageWriteSharp;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;

namespace Tyrant.Core.Assets;

public enum EnvironmentKind
{
    Ground,
    Sky,
}

/// <summary>A game texture for the 3D preview's surroundings — a terrain layer or a sky cubemap — found by name in a loose .assets file.</summary>
public sealed record EnvironmentPreset(string Id, string Label, EnvironmentKind Kind, string File, string ObjectName);

public static class EnvironmentPresets
{
    private const string Terrain = "sharedassets3.assets";

    public static readonly IReadOnlyList<EnvironmentPreset> All =
    [
        new("lush-grass", "Lush grass", EnvironmentKind.Ground, Terrain, "LushGrass_Diffuse"),
        new("dry-grass", "Dry grass", EnvironmentKind.Ground, Terrain, "DryGrass_Diffuse"),
        new("temperate-grass", "Temperate grass", EnvironmentKind.Ground, Terrain, "TempContinentGrass_Diffuse"),
        new("wetland-grass", "Wetland grass", EnvironmentKind.Ground, Terrain, "WetlandGrass_Diffuse"),
        new("scrubland-grass", "Scrubland grass", EnvironmentKind.Ground, Terrain, "ScrublandGrass_Diffuse"),
        new("dirt", "Dirt", EnvironmentKind.Ground, Terrain, "TemperateDirt_Diffuse"),
        new("desert", "Desert", EnvironmentKind.Ground, Terrain, "DesertDirt_Diffuse"),
        new("sand", "Sand", EnvironmentKind.Ground, Terrain, "GrasslandSand_Diffuse"),
        new("morning", "Morning", EnvironmentKind.Sky, Terrain, "Morn_Sunny"),
        new("noon", "Noon", EnvironmentKind.Sky, Terrain, "Noon_Sunny"),
        new("cloudy", "Cloudy", EnvironmentKind.Sky, Terrain, "Noon_PartlyCloudy"),
        new("stormy", "Stormy", EnvironmentKind.Sky, Terrain, "Noon_Stormy"),
        new("evening", "Evening", EnvironmentKind.Sky, Terrain, "Eve_Sunny"),
        new("night", "Night", EnvironmentKind.Sky, Terrain, "Night_PartlyCloudy"),
    ];

    public static EnvironmentPreset Find(string id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal))
        ?? throw new ArgumentException($"Unknown environment '{id}'. Known: {string.Join(", ", All.Select(p => p.Id))}.");
}

/// <summary>
/// Writes surroundings from the game's loose .assets files. Those carry no type trees, so the Texture2D layout is borrowed
/// from a texture in the bundles (same game build); a Cubemap starts with the same fields.
/// </summary>
public sealed class EnvironmentTextureWriter
{
    /// <returns>Ground: [ground.png]. Sky: sky_0..5.png in Unity's face order +X, -X, +Y, -Y, +Z, -Z.</returns>
    public IReadOnlyList<string> Write(GameInstall install, AssetIndex index, EnvironmentPreset preset, string dir)
    {
        var what = preset.Kind == EnvironmentKind.Sky ? "sky" : "ground";
        var path = Path.Combine(install.DataDir, preset.File);
        if (!File.Exists(path))
            throw new TyrantException(TyrantErrorCode.AssetNotFound, $"The game file '{preset.File}' is missing, so the '{preset.Label}' {what} cannot be shown.");
        using var session = new AssetSession(install);
        var template = BorrowTextureLayout(session, index);
        try
        {
            var loose = session.Manager.LoadAssetsFile(path, false);
            var type = preset.Kind == EnvironmentKind.Sky ? AssetClassID.Cubemap : AssetClassID.Texture2D;
            foreach (var info in loose.file.GetAssetsOfType(type))
            {
                var field = template.MakeValue(loose.file.Reader, info.GetAbsoluteByteOffset(loose.file));
                if (field["m_Name"].AsString != preset.ObjectName) continue;
                Directory.CreateDirectory(dir);
                return preset.Kind == EnvironmentKind.Sky ? WriteFaces(field, loose, dir) : [WriteGround(field, loose, dir)];
            }
        }
        catch (Exception ex) when (ex is not TyrantException and not OperationCanceledException)
        {
            throw new TyrantException(TyrantErrorCode.AssetUnreadable,
                $"The '{preset.Label}' {what} could not be read from '{preset.File}': {ex.Message}", FixAction.None, ex);
        }
        throw new TyrantException(TyrantErrorCode.AssetNotFound,
            $"'{preset.ObjectName}' is not in '{preset.File}' (game updated?), so the '{preset.Label}' {what} cannot be shown.");
    }

    private static AssetTypeTemplateField BorrowTextureLayout(AssetSession session, AssetIndex index)
    {
        var sample = index.Assets.FirstOrDefault(a => a.Type == "Texture2D")
            ?? throw new TyrantException(TyrantErrorCode.AssetIndexMissing,
                "The asset index has no textures to learn the game's texture layout from. Click Index assets on the Workspace tab.", FixAction.RefreshWorkspace);
        var (file, _) = session.Open(sample);
        var type = file.file.Metadata.FindTypeTreeTypeByID((int)AssetClassID.Texture2D)
            ?? throw new TyrantException(TyrantErrorCode.AssetUnreadable, $"'{sample.Bundle}' carries no texture layout to borrow.");
        var template = new AssetTypeTemplateField();
        template.FromTypeTree(type);
        return template;
    }

    private static string WriteGround(AssetTypeValueField field, AssetsFileInstance file, string dir)
    {
        var texture = TextureFile.ReadTextureFile(field);
        var raw = texture.FillPictureData(file);
        var path = Path.Combine(dir, "ground.png");
        using var stream = File.Create(path);
        if (raw is null || raw.Length == 0 || !texture.DecodeTextureImage(raw, stream, ImageExportType.Png, 100))
            throw new InvalidDataException($"texture format {(TextureFormat)texture.m_TextureFormat} could not be decoded");
        return path;
    }

    /// <summary>A cubemap stores its six faces one after another (each with its mips), top row first — unlike 2D textures.</summary>
    private static IReadOnlyList<string> WriteFaces(AssetTypeValueField field, AssetsFileInstance file, string dir)
    {
        var texture = TextureFile.ReadTextureFile(field);
        var raw = texture.FillPictureData(file);
        var faces = field["m_ImageCount"].AsInt;
        if (raw is null || faces != 6 || raw.Length % 6 != 0)
            throw new InvalidDataException($"expected six cube faces, found {faces}");
        var size = raw.Length / 6;
        var paths = new List<string>();
        for (var f = 0; f < 6; f++)
        {
            var pixels = texture.DecodeTextureRaw(raw.AsSpan(f * size, size).ToArray(), false)
                ?? throw new InvalidDataException($"texture format {(TextureFormat)texture.m_TextureFormat} could not be decoded");
            var path = Path.Combine(dir, $"sky_{f}.png");
            using (var stream = File.Create(path))
                new ImageWriter().WritePng(pixels, texture.m_Width, texture.m_Height, ColorComponents.RedGreenBlueAlpha, stream);
            paths.Add(path);
        }
        return paths;
    }
}
