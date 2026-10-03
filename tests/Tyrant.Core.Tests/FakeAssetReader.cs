using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Jobs;
using Tyrant.Core.Species;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Tests;

/// <summary>An IAssetReader that writes tiny placeholder files instead of decoding bundles (no game content).</summary>
public sealed class FakeAssetReader : IAssetReader
{
    private int _textures, _models, _json, _inspections;

    public HashSet<string> FailFor { get; } = new(StringComparer.Ordinal);

    /// <summary>The exception a FailFor asset throws (default: an AssetUnreadable TyrantException).</summary>
    public Func<AssetRecord, Exception>? FailWith { get; set; }
    public AssetInspection Inspection { get; set; } = new(128, """{"m_Name":"fake"}""", [], []);
    public SpeciesPackResult? SpeciesPack { get; set; }

    /// <summary>Names of the parts WriteModel writes (150 vertices and 50 triangles each).</summary>
    public string[] ModelParts { get; set; } = ["Body", "Eyes"];
    public int Textures => _textures;
    public int Models => _models;
    public int Json => _json;
    public int Inspections => _inspections;

    public AssetInspection Inspect(GameInstall install, AssetRecord asset)
    {
        Interlocked.Increment(ref _inspections);
        Fail(asset);
        return Inspection;
    }

    public TextureFacts WriteTexture(GameInstall install, AssetRecord texture, string pngPath)
    {
        Interlocked.Increment(ref _textures);
        Fail(texture);
        Write(pngPath, "png");
        return new TextureFacts(64, 32, "DXT5", 7);
    }

    public ModelFacts WriteModel(GameInstall install, AssetRecord asset, string outputDir)
    {
        Interlocked.Increment(ref _models);
        Fail(asset);
        if (Directory.Exists(outputDir)) Directory.Delete(outputDir, recursive: true);
        var parts = ModelParts.Select(name => new ModelPart(Path.Combine(outputDir, name + ".glb"), name, 150, 50, asset.Type == "GameObject")).ToList();
        foreach (var part in parts) Write(part.File, "glb");
        return new ModelFacts(parts, []);
    }

    public void WriteJson(GameInstall install, AssetRecord asset, string jsonPath)
    {
        Interlocked.Increment(ref _json);
        Fail(asset);
        Write(jsonPath, "{}");
    }

    public SpeciesPackResult WriteSpeciesPack(GameInstall install, Workspace ws, AssetIndex index, SpeciesEntry species,
        IProgress<JobProgress>? progress, CancellationToken ct)
    {
        Fail(species.Prefab);
        progress?.Report(new JobProgress(1, "Done"));
        return SpeciesPack ?? new SpeciesPackResult(Path.Combine(ws.AssetsDir, "species", species.Key), [], [], Path.Combine(ws.AssetsDir, "species", species.Key, "targets.json"));
    }

    private void Fail(AssetRecord asset)
    {
        if (FailFor.Contains(asset.Ref))
            throw FailWith?.Invoke(asset) ?? new TyrantException(TyrantErrorCode.AssetUnreadable, $"Bundle '{asset.Bundle}' could not be read (fake).", FixAction.RefreshWorkspace);
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, content);
    }
}
