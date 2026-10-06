using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Jobs;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Animation;

/// <summary>The written .glb files and what could not be exported (named).</summary>
public sealed record AnimationExportResult(IReadOnlyList<string> Files, IReadOnlyList<string> Notes);

/// <summary>
/// A species' animations as files: the model's first level of detail with the game's materials, plus the animations as glTF
/// animations (each named after its clip). One file per animation, or all in one. FBX is made from these by the caller.
/// </summary>
public static class AnimationExporter
{
    /// <summary>Above this many animations, exporting them all asks first (the app) or names the count (the CLI).</summary>
    public const int ManyAnimations = 50;

    /// <summary>
    /// Export in the asked format: FBX (default for modders) is converted from the .glb files by the user's Blender
    /// (<paramref name="converter"/>, null for glb), each animation a take; "both" keeps the .glb files too.
    /// </summary>
    public static AnimationExportResult Run(GameInstall install, Workspace ws, AssetIndex index, IReadOnlyList<SpeciesSkins> species,
        IAssetReader reader, string speciesId, IReadOnlyList<string> ids, string outDir, bool singleFile, Blender.ModelFormat format,
        Blender.IModelConverter? converter, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var glbs = Export(install, ws, index, species, reader, speciesId, ids, outDir, singleFile, progress, ct);
        if (format == Blender.ModelFormat.Glb || converter is null || glbs.Files.Count == 0) return glbs;
        progress?.Report(new JobProgress(0, "Converting to FBX"));
        var (files, notes) = Blender.ModelFormats.Apply(converter, glbs.Files, format, keepGlb: format == Blender.ModelFormat.Both,
            v => progress?.Report(new JobProgress(v, "Converting to FBX")), ct);
        return new AnimationExportResult(files, [.. glbs.Notes, .. notes]);
    }

    public static AnimationExportResult Export(GameInstall install, Workspace ws, AssetIndex index, IReadOnlyList<SpeciesSkins> species,
        IAssetReader reader, string speciesId, IReadOnlyList<string> ids, string outDir, bool singleFile, IProgress<JobProgress>? progress,
        CancellationToken ct)
    {
        var (id, prefab) = ModProject.ResolveModelTarget(index, species, speciesId, null);
        progress?.Report(new JobProgress(0, $"Reading {ids.Count} animation(s)"));
        var clips = AnimationService.Clips(ws, install, index, species, reader, id, ids);
        var notes = clips.Where(c => c.Clip is null).Select(c => c.Failure ?? $"'{c.Id}' could not be read.").ToList();
        notes.AddRange(clips.Where(c => c.Clip is not null).SelectMany(c => c.Clip!.Skipped.Select(s => $"{c.Clip!.Name}: {s}")).Distinct());
        var ready = clips.Where(c => c.Clip is not null).Select(c => c.Clip!).ToList();
        Directory.CreateDirectory(outDir);
        var stem = TextureExporter.Sanitize(id);
        if (ready.Count == 0) return new AnimationExportResult([], notes);
        IReadOnlyList<(string Path, IReadOnlyList<ClipAnimation> Animations)> files = singleFile
            ? [(Path.Combine(outDir, $"{stem}-animations.glb"), ready)]
            : ready.Select(c => (Path.Combine(outDir, $"{stem}-{TextureExporter.Sanitize(c.Name)}.glb"), (IReadOnlyList<ClipAnimation>)[c])).ToList();
        ct.ThrowIfCancellationRequested();
        progress?.Report(new JobProgress(0.5, $"Writing {files.Count} file(s)"));
        reader.WriteAnimatedModels(install, prefab, index, files);
        progress?.Report(new JobProgress(1, "Done"));
        return new AnimationExportResult(files.Select(f => f.Path).ToList(), notes);
    }
}
