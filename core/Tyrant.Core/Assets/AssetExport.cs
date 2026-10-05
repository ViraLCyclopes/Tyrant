using System.Text.Json;
using Tyrant.Core.Blender;
using Tyrant.Core.Data;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Jobs;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Assets;

public sealed record AssetExportItem(string Ref, string Type, string Name, string? ContainerPath, string? Guid, bool Success,
    IReadOnlyList<string> Outputs, string? Error, IReadOnlyList<string>? Notes = null);

public sealed record AssetExportReport(DateTimeOffset CreatedUtc, string BuildGuid, IReadOnlyList<AssetExportItem> Items);

/// <summary>
/// Exports chosen assets by type — Texture2D → PNG, Mesh/GameObject → .glb, anything else → JSON — and writes a report
/// with every asset's keys. One failure never stops the rest (spec §5). Models are written with their textures when an index is given,
/// and as FBX (or both) when a format and a converter are given.
/// </summary>
public sealed class AssetExport(IAssetReader reader, AssetIndex? index = null, ModelFormat format = ModelFormat.Glb, IModelConverter? converter = null)
{
    private static readonly JsonSerializerOptions ReportJson = new(DataStore.ReadableJson) { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public (AssetExportReport Report, string ReportPath) Run(GameInstall install, Workspace ws, IReadOnlyList<AssetRecord> assets,
        IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<AssetExportItem>();
        using (reader.Batch(install))
            for (var i = 0; i < assets.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new JobProgress((double)i / assets.Count, $"Exporting {assets[i].Name}"));
                items.Add(ExportOne(install, ws, assets[i], used));
            }
        if (format != ModelFormat.Glb && converter is not null)
        {
            ConvertModels(items, progress, ct);
        }
        var report = new AssetExportReport(DateTimeOffset.UtcNow, GameFingerprint.Compute(install).BuildGuid, items);
        var reportPath = ReportPath(ws);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, ReportJson));
        progress?.Report(new JobProgress(1, "Done"));
        return (report, reportPath);
    }

    /// <summary>One Blender run for every model of the export: each model item's glb outputs become FBX (or both).</summary>
    private void ConvertModels(List<AssetExportItem> items, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var models = Enumerable.Range(0, items.Count).Where(k => items[k].Success && items[k].Type is "Mesh" or "GameObject").ToList();
        progress?.Report(new JobProgress(0, "Converting models to FBX"));
        Action<double>? converted = progress is null ? null : p => progress.Report(new JobProgress(p, "Converting models to FBX"));
        var (files, notes) = ModelFormats.Apply(converter!, [.. models.SelectMany(k => items[k].Outputs)], format, progress: converted, ct: ct);
        var produced = files.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var k in models)
        {
            var outputs = new List<string>();
            foreach (var output in items[k].Outputs)
            {
                if (produced.Contains(output)) outputs.Add(output); // not a glb, kept (both), or a glb that failed
                var fbx = Path.ChangeExtension(output, ".fbx");
                if (output.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) && produced.Contains(fbx)) outputs.Add(fbx);
            }
            var mine = notes.Where(n => items[k].Outputs.Any(o => n.StartsWith(Path.GetFileName(o) + " ", StringComparison.OrdinalIgnoreCase))).ToList();
            List<string> all = [.. items[k].Notes ?? [], .. mine];
            items[k] = items[k] with { Outputs = outputs, Notes = all.Count == 0 ? null : all };
        }
    }

    private AssetExportItem ExportOne(GameInstall install, Workspace ws, AssetRecord asset, HashSet<string> used)
    {
        try
        {
            IReadOnlyList<string> outputs;
            IReadOnlyList<string>? notes = null;
            switch (asset.Type)
            {
                case "Texture2D":
                    var png = Unique(TextureExporter.OutputPathFor(asset, ws.AssetsDir), asset, used);
                    reader.WriteTexture(install, asset, png);
                    outputs = [png];
                    break;
                case "Mesh":
                case "GameObject":
                    var dir = Unique(Path.Combine(ws.AssetsDir, "models", SafeName(asset)), asset, used);
                    var model = reader.WriteModel(install, asset, dir, index);
                    outputs = [.. model.Files, .. TexturePngs(dir)];
                    IReadOnlyList<string> modelNotes = asset.Type == "Mesh" ? [.. model.Notes, .. model.MaterialFailures, BareMeshNote] : [.. model.Notes, .. model.MaterialFailures];
                    notes = modelNotes.Count == 0 ? null : modelNotes;
                    break;
                default:
                    var json = Unique(Path.Combine(ws.AssetsDir, "json", TextureExporter.Sanitize(asset.Type), $"{SafeName(asset)}_{asset.PathId}.json"), asset, used);
                    reader.WriteJson(install, asset, json);
                    outputs = [json];
                    break;
            }
            return Item(asset, true, outputs, null, notes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) // spec §5: one item never stops the batch
        {
            return Item(asset, false, [], ex.Message);
        }
    }

    private static AssetExportItem Item(AssetRecord a, bool success, IReadOnlyList<string> outputs, string? error, IReadOnlyList<string>? notes = null) =>
        new(a.Ref, a.Type, a.Name, a.ContainerPath, a.Guid, success, outputs, error, notes);

    public const string BareMeshNote = "This Mesh is exported without its bones (skin); export its prefab (GameObject) to keep them.";

    /// <summary>The PNGs written beside a model's .glb files (outputDir/textures).</summary>
    private static IEnumerable<string> TexturePngs(string dir)
    {
        var textures = Path.Combine(dir, Models.GltfModelWriter.TexturesFolder);
        return Directory.Exists(textures) ? Directory.EnumerateFiles(textures, "*.png").Order(StringComparer.OrdinalIgnoreCase) : [];
    }

    /// <summary>At most 80 characters: long names keep their start and get a short hash, so paths stay under Windows' limit.</summary>
    internal static string Shorten(string name)
    {
        if (name.Length <= 80) return name;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name)))[..8].ToLowerInvariant();
        return $"{name[..70]}_{hash}";
    }

    /// <summary>A file or folder name from the asset name; never empty, never a path.</summary>
    private static string SafeName(AssetRecord asset)
    {
        var name = string.Join("_", asset.Name.Split('/', '\\').Where(s => s is not ("" or "." or "..")));
        return name.Length > 0 ? Shorten(TextureExporter.Sanitize(name)) : $"{asset.Type}_{asset.PathId}";
    }

    /// <summary>Appends the path id (then a counter) when two assets would share a path.</summary>
    private static string Unique(string path, AssetRecord asset, HashSet<string> used)
    {
        if (used.Add(path)) return path;
        var extension = Path.GetExtension(path);
        var stem = path[..^extension.Length] + $"_{asset.PathId}";
        var candidate = stem + extension;
        for (var n = 2; !used.Add(candidate); n++) candidate = $"{stem}_{n}{extension}";
        return candidate;
    }

    private static string ReportPath(Workspace ws)
    {
        var stem = Path.Combine(ws.Dir, "exports", $"asset-export-{DateTime.Now:yyyyMMdd-HHmmss}");
        var path = stem + ".json";
        for (var n = 2; File.Exists(path); n++) path = $"{stem}-{n}.json";
        return path;
    }
}
