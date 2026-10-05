using Tyrant.Core.Assets;
using Tyrant.Core.Blender;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.ModelReplacements;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;
using Tyrant.Rpc.Assets;
using Tyrant.Rpc.Protocol;

namespace Tyrant.Rpc.Mods;

/// <summary>mods.* for model replacements: replace, remove, rebuild, preview.</summary>
public sealed partial class ModsMethods
{
    [RpcMethod("mods.replaceModel")]
    public ModDetail ReplaceModel(ModReplaceModelParams p)
    {
        var (ws, install) = session.Current();
        var mod = ModProject.Open(ws, p.Id, p.Revision);
        var report = mod.ReplaceModel(install, RequireIndex(ws), RequireSpecies(ws), Options.AssetReader, p.File, p.Target, p.PrefabRef, p.Skin,
            () => new BlenderService(Options.Blender).Converter(ws));
        session.Log($"'{p.Id}' replaces a model with {Path.GetFileName(p.File)} ({report.Lods.Count} LOD(s)){(report.Warnings.Count == 0 ? "" : ": " + string.Join(" ", report.Warnings))}.");
        return DetailOf(ws, mod);
    }

    [RpcMethod("mods.removeModel")]
    public ModDetail RemoveModel(ModRemoveModelParams p) =>
        Edit(p.Id, p.Revision, mod => mod.RemoveModel(p.Target, p.Skin),
            p.Skin is null ? $"no longer replaces {p.Target}'s model" : $"'{p.Skin}' uses the species model again");

    /// <summary>Rebuilds every model of the mod (the Rebuild LODs button), not only the ones whose .glb changed.</summary>
    [RpcMethod("mods.rebuildModels")]
    public ModDetail RebuildModels(ModRebuildModelsParams p)
    {
        var (ws, install) = session.Current();
        var mod = ModProject.Open(ws, p.Id, p.Revision);
        var index = RequireIndex(ws);
        var species = RequireSpecies(ws);
        foreach (var (speciesId, file) in mod.ModelEntries()) // only files inside the mod
            ModelBuilder.Build(mod.Dir, file, Options.AssetReader.ReadPrefabModel(install, ModProject.ResolveModelTarget(index, species, speciesId, null).Prefab), null, mod.RigOfFile(file));
        session.Log($"'{p.Id}': rebuilt its models and their levels of detail.");
        return DetailOf(ws, mod);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> PreviewLocks = new();

    private static readonly System.Text.Json.JsonSerializerOptions PreviewJson = new() { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };

    /// <summary>
    /// One .glb per LOD of a replacement model, on the game's skeleton with the game's materials, for the 3D preview. Made once per
    /// build (kept in a folder keyed by its LOD files' stamps) and one request at a time per model, so overlapping requests share it.
    /// </summary>
    [RpcMethod("mods.modelPreview")]
    public ModModelPreview ModelPreview(ModModelPreviewParams p)
    {
        var (ws, install) = session.Current();
        var mod = ModProject.Open(ws, p.Id);
        var file = p.Skin is null ? mod.Manifest.Models.FirstOrDefault(m => m.Target == p.Target)?.File : mod.Skin(p.Skin).Model;
        if (file is null) throw new TyrantException(TyrantErrorCode.TargetNotFound, $"'{p.Id}' has no model for {p.Skin ?? p.Target}.");
        var index = RequireIndex(ws);
        var prefab = ModProject.ResolveModelTarget(index, RequireSpecies(ws), p.Target, null).Prefab;
        var report = ModelBuilder.ReadReport(mod.Dir, file);
        if (report is null || report.Errors.Count > 0 || report.Lods.Count == 0)
            throw new TyrantException(TyrantErrorCode.ModInvalid, "The model has no built levels of detail to show: fix its problems (see Check), then click Rebuild LODs.");
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"{mod.Dir}|{file}|{string.Join("|", report.Lods.Select(l => ModelBuilder.Stamp(Path.Combine(mod.Dir, l.File))))}")))[..16].ToLowerInvariant();
        var dir = Path.Combine(PreviewsDir(ws, "model-replacements"), key);
        lock (PreviewLocks.GetOrAdd(dir, _ => new object()))
        {
            var saved = Path.Combine(dir, "preview.json");
            if (File.Exists(saved))
            {
                try
                {
                    var cached = System.Text.Json.JsonSerializer.Deserialize<ModModelPreview>(File.ReadAllText(saved), PreviewJson);
                    if (cached is not null && cached.Lods.All(l => File.Exists(Path.Combine(dir, l.File)))) return cached;
                }
                catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or NotSupportedException)
                {
                    // made again below
                }
            }
            var lods = report.Lods.Select(l =>
            {
                using var stream = File.OpenRead(Path.Combine(mod.Dir, l.File));
                return TMesh.Read(stream);
            }).ToList();
            var facts = Options.AssetReader.WriteReplacedModel(install, prefab, lods, dir, index, mod.RigOfFile(file));
            var preview = new ModModelPreview(prefab.Ref,
                facts.Parts.Select((part, i) => new ModModelPreviewLod(part.File, i < lods.Count ? lods[i].VertexCount : part.Vertices)).ToList(),
                facts.Materials.Select(m => new PreviewMaterial(m.Name, m.BaseColor?.Name, m.Normal?.Name, false, m.Shader, m.Animal, m.Cutoff,
                    m.Slots.Select(s => new PreviewSlot(s.Name, s.Texture.Name, facts.TextureFiles.GetValueOrDefault(s.Texture.Ref))).ToList())).ToList());
            File.WriteAllText(saved, System.Text.Json.JsonSerializer.Serialize(preview, PreviewJson));
            return preview;
        }
    }

    private static ModModelDto ModelDto(ModProject mod, string target, string? skin, string file)
    {
        var report = ModelBuilder.ReadReport(mod.Dir, file);
        return new ModModelDto(target, skin, file,
            report?.Lods.Select(l => new ModModelLodDto(l.File, l.Vertices, l.Index32, l.Vanilla)).ToList() ?? [],
            report?.Errors ?? [], report?.Warnings ?? [], ModelBuilder.IsStale(mod.Dir, file, mod.RigOfFile(file)),
            report?.Origin, report is not null && ModelBuilder.OriginChanged(report));
    }

    /// <summary>Before Check and Install: models whose .glb changed are built again (needs the index and the data dump).</summary>
    private void RebuildStaleModels(Workspace ws, GameInstall install, ModProject mod)
    {
        if (!mod.ModelEntries().Any() || TryIndex(ws) is not { } index || TrySpecies(ws) is not { } species) return;
        try
        {
            foreach (var file in mod.RebuildStaleModels(install, index, species, Options.AssetReader))
                session.Log($"'{mod.Id}': rebuilt {file} (it changed since it was built).");
        }
        catch (TyrantException ex)
        {
            session.Log($"'{mod.Id}': a model could not be rebuilt: {ex.Message}"); // Check then reports it as out of date
        }
    }

    private static AssetIndex RequireIndex(Workspace ws) => TryIndex(ws) ?? throw new TyrantException(TyrantErrorCode.AssetIndexMissing,
        "Model replacements need the asset index: on the Workspace tab click Index assets (or run 'tyrant assets index').", FixAction.ReindexAssets);

    private static IReadOnlyList<SpeciesSkins> RequireSpecies(Workspace ws) => TrySpecies(ws) ?? throw new TyrantException(TyrantErrorCode.DataMissing,
        "Model replacements need the game's data: on the Workspace tab click Run data dump (or run 'tyrant dump run').", FixAction.RefreshWorkspace);
}
