using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Models;

/// <summary>Outcome of writing one renderer; OutputPath is the intended file even on failure ("" when none).</summary>
public sealed record ModelExportResult(string Name, RendererModel? Renderer, bool Success, string OutputPath, string? Error);

/// <summary>Exports a prefab's renderers as .glb files.</summary>
public sealed class ModelExporter
{
    public const string OutputName = "assets/models";

    public PrefabModel ReadPrefab(AssetSession session, AssetRecord prefab)
    {
        if (!string.Equals(prefab.Type, "GameObject", StringComparison.Ordinal))
            throw new TyrantException(TyrantErrorCode.AssetNotFound, $"'{prefab.Ref}' is a {prefab.Type}, not a prefab GameObject.");
        var (file, _) = session.Open(prefab);
        try
        {
            return PrefabReader.Read(session.Manager, file, prefab.PathId);
        }
        catch (InvalidDataException ex)
        {
            throw new TyrantException(TyrantErrorCode.AssetUnreadable, $"'{prefab.Ref}' could not be read as a prefab: {ex.Message}", FixAction.None, ex);
        }
    }

    public IReadOnlyList<ModelExportResult> WriteModels(PrefabModel model, string outputDir)
    {
        var results = new List<ModelExportResult>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var renderer in model.Renderers)
        {
            var name = renderer.Mesh.Name.Length > 0 ? renderer.Mesh.Name : renderer.Name;
            var baseName = TextureExporter.Sanitize(name);
            var path = Path.Combine(outputDir, baseName + ".glb");
            for (var n = 2; !used.Add(path); n++) path = Path.Combine(outputDir, $"{baseName}_{n}.glb");
            try
            {
                GltfModelWriter.WriteGlb(model, renderer, path);
                results.Add(new ModelExportResult(name, renderer, true, path, null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // SharpGLTF reports invalid models with its own exception types; one bad renderer must not stop the rest.
                results.Add(new ModelExportResult(name, renderer, false, path, ex.Message));
            }
        }
        foreach (var failure in model.Failures)
            results.Add(new ModelExportResult(failure.Split(':')[0], null, false, "", failure));
        return results;
    }

    /// <summary>Writes &lt;workspace&gt;/assets/models/&lt;prefab&gt;/*.glb, replacing a previous export of the same prefab.</summary>
    public IReadOnlyList<ModelExportResult> Export(GameInstall install, Workspace ws, AssetRecord prefab)
    {
        using var session = new AssetSession(install);
        var model = ReadPrefab(session, prefab);
        session.Release();
        var dir = Path.Combine(ws.AssetsDir, "models", TextureExporter.Sanitize(model.Name));
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        var results = WriteModels(model, dir);
        if (results.Any(r => r.Success)) ws.StampOutput(OutputName, GameFingerprint.Compute(install));
        return results;
    }
}
