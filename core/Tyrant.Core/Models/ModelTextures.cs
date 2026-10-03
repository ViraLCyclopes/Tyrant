using Tyrant.Core.Assets;

namespace Tyrant.Core.Models;

/// <summary>
/// The textures a prefab's materials use, decoded once into &lt;outputDir&gt;/textures and handed to the glTF writer as
/// linked files, so every .glb (LOD) in the folder shares them. A texture that fails leaves its material plain.
/// </summary>
public sealed class ModelTextures
{
    private readonly AssetSession _session;
    private readonly AssetIndex _index;
    private readonly string _bundle;
    private readonly string _dir;
    private readonly Dictionary<string, string?> _pngs = new(StringComparer.Ordinal); // texture ref → PNG, null when it failed
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<RendererModel, IReadOnlyList<GltfMaterial>> _byRenderer = new(ReferenceEqualityComparer.Instance);
    private readonly List<ResolvedMaterial> _materials = [];
    private readonly List<string> _failures = [];

    private ModelTextures(AssetSession session, AssetIndex index, string bundle, string dir)
    {
        _session = session;
        _index = index;
        _bundle = bundle;
        _dir = dir;
    }

    /// <summary>The prefab's named materials with the textures found for them.</summary>
    public IReadOnlyList<ResolvedMaterial> Materials => _materials;

    /// <summary>"&lt;texture&gt;: &lt;why&gt;" for every texture that was found but could not be decoded.</summary>
    public IReadOnlyList<string> Failures => _failures;

    /// <summary>What a person exporting the model should know: an index too old to follow textures, and textures that failed.</summary>
    public IReadOnlyList<string> Notes { get; private set; } = [];

    /// <summary>The note for textures kept in other bundles when the index predates archive maps (Plans 3–6).</summary>
    public const string NewerIndexNote =
        "Textures kept in other bundles need a newer asset index: click Index assets on the Workspace tab (or run 'tyrant assets index') and export again.";

    /// <param name="bundle">The bundle the prefab was read from; its materials' own-file textures live there.</param>
    public static ModelTextures Write(AssetSession session, AssetIndex index, string bundle, PrefabModel model, string outputDir)
    {
        var textures = new ModelTextures(session, index, bundle, Path.Combine(outputDir, GltfModelWriter.TexturesFolder));
        foreach (var renderer in model.Renderers)
            textures._byRenderer[renderer] = renderer.Materials.Select(m => textures.Prepare(renderer, m)).ToList();
        var outside = model.Renderers.SelectMany(r => r.Materials).SelectMany(m => m.Textures).Any(t => t.Archive is not null);
        textures.Notes = [
            .. index.Archives.Count == 0 && outside ? [NewerIndexNote] : Array.Empty<string>(),
            .. textures._failures.Select(f => $"Texture {f} (its material is plain)"),
        ];
        return textures;
    }

    /// <summary>The renderer's glTF materials, one per sub-mesh; null for a renderer that is not part of this model.</summary>
    public IReadOnlyList<GltfMaterial>? For(RendererModel renderer) => _byRenderer.GetValueOrDefault(renderer);

    private GltfMaterial Prepare(RendererModel renderer, MaterialModel material)
    {
        var resolved = MaterialResolver.Resolve(_index, _bundle, material);
        if (material.Name.Length > 0 && !_materials.Any(m => m.Name == resolved.Name && m.BaseColor == resolved.BaseColor && m.Normal == resolved.Normal))
            _materials.Add(resolved);
        var name = material.Name.Length > 0 ? material.Name : renderer.Mesh.Name.Length > 0 ? renderer.Mesh.Name : renderer.Name;
        return new GltfMaterial(name, Png(resolved.BaseColor), Png(resolved.Normal));
    }

    private string? Png(AssetRecord? texture)
    {
        if (texture is null) return null;
        if (_pngs.TryGetValue(texture.Ref, out var known)) return known;
        var name = TextureExporter.Sanitize(texture.Name.Length > 0 ? texture.Name : $"texture_{texture.PathId}");
        var path = Path.Combine(_dir, name + ".png");
        if (!_files.Add(path)) _files.Add(path = Path.Combine(_dir, $"{name}_{texture.PathId}.png")); // two textures share a name
        var result = new TextureExporter().Export(_session, texture, path); // rebuilds packed normal maps; releases the session
        if (!result.Success) _failures.Add($"{texture.Name}: {result.Error}");
        return _pngs[texture.Ref] = result.Success ? path : null;
    }
}
