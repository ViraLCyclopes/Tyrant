using System.Text.Json;
using PK.Core.Assets;
using PK.Core.Models;

namespace PK.Core.Species;

public sealed record AssetTarget(string Ref, string? ContainerPath, string? Guid);

public sealed record ModelTarget(string File, string Mesh, string Renderer, bool Skinned, int VertexCount, int TriangleCount,
    List<string> BlendShapes, List<string> Bones);

/// <summary>A node of the prefab hierarchy; Parent is the parent's Path (names can repeat, paths cannot).</summary>
public sealed record BoneTarget(string Name, string Path, string? Parent);

public sealed record TextureTarget(string File, string Name, string? ContainerPath, string? Guid, string Ref);

/// <summary>targets.json: the IDs and bone structure replacement mods for this species need.</summary>
public sealed record TargetsFile(int SchemaVersion, string Species, string Key, bool Vivarium, string? GameBuild,
    AssetTarget Prefab, List<ModelTarget> Models, List<BoneTarget> Skeleton, List<TextureTarget> Textures);

public static class SpeciesTargets
{
    public const string FileName = "targets.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static TargetsFile Build(SpeciesEntry species, PrefabModel prefab, IEnumerable<ModelExportResult> models,
        IEnumerable<TextureExportResult> textures, string packDir, string? gameBuild) => new(
        SchemaVersion: 1,
        Species: species.DisplayName,
        Key: species.Key,
        Vivarium: species.Vivarium,
        GameBuild: gameBuild,
        Prefab: new AssetTarget(species.Prefab.Ref, species.Prefab.ContainerPath, species.Prefab.Guid),
        Models: models.Where(m => m.Success && m.Renderer is not null).Select(m => new ModelTarget(
            Relative(packDir, m.OutputPath), m.Renderer!.Mesh.Name, m.Renderer.Name, m.Renderer.IsSkinned,
            m.Renderer.Mesh.VertexCount, m.Renderer.Mesh.TriangleCount,
            m.Renderer.Mesh.BlendShapes.Select(b => b.Name).ToList(), m.Renderer.Bones.Select(b => b.Name).ToList())).ToList(),
        Skeleton: prefab.Root.DepthFirst().Select(n => new BoneTarget(n.Name, n.Path, n.Parent?.Path)).ToList(),
        Textures: textures.Where(t => t.Success).Select(t => new TextureTarget(
            Relative(packDir, t.OutputPath), t.Asset.Name, t.Asset.ContainerPath, t.Asset.Guid, t.Asset.Ref)).ToList());

    public static void Save(TargetsFile file, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(file, Json));
    }

    public static TargetsFile Load(string path) =>
        JsonSerializer.Deserialize<TargetsFile>(File.ReadAllText(path), Json) ?? throw new InvalidDataException($"{path} is empty.");

    private static string Relative(string dir, string path) => System.IO.Path.GetRelativePath(dir, path).Replace('\\', '/');
}
