using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Jobs;
using Tyrant.Core.Models;
using Tyrant.Core.Species;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Tests;

/// <summary>An IAssetReader that writes tiny placeholder files instead of decoding bundles (no game content).</summary>
public sealed class FakeAssetReader : IAssetReader
{
    private int _textures, _models, _json, _inspections, _environments;

    public HashSet<string> FailFor { get; } = new(StringComparer.Ordinal);

    /// <summary>The exception a FailFor asset throws (default: an AssetUnreadable TyrantException).</summary>
    public Func<AssetRecord, Exception>? FailWith { get; set; }
    public AssetInspection Inspection { get; set; } = new(128, """{"m_Name":"fake"}""", [], []);
    public SpeciesPackResult? SpeciesPack { get; set; }

    /// <summary>Names of the parts WriteModel writes (150 vertices and 50 triangles each).</summary>
    public string[] ModelParts { get; set; } = ["Body", "Eyes"];
    /// <summary>What WriteModel reports as the model's materials when it is given an index.</summary>
    public IReadOnlyList<ResolvedMaterial> ModelMaterials { get; set; } = [];
    public IReadOnlyList<string> ModelTextureFailures { get; set; } = [];
    public IReadOnlyList<string> ModelNotes { get; set; } = [];
    public AssetIndex? LastModelIndex { get; private set; }

    /// <summary>Texture PNGs WriteModel writes into outputDir/textures (as ModelTextures does).</summary>
    public string[] ModelTextureFiles { get; set; } = [];

    private int _batches, _callsInBatch;
    private bool _inBatch;
    public int Batches => _batches;
    public int CallsInBatch => _callsInBatch;

    public IDisposable Batch(GameInstall install)
    {
        Interlocked.Increment(ref _batches);
        _inBatch = true;
        return new End(() => _inBatch = false);
    }

    private sealed class End(Action end) : IDisposable
    {
        public void Dispose() => end();
    }

    private void Count()
    {
        if (_inBatch) Interlocked.Increment(ref _callsInBatch);
    }
    public int Textures => _textures;
    public int Models => _models;
    public int Json => _json;
    public int Inspections => _inspections;
    public int Environments => _environments;

    public AssetInspection Inspect(GameInstall install, AssetRecord asset)
    {
        Interlocked.Increment(ref _inspections);
        Fail(asset);
        return Inspection;
    }

    public TextureFacts WriteTexture(GameInstall install, AssetRecord texture, string pngPath)
    {
        Interlocked.Increment(ref _textures);
        Count();
        Fail(texture);
        Write(pngPath, "png");
        return new TextureFacts(64, 32, "DXT5", 7, NormalMap.IsCandidate(texture.Name));
    }

    public ModelFacts WriteModel(GameInstall install, AssetRecord asset, string outputDir, AssetIndex? index = null)
    {
        Interlocked.Increment(ref _models);
        Count();
        LastModelIndex = index;
        Fail(asset);
        if (Directory.Exists(outputDir)) Directory.Delete(outputDir, recursive: true);
        var parts = ModelParts.Select(name => new ModelPart(Path.Combine(outputDir, name + ".glb"), name, 150, 50, asset.Type == "GameObject")).ToList();
        foreach (var part in parts) Write(part.File, "glb");
        foreach (var texture in ModelTextureFiles) Write(Path.Combine(outputDir, "textures", texture), "png");
        return new ModelFacts(parts, []) { Materials = index is null ? [] : ModelMaterials, TextureFailures = ModelTextureFailures, Notes = ModelNotes };
    }

    public void WriteJson(GameInstall install, AssetRecord asset, string jsonPath)
    {
        Interlocked.Increment(ref _json);
        Count();
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

    public IReadOnlyList<string> WriteEnvironment(GameInstall install, AssetIndex index, EnvironmentPreset preset, string dir)
    {
        Interlocked.Increment(ref _environments);
        List<string> files = preset.Kind == EnvironmentKind.Sky
            ? Enumerable.Range(0, 6).Select(f => Path.Combine(dir, $"sky_{f}.png")).ToList()
            : [Path.Combine(dir, "ground.png")];
        foreach (var file in files) Write(file, "png");
        return files;
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
