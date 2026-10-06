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

    public IReadOnlyList<string> ModelMaterialFailures { get; set; } = [];

    /// <summary>Writes a placeholder .glb per LOD (WriteReplacedModel), with ModelMaterials as the materials.</summary>
    public int ReplacedModelWrites { get; private set; }

    public ModelFacts WriteReplacedModel(GameInstall install, AssetRecord prefab, IReadOnlyList<Tyrant.Framework.Core.TMesh> lods, string outputDir, AssetIndex index,
        IReadOnlyDictionary<string, Tyrant.Framework.Core.RigOffset>? rig = null)
    {
        ReplacedModelWrites++;
        Fail(prefab);
        var parts = lods.Select((lod, i) => new ModelPart(Path.Combine(outputDir, $"lod{i}.glb"), $"LOD {i}", lod.VertexCount, lod.Indices.Length / 3, true)).ToList();
        foreach (var part in parts) Write(part.File, "glb");
        return new ModelFacts(parts, []) { Materials = ModelMaterials };
    }

    /// <summary>What ReadClips returns (whatever clips are asked for), and the clips asked for last.</summary>
    public IReadOnlyList<ClipChannels> ClipsToReturn { get; set; } = [];
    public IReadOnlyList<AssetRecord> LastClipsAsked { get; private set; } = [];

    public (IReadOnlyList<ClipChannels> Clips, IReadOnlyList<string> Failures) ReadClips(GameInstall install, IReadOnlyList<AssetRecord> clips)
    {
        LastClipsAsked = clips;
        return (ClipsToReturn, []);
    }

    /// <summary>Writes a real .glb (no textures) of PrefabModelToReturn's first LOD with the animations.</summary>
    public void WriteAnimatedModel(GameInstall install, AssetRecord prefab, string path, AssetIndex index, IReadOnlyList<Tyrant.Core.Animation.ClipAnimation> animations)
    {
        Fail(prefab);
        var model = PrefabModelToReturn ?? throw new InvalidOperationException("Set PrefabModelToReturn.");
        GltfModelWriter.WriteGlb(model, Tyrant.Core.ModelReplacements.ModelBuilder.GameRenderers(model).Take(1).ToList(), path, null, animations);
    }

    /// <summary>What ReadRawClips returns (whatever clips are asked for), and the clips asked for last.</summary>
    public IReadOnlyList<Tyrant.Core.Animation.RawClip> RawClipsToReturn { get; set; } = [];
    public IReadOnlyList<AssetRecord> LastRawClipsAsked { get; private set; } = [];

    public (IReadOnlyList<Tyrant.Core.Animation.RawClip> Clips, IReadOnlyList<string> Failures) ReadRawClips(GameInstall install, IReadOnlyList<AssetRecord> clips)
    {
        LastRawClipsAsked = clips;
        return (RawClipsToReturn, []);
    }

    /// <summary>What ReadPrefabModel returns (the game prefab a model is fitted to).</summary>
    public Tyrant.Core.Models.PrefabModel? PrefabModelToReturn { get; set; }

    public Tyrant.Core.Models.PrefabModel ReadPrefabModel(GameInstall install, AssetRecord prefab)
    {
        Fail(prefab);
        return PrefabModelToReturn ?? throw new InvalidOperationException("This test gave the fake reader no prefab model.");
    }

    /// <summary>What ReadMaterials returns; PrefabReads counts the calls.</summary>
    public IReadOnlyList<Tyrant.Core.Models.MaterialModel> PrefabMaterials { get; set; } = [];
    public int PrefabReads { get; private set; }

    public IReadOnlyList<Tyrant.Core.Models.MaterialModel> ReadMaterials(GameInstall install, AssetRecord prefab)
    {
        PrefabReads++;
        Fail(prefab);
        return PrefabMaterials;
    }

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
        if (RealPngs)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pngPath))!);
            using var stream = File.Create(pngPath);
            new StbImageWriteSharp.ImageWriter().WritePng(Enumerable.Repeat((byte)128, 4 * 4 * 4).ToArray(), 4, 4, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        }
        else
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
        var textureFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var texture in ModelTextureFiles)
        {
            var png = Path.Combine(outputDir, "textures", texture);
            Write(png, "png");
            if (index?.Assets.FirstOrDefault(a => a.Name == Path.GetFileNameWithoutExtension(texture)) is { } record) textureFiles[record.Ref] = png;
        }
        return new ModelFacts(parts, []) { Materials = index is null ? [] : ModelMaterials, TextureFailures = ModelTextureFailures, Notes = ModelNotes,
            TextureFiles = textureFiles, MaterialFailures = ModelMaterialFailures };
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

    /// <summary>Textures are written as real 4×4 grey PNGs (Blender tests paint them), not placeholder text.</summary>
    public bool RealPngs { get; init; }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, content);
    }
}
