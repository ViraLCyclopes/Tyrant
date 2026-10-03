using AssetsTools.NET.Texture;
using PK.Core.Install;
using PK.Core.Jobs;
using PK.Core.Workspaces;

namespace PK.Core.Assets;

/// <summary>Result of one texture export; OutputPath is the intended file even when the export failed.</summary>
public sealed record TextureExportResult(AssetRecord Asset, bool Success, string OutputPath, string? Error);

/// <summary>Exports Texture2D objects to PNG under &lt;workspace&gt;/assets/textures.</summary>
public sealed class TextureExporter
{
    public const string OutputName = "assets/textures";

    /// <summary>
    /// Mirrors the container path (minus "Assets/") under assets/textures; never escapes that folder.
    /// Non-PNG sources keep their extension ("T_x.tga" → "T_x.tga.png") so names never depend on what else is exported.
    /// </summary>
    public static string OutputPathFor(AssetRecord texture, string assetsDir)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(assetsDir, "textures")));
        var relative = texture.ContainerPath is { Length: > 0 } container
            ? (container.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ? container["Assets/".Length..] : container)
            : $"_unnamed/{Path.GetFileNameWithoutExtension(texture.Bundle)}/{(texture.Name.Length > 0 ? texture.Name : "texture")}_{texture.PathId}";

        var segments = relative.Split('/', '\\').Where(s => s is not ("" or "." or "..")).Select(Sanitize).ToList();
        if (segments.Count == 0) segments.Add($"texture_{texture.PathId}");
        if (!segments[^1].EndsWith(".png", StringComparison.OrdinalIgnoreCase)) segments[^1] += ".png";
        var full = Path.GetFullPath(Path.Combine([root, .. segments]));
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? full
            : Path.Combine(root, $"texture_{texture.PathId}.png");
    }

    public TextureExportResult Export(AssetSession session, AssetRecord texture, string outputPath)
    {
        if (!string.Equals(texture.Type, "Texture2D", StringComparison.Ordinal))
            return new TextureExportResult(texture, false, outputPath, $"'{texture.Ref}' is a {texture.Type}, not a Texture2D.");

        var tmp = outputPath + ".tmp";
        try
        {
            var (file, baseField) = session.Open(texture);
            var tex = TextureFile.ReadTextureFile(baseField);
            var raw = tex.FillPictureData(file);
            if (raw is null || raw.Length == 0)
                return new TextureExportResult(texture, false, outputPath, "The texture has no image data.");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            bool decoded;
            using (var stream = File.Create(tmp))
                decoded = tex.DecodeTextureImage(raw, stream, ImageExportType.Png, 100);
            if (!decoded)
            {
                File.Delete(tmp);
                return new TextureExportResult(texture, false, outputPath, $"Texture format {(TextureFormat)tex.m_TextureFormat} is not supported.");
            }
            File.Move(tmp, outputPath, overwrite: true);
            return new TextureExportResult(texture, true, outputPath, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            try { File.Delete(tmp); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return new TextureExportResult(texture, false, outputPath, ex.Message);
        }
        finally
        {
            session.Release();
        }
    }

    public IReadOnlyList<TextureExportResult> ExportMany(GameInstall install, Workspace ws, IReadOnlyList<AssetRecord> textures,
        IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var results = new List<TextureExportResult>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var session = new AssetSession(install);
        for (var i = 0; i < textures.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new JobProgress((double)i / textures.Count, $"Exporting {textures[i].Name}"));
            var path = OutputPathFor(textures[i], ws.AssetsDir);
            if (!used.Add(path))
            {
                var stem = path[..^".png".Length] + $"_{textures[i].PathId}";
                path = stem + ".png";
                for (var n = 2; !used.Add(path); n++) path = $"{stem}_{n}.png";
            }
            results.Add(Export(session, textures[i], path));
        }
        progress?.Report(new JobProgress(1.0, "Done"));
        if (results.Any(r => r.Success)) ws.StampOutput(OutputName, GameFingerprint.Compute(install));
        return results;
    }

    internal static string Sanitize(string segment)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(segment.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).TrimEnd('.', ' ');
        return cleaned.Length > 0 ? cleaned : "_";
    }
}
