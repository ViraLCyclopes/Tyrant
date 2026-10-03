using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Species;
using Tyrant.Rpc.Jobs;
using Tyrant.Rpc.Protocol;

namespace Tyrant.Rpc.Assets;

/// <summary>The parts of assets.* that read bundles: previews, exports and species packs.</summary>
public sealed partial class AssetsMethods
{
    private readonly PreviewCache _previews = new();

    [RpcMethod("assets.preview")]
    public AssetPreview Preview(AssetRefParams p)
    {
        var (ws, install, index) = Open();
        var asset = index.Resolve(p.Ref);
        if (asset.Type is not ("Texture2D" or "Mesh" or "GameObject"))
            return new AssetPreview(PreviewKind.None, [], Message: $"{asset.Type} assets have no picture or 3D preview; their fields are shown below.");
        // A prefab previewed from an index without archives lacks outside textures; keyed apart, so a re-index remakes it.
        var key = asset.Type == "GameObject" && index.Archives.Count == 0 ? $"{asset.Ref}|no-archives" : asset.Ref;
        return _previews.GetOrCreate(ws, GameFingerprint.Compute(install), key, asset.Name, dir =>
            asset.Type == "Texture2D" ? TexturePreview(install, asset, dir) : ModelPreview(install, index, asset, dir));
    }

    private AssetPreview TexturePreview(GameInstall install, AssetRecord asset, string dir)
    {
        var png = Path.Combine(dir, "texture.png");
        var facts = Reader.WriteTexture(install, asset, png);
        return new AssetPreview(PreviewKind.Texture, [png], facts.Width, facts.Height, facts.Format, facts.MipCount,
            Message: facts.RebuiltNormal ? "Normal map rebuilt from Unity's packed format (X was in alpha, Y in green); exports are rebuilt the same way." : null);
    }

    private AssetPreview ModelPreview(GameInstall install, AssetIndex index, AssetRecord asset, string dir)
    {
        var model = Reader.WriteModel(install, asset, Path.Combine(dir, "model"), index);
        var shown = PreviewParts.Pick(model.Parts);
        var notes = new List<string>();
        if (shown.Count < model.Parts.Count)
            notes.Add($"Showing the most detailed level of detail ({shown.Count} of {model.Parts.Count} parts); exports include every LOD.");
        if (model.Failures.Count > 0)
            notes.Add($"{model.Failures.Count} part(s) could not be converted. {string.Join(" ", model.Failures.Take(3))}");
        if (asset.Type == "GameObject" && index.Archives.Count == 0)
            notes.Add("Textures kept in other bundles need a newer asset index: click Index assets on the Home tab, then load the preview again.");
        if (model.TextureFailures.Count > 0)
            notes.Add($"{model.TextureFailures.Count} texture(s) could not be decoded, so their materials are plain. {string.Join(" ", model.TextureFailures.Take(3))}");

        IReadOnlyList<AssetRecord> skins = asset.Type == "GameObject" ? PreviewSkins.Candidates(index, asset) : [];
        var materials = model.Materials
            .Select(m => new PreviewMaterial(m.Name, m.BaseColor?.Name, m.Normal?.Name, m.BaseColor is not null && skins.Contains(m.BaseColor)))
            .ToList();
        var worn = model.Materials.Select(m => m.BaseColor?.Ref).OfType<string>().ToHashSet(StringComparer.Ordinal);
        return new AssetPreview(PreviewKind.Model, shown.Select(p => p.File).ToList(), Vertices: shown.Sum(p => p.Vertices),
            Triangles: shown.Sum(p => p.Triangles), Skinned: shown.Any(p => p.Skinned), Message: notes.Count == 0 ? null : string.Join(" ", notes),
            Materials: materials.Count == 0 ? null : materials,
            Skins: materials.Any(m => m.Skinnable) ? skins.Select(s => new PreviewSkin(s.Ref, s.Name, worn.Contains(s.Ref))).ToList() : null);
    }

    [RpcMethod("assets.export", JobResult = typeof(AssetExportRunResult))]
    public JobStarted Export(AssetExportParams p)
    {
        if (p.Refs.Count == 0) throw new ArgumentException("Pick at least one asset to export.");
        var (ws, install, index) = Open();
        var assets = p.Refs.Distinct(StringComparer.Ordinal).Select(r => index.Resolve(r)).ToList(); // an unknown ref fails before the job starts
        return _jobs.Start("Export assets", (progress, ct) =>
        {
            var (report, reportPath) = new AssetExport(Reader, index).Run(install, ws, assets, progress, ct);
            var failed = report.Items.Where(i => !i.Success).ToList();
            return new AssetExportRunResult(report.Items.Count - failed.Count, failed.Count, reportPath,
                failed.Take(20).Select(i => new AssetExportFailure(i.Name, i.Type, i.Error ?? "")).ToList(),
                report.Items.SelectMany(i => i.Notes ?? []).Distinct().Take(10).ToList());
        });
    }

    [RpcMethod("species.pack", JobResult = typeof(SpeciesPackRunResult))]
    public JobStarted Pack(SpeciesPackParams p)
    {
        var (ws, install, index) = Open();
        var species = SpeciesCatalog.Find(SpeciesCatalog.FromIndex(index), p.Key);
        return _jobs.Start($"Species pack: {species.DisplayName}", (progress, ct) =>
        {
            var result = Reader.WriteSpeciesPack(install, ws, index, species, progress, ct);
            return new SpeciesPackRunResult(result.Directory, result.Models.Count(m => m.Success), result.Textures.Count(t => t.Success),
                result.Models.Count(m => !m.Success) + result.Textures.Count(t => !t.Success), result.TargetsPath, result.Notes);
        });
    }
}
