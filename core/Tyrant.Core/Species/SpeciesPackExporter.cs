using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Jobs;
using Tyrant.Core.Models;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Species;

public sealed record SpeciesPackResult(string Directory, IReadOnlyList<ModelExportResult> Models, IReadOnlyList<TextureExportResult> Textures, string TargetsPath);

/// <summary>Everything one species uses, in &lt;workspace&gt;/assets/species/&lt;key&gt;: models, textures, targets.json.</summary>
public sealed class SpeciesPackExporter
{
    public SpeciesPackResult Export(GameInstall install, Workspace ws, AssetIndex index, SpeciesEntry species,
        IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var models = new ModelExporter();
        var textures = new TextureExporter();
        using var session = new AssetSession(install);

        // Read first, so a failure (e.g. bundle gone after a game update) leaves the previous pack intact.
        progress?.Report(new JobProgress(0, $"Reading {species.DisplayName}"));
        var prefab = models.ReadPrefab(session, species.Prefab);
        session.Release();

        // Key is ASCII letters/digits only, so this always stays inside assets/species.
        var dir = Path.Combine(ws.AssetsDir, "species", species.Key);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var modelResults = models.WriteModels(prefab, Path.Combine(dir, "models"));

        var list = SpeciesCatalog.TexturesFor(index, species);
        var textureResults = new List<TextureExportResult>();
        for (var i = 0; i < list.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new JobProgress(0.2 + 0.8 * i / Math.Max(list.Count, 1), $"Exporting {list[i].Name}"));
            textureResults.Add(textures.Export(session, list[i], TextureExporter.OutputPathFor(list[i], dir)));
        }

        var targetsPath = Path.Combine(dir, SpeciesTargets.FileName);
        SpeciesTargets.Save(SpeciesTargets.Build(species, prefab, modelResults, textureResults, dir, index.Fingerprint?.BuildGuid), targetsPath);
        progress?.Report(new JobProgress(1.0, "Done"));

        if (modelResults.Any(r => r.Success)) ws.StampOutput($"assets/species/{species.Key}", GameFingerprint.Compute(install));
        return new SpeciesPackResult(dir, modelResults, textureResults, targetsPath);
    }
}
