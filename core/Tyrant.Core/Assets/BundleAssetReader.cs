using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Jobs;
using Tyrant.Core.Models;
using Tyrant.Core.Species;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Assets;

/// <summary>Reads the game's Addressables bundles through AssetsTools.NET (one session per call).</summary>
public sealed class BundleAssetReader : IAssetReader
{
    public AssetInspection Inspect(GameInstall install, AssetRecord asset)
    {
        using var session = new AssetSession(install);
        var (file, baseField) = session.Open(asset);
        var info = file.file.GetAssetInfo(asset.PathId);
        var externals = file.file.Metadata.Externals.Select(e => e.PathName).ToList();
        return new AssetInspection(info?.ByteSize ?? 0, FieldJsonWriter.ToJson(baseField), AssetReferences.Collect(baseField), externals);
    }

    public TextureFacts WriteTexture(GameInstall install, AssetRecord texture, string pngPath)
    {
        using var session = new AssetSession(install);
        var (_, baseField) = session.Open(texture);
        var facts = TextureFacts.Read(baseField);
        var result = new TextureExporter().Export(session, texture, pngPath);
        if (!result.Success)
            throw new TyrantException(TyrantErrorCode.AssetUnreadable, $"'{texture.Name}' could not be decoded: {result.Error}");
        return facts;
    }

    public ModelFacts WriteModel(GameInstall install, AssetRecord asset, string outputDir)
    {
        PrefabModel model;
        using (var session = new AssetSession(install))
        {
            model = asset.Type switch
            {
                "GameObject" => new ModelExporter().ReadPrefab(session, asset),
                "Mesh" => StandaloneMesh.ToPrefab(DecodeMesh(session, asset)),
                _ => throw new TyrantException(TyrantErrorCode.AssetUnreadable,
                    $"'{asset.Name}' is a {asset.Type}; only meshes and prefabs can be shown in 3D."),
            };
        }
        if (Directory.Exists(outputDir)) Directory.Delete(outputDir, recursive: true);
        var results = new ModelExporter().WriteModels(model, outputDir);
        var converted = results.Where(r => r.Success && r.Renderer is not null).ToList();
        if (converted.Count == 0)
            throw new TyrantException(TyrantErrorCode.AssetUnreadable,
                $"'{asset.Name}' has no mesh that could be converted. {string.Join(" ", results.Select(r => r.Error))}".TrimEnd());
        return new ModelFacts(
            converted.Select(r => r.OutputPath).ToList(),
            converted.Sum(r => r.Renderer!.Mesh.VertexCount),
            converted.Sum(r => r.Renderer!.Mesh.TriangleCount),
            converted.Any(r => r.Renderer!.IsSkinned),
            results.Where(r => !r.Success).Select(r => r.Error ?? r.Name).ToList());
    }

    public void WriteJson(GameInstall install, AssetRecord asset, string jsonPath)
    {
        using var session = new AssetSession(install);
        var (_, baseField) = session.Open(asset);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonPath))!);
        File.WriteAllText(jsonPath, FieldJsonWriter.ToJson(baseField));
    }

    public SpeciesPackResult WriteSpeciesPack(GameInstall install, Workspace ws, AssetIndex index, SpeciesEntry species,
        IProgress<JobProgress>? progress, CancellationToken ct) =>
        new SpeciesPackExporter().Export(install, ws, index, species, progress, ct);

    private static MeshData DecodeMesh(AssetSession session, AssetRecord asset)
    {
        var (_, field) = session.Open(asset);
        try
        {
            return MeshDecoder.Decode(field);
        }
        catch (InvalidDataException ex)
        {
            throw new TyrantException(TyrantErrorCode.AssetUnreadable, $"Mesh '{asset.Name}' could not be decoded: {ex.Message}", FixAction.None, ex);
        }
    }
}
