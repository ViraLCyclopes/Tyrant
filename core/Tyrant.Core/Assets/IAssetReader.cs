using Tyrant.Core.Install;
using Tyrant.Core.Jobs;
using Tyrant.Core.Models;
using Tyrant.Core.Species;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Assets;

/// <summary>An object's raw facts: its size in the bundle, its fields as JSON, and what it references.</summary>
public sealed record AssetInspection(long ByteSize, string FieldsJson, IReadOnlyList<AssetReference> References, IReadOnlyList<string> ExternalFiles);

/// <summary>One converted mesh: its .glb file and counts.</summary>
public sealed record ModelPart(string File, string Name, int Vertices, int Triangles, bool Skinned);

/// <summary>The .glb files written for a mesh or prefab (one part per converted mesh), and what could not be converted.</summary>
public sealed record ModelFacts(IReadOnlyList<ModelPart> Parts, IReadOnlyList<string> Failures)
{
    public IReadOnlyList<string> Files => Parts.Select(p => p.File).ToList();
    public int Vertices => Parts.Sum(p => p.Vertices);
    public int Triangles => Parts.Sum(p => p.Triangles);
    public bool Skinned => Parts.Any(p => p.Skinned);

    /// <summary>The prefab's materials and the textures found for them (empty for a bare mesh or without an index).</summary>
    public IReadOnlyList<ResolvedMaterial> Materials { get; init; } = [];

    /// <summary>Textures that were found but could not be decoded; their materials are plain.</summary>
    public IReadOnlyList<string> TextureFailures { get; init; } = [];

    /// <summary>Notes for exports: an index too old to follow textures, and textures that failed (see ModelTextures.Notes).</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>Texture ref → PNG written into outputDir/textures.</summary>
    public IReadOnlyDictionary<string, string> TextureFiles { get; init; } = new Dictionary<string, string>();

    /// <summary>Materials that could not be read (their meshes are drawn plain).</summary>
    public IReadOnlyList<string> MaterialFailures { get; init; } = [];
}

/// <summary>Everything that reads game bundles. Production code uses <see cref="BundleAssetReader"/>; tests use a fake.</summary>
public interface IAssetReader
{
    /// <summary>Until disposed, calls for this install made in the same flow share one session (the class database loads once).</summary>
    IDisposable Batch(GameInstall install);

    AssetInspection Inspect(GameInstall install, AssetRecord asset);

    /// <summary>Decodes a Texture2D to a PNG at <paramref name="pngPath"/>.</summary>
    TextureFacts WriteTexture(GameInstall install, AssetRecord texture, string pngPath);

    /// <summary>
    /// Writes a Mesh or prefab GameObject as .glb files into <paramref name="outputDir"/> (replacing its contents). With an
    /// index, a prefab's textures are decoded into outputDir/textures and linked from the .glb files.
    /// </summary>
    ModelFacts WriteModel(GameInstall install, AssetRecord asset, string outputDir, AssetIndex? index = null);

    /// <summary>The prefab's hierarchy and renderers with their materials (a replacement model is fitted to it).</summary>
    PrefabModel ReadPrefabModel(GameInstall install, AssetRecord prefab);

    /// <summary>The prefab's materials (each renderer's, distinct by name), for the skin page's slot list.</summary>
    IReadOnlyList<MaterialModel> ReadMaterials(GameInstall install, AssetRecord prefab);

    /// <summary>Writes any object's fields as JSON.</summary>
    void WriteJson(GameInstall install, AssetRecord asset, string jsonPath);

    /// <summary>Writes a ground or sky texture from the game's loose files into <paramref name="dir"/>; see <see cref="EnvironmentTextureWriter"/>.</summary>
    IReadOnlyList<string> WriteEnvironment(GameInstall install, AssetIndex index, EnvironmentPreset preset, string dir);

    SpeciesPackResult WriteSpeciesPack(GameInstall install, Workspace ws, AssetIndex index, SpeciesEntry species,
        IProgress<JobProgress>? progress, CancellationToken ct);
}
