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
    public const int MaxDisplayedArrayElements = 100;

    public AssetInspection Inspect(GameInstall install, AssetRecord asset)
    {
        using var session = new AssetSession(install);
        var (file, baseField) = session.Open(asset);
        var info = file.file.GetAssetInfo(asset.PathId);
        var externals = file.file.Metadata.Externals.Select(e => e.PathName).ToList();
        // Capped for display: some meshes carry tens of megabytes of blend-shape structs. Exports write the full tree.
        return new AssetInspection((info?.ByteSize ?? 0) + AssetSizes.StreamedBytes(baseField), FieldJsonWriter.ToJson(baseField, maxArrayElements: MaxDisplayedArrayElements),
            AssetReferences.Collect(baseField), externals);
    }

    public TextureFacts WriteTexture(GameInstall install, AssetRecord texture, string pngPath)
    {
        using var session = new AssetSession(install);
        var (_, baseField) = session.Open(texture);
        var facts = TextureFacts.Read(baseField);
        var result = new TextureExporter().Export(session, texture, pngPath);
        if (!result.Success)
            throw new TyrantException(TyrantErrorCode.AssetUnreadable, $"'{texture.Name}' could not be decoded: {result.Error}");
        return facts with { RebuiltNormal = result.RebuiltNormal };
    }

    public ModelFacts WriteModel(GameInstall install, AssetRecord asset, string outputDir)
    {
        PrefabModel model;
        using (var session = new AssetSession(install))
        {
            try
            {
                model = asset.Type switch
                {
                    "GameObject" => new ModelExporter().ReadPrefab(session, asset),
                    "Mesh" => StandaloneMesh.ToPrefab(MeshDecoder.Decode(session.Open(asset).BaseField)),
                    _ => throw new TyrantException(TyrantErrorCode.AssetUnreadable,
                        $"'{asset.Name}' is a {asset.Type}; only meshes and prefabs can be shown in 3D."),
                };
            }
            catch (Exception ex) when (ex is InvalidDataException or FormatException or NotSupportedException)
            {
                // e.g. vertex data streamed in a .resS file, or a vertex format the decoder does not read yet
                throw new TyrantException(TyrantErrorCode.AssetUnreadable, $"'{asset.Name}' could not be converted: {ex.Message}", FixAction.None, ex);
            }
        }
        if (Directory.Exists(outputDir)) Directory.Delete(outputDir, recursive: true);
        var results = new ModelExporter().WriteModels(model, outputDir);
        var converted = results.Where(r => r.Success && r.Renderer is not null).ToList();
        if (converted.Count == 0)
            throw new TyrantException(TyrantErrorCode.AssetUnreadable,
                $"'{asset.Name}' has no mesh that could be converted. {string.Join(" ", results.Select(r => r.Error))}".TrimEnd());
        return new ModelFacts(
            converted.Select(r => new ModelPart(r.OutputPath, r.Name, r.Renderer!.Mesh.VertexCount, r.Renderer.Mesh.TriangleCount, r.Renderer.IsSkinned)).ToList(),
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
}
