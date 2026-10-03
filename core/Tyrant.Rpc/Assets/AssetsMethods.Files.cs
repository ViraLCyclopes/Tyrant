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
        return _previews.GetOrCreate(ws, GameFingerprint.Compute(install).BuildGuid, asset, dir =>
        {
            if (asset.Type == "Texture2D")
            {
                var png = Path.Combine(dir, "texture.png");
                var facts = Reader.WriteTexture(install, asset, png);
                return new AssetPreview(PreviewKind.Texture, [png], facts.Width, facts.Height, facts.Format, facts.MipCount);
            }
            var model = Reader.WriteModel(install, asset, Path.Combine(dir, "model"));
            var message = model.Failures.Count == 0 ? null : $"{model.Failures.Count} part(s) could not be converted. {string.Join(" ", model.Failures.Take(3))}";
            return new AssetPreview(PreviewKind.Model, model.Files, Vertices: model.Vertices, Triangles: model.Triangles, Skinned: model.Skinned, Message: message);
        });
    }

    [RpcMethod("assets.export", JobResult = typeof(AssetExportRunResult))]
    public JobStarted Export(AssetExportParams p)
    {
        if (p.Refs.Count == 0) throw new ArgumentException("Pick at least one asset to export.");
        var (ws, install, index) = Open();
        var assets = p.Refs.Distinct(StringComparer.Ordinal).Select(r => index.Resolve(r)).ToList(); // an unknown ref fails before the job starts
        return _jobs.Start("Export assets", (progress, ct) =>
        {
            var (report, reportPath) = new AssetExport(Reader).Run(install, ws, assets, progress, ct);
            var failed = report.Items.Where(i => !i.Success).ToList();
            return new AssetExportRunResult(report.Items.Count - failed.Count, failed.Count, reportPath,
                failed.Take(20).Select(i => new AssetExportFailure(i.Name, i.Type, i.Error ?? "")).ToList());
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
                result.Models.Count(m => !m.Success) + result.Textures.Count(t => !t.Success), result.TargetsPath);
        });
    }
}
