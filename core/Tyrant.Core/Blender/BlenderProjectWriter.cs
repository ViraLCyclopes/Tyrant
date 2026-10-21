using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.ModelReplacements;
using Tyrant.Core.Models;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Blender;

/// <summary>What to open: a species (with a game skin), or with Mod a mod's skin (Skin = its id) or its species model.</summary>
/// <summary>Sex: which sex Blender shows first ("male" or "female"). PrefabRef: any GameObject from the Assets tab instead of a species.</summary>
public sealed record BlenderOpenRequest(string Species, string? Skin, string? Mod, bool Fresh, bool Lods, string Sex = "male")
{
    public string? PrefabRef { get; init; }

    /// <summary>False: Open in Blender builds no IK controls (the chains are still written, for Add IK controls later).</summary>
    public bool Ik { get; init; } = true;

    /// <summary>Animations (clip ids, e.g. "Carch|LocWalk") the model opens with, as Actions.</summary>
    public IReadOnlyList<string> Animations { get; init; } = [];
}

public sealed record BlenderProjectResult(string ProjectFile, string Dir, bool GameChanged);

/// <summary>Writes (or refreshes) the Blender project folder for one target: model.glb, textures/ and tyrant-blender.json.</summary>
public static class BlenderProjectWriter
{
    public const string ModelFile = "model.glb";

    /// <summary>&lt;workspace&gt;/blender/&lt;mod, or "game"&gt;: every model of that mod opened in Blender.</summary>
    public static string ModFolderOf(string workspaceDir, string? modId) =>
        Path.Combine(workspaceDir, "blender", modId is null ? "game" : TextureExporter.Sanitize(modId));

    /// <summary>One model's Blender project folder: &lt;mod folder&gt;/&lt;species[-suffix]&gt;, lowercased and made safe.</summary>
    public static string FolderOf(string workspaceDir, string? modId, string speciesId, string? suffix) =>
        Path.Combine(ModFolderOf(workspaceDir, modId), TextureExporter.Sanitize((speciesId + (suffix is null ? "" : "-" + suffix)).ToLowerInvariant()));

    public static BlenderProjectResult Write(BlenderOpenRequest request, Workspace ws, GameInstall install, AssetIndex index,
        IReadOnlyList<SpeciesSkins> species, IAssetReader reader, string tyrantExe)
    {
        if (request.PrefabRef is { } prefabRef)
        {
            var record = index.Resolve(prefabRef, "GameObject");
            var owner = record.Guid is null ? null : species.FirstOrDefault(s => string.Equals(s.PrefabGuid, record.Guid, StringComparison.OrdinalIgnoreCase));
            return owner is not null
                ? Write(request with { PrefabRef = null, Species = owner.SpeciesId }, ws, install, index, species, reader, tyrantExe)
                : WriteObject(request, record, ws, install, index, reader, tyrantExe);
        }
        var mod = request.Mod is null ? null : ModProject.Open(ws, request.Mod);
        var ownSkin = mod is not null && request.Skin is not null ? mod.Skin(request.Skin) : null;
        var (speciesId, prefabRecord) = ModProject.ResolveModelTarget(index, species, ownSkin?.Species ?? request.Species, null);
        var speciesEntry = mod?.Manifest.Models.FirstOrDefault(m => string.Equals(m.Target, speciesId, StringComparison.Ordinal));
        var modReplacement = speciesEntry is { File.Length: > 0 } ? speciesEntry.File : null;
        var customOfMod = mod is not null && mod.Manifest.Species.Any(s => s.Key(mod.Id) == speciesId);
        if (mod is not null && ownSkin is null && speciesEntry is null && !customOfMod)
            throw new TyrantException(TyrantErrorCode.TargetNotFound,
                $"'{mod.Id}' does not replace {speciesId}'s model; open it from the species instead (Assets → Species → Open in Blender).");
        var vanilla = ownSkin is not null ? SkinMaps.BaseOf(species, ownSkin) : SkinMaps.Vanilla(species, speciesId, request.Skin);
        if (mod is null && request.Skin is not null && vanilla is null)
            throw new TyrantException(TyrantErrorCode.TargetNotFound, $"{speciesId} has no skin '{request.Skin}'.");

        var kind = mod is null ? "game" : ownSkin is not null ? "skin" : "model";
        var suffix = kind == "game" ? vanilla?.Name : ownSkin?.Id;
        var dir = FolderOf(ws.Dir, mod?.Id, speciesId, suffix);
        var projectFile = Path.Combine(dir, BlenderProjectFile.FileName);
        var old = File.Exists(projectFile) ? TryRead(projectFile) : null;
        var build = GameFingerprint.Compute(install).BuildGuid;

        var existing = ExistingBlend(old, dir);
        // The .blend is the user's own file (the model is one scene in it): Start fresh never moves it. The add-on keeps the
        // model's scene as "(old)" and imports into a new one; the model the scene holds stays as long as Start fresh is not used.
        var keepModel = !request.Fresh && existing is not null;
        var modelBuild = keepModel && old is not null ? old.GameBuild : build;
        var gameChanged = modelBuild != build;

        Directory.CreateDirectory(dir);
        var prefab = reader.ReadPrefabModel(install, prefabRecord);
        var renderers = ModelBuilder.GameRenderers(prefab);
        var modelGlb = Path.Combine(dir, ModelFile);
        if (!keepModel || !File.Exists(modelGlb))
        {
            var ownModel = SkinDecides(ownSkin) ? ownSkin!.Model : modReplacement;
            if (mod is not null && ownModel is not null)
                File.Copy(Path.Combine(mod.Dir, ownModel.Replace('/', Path.DirectorySeparatorChar)), modelGlb, overwrite: true);
            else
                GltfModelWriter.WriteGlb(prefab, request.Lods ? renderers : renderers.Take(1).ToList(), modelGlb,
                    r => r.Materials.Select(m => new GltfMaterial(m.Name)).ToList());
        }

        // The model and rig edit the animal wears in game: the skin's entry (a model or a rig edit) wins, else the species'.
        var (rig, rigBaked) = SkinDecides(ownSkin) ? (ownSkin!.Rig, ownSkin.Model is not null && ownSkin.Rig is not null)
            : (speciesEntry?.Rig, modReplacement is not null && speciesEntry?.Rig is not null);
        (IReadOnlyList<string> Files, IReadOnlyList<string> Errors)? asked = request.Animations.Count == 0 ? null
            : BlenderService.WriteAnimations(ws, install, index, species, reader, speciesId, dir, request.Animations);
        var materials = Materials(renderers, index, prefabRecord.Bundle, install, reader, mod, ownSkin, vanilla, dir, request.Fresh);
        // A custom species grows like its template, from its starting skin.
        var ownSpecies = mod?.Manifest.Species.FirstOrDefault(s => s.Key(mod.Id) == speciesId);
        var dataId = ownSpecies?.Base ?? speciesId;
        var skinIndex = ownSpecies?.StartingSkin ?? vanilla?.Index ?? 0;
        var store = BlenderGrowthReader.TryStore(ws);
        var size = OriginalSizeOf(store, speciesId, mod);
        var growth = BlenderGrowthReader.Read(store, dataId, prefab.Root.DepthFirst().Select(n => n.Name));
        var skinSize = size is { Skins.Count: > 0 } ? size.Skins[Math.Clamp(skinIndex, 0, size.Skins.Count - 1)] : (Male: 1.0, Female: 1.0);
        var project = new BlenderProject(BlenderProjectFile.CurrentVersion, ws.Dir, tyrantExe, modelBuild,
            new BlenderSource(kind, speciesId, ownSkin?.Id ?? vanilla?.Name, mod?.Id),
            old?.Destination ?? (mod is null ? null : new BlenderDestination(mod.Id, speciesId, ownSkin?.Id)),
            materials, growth is not null && size is { Curve.Length: > 0 } ? growth with { OriginalScale = Keys(size.Curve) } : growth,
            Rest(prefab.Root), existing, request.Lods)
        {
            GameChanged = gameChanged,
            Fresh = request.Fresh,
            Sex = request.Sex == "female" ? "female" : "male",
            Sexes = BlenderGrowthReader.ReadSexes(store, dataId, skinIndex),
            Sizes = size is null ? null : new BlenderSizes(null, null, null, null, skinSize.Male, skinSize.Female),
            GrowthKeys = renderers.Count > 0
                ? renderers[0].Mesh.BlendShapes.Take(ModelFitter.GrowthKeys).Select(s => s.Name).ToList()
                : [],
            Ik = BlenderIkReader.From(prefab),
            IkOnOpen = request.Ik,
            Rig = rig?.ToDictionary(p => p.Key, p => BlenderRigOffset.From(p.Value)),
            RigBaked = rigBaked,
            RigInfo = RigInfoOf(ws, install, index, species, reader, speciesId),
            Animations = AnimationsOf(ws, install, index, species, reader, speciesId),
            AnimationFiles = asked?.Files.Select(f => Path.GetRelativePath(dir, f).Replace(Path.DirectorySeparatorChar, '/')).ToList(),
            AnimationErrors = asked is { Errors.Count: > 0 } ? asked.Value.Errors : null,
        };
        BlenderProjectFile.Write(projectFile, WithSizes(project, ws));
        return new BlenderProjectResult(projectFile, dir, gameChanged);
    }

    /// <summary>
    /// The destination mod's sizes on the project: the values for the Size panel, the sex sizes and the scale curve with them
    /// applied, whether Blender may change them (only for a species destination: a skin's sizes are its species') and its size limit.
    /// </summary>
    internal static BlenderProject WithSizes(BlenderProject project, Workspace ws)
    {
        var d = project.Destination;
        var destination = d is not null && ModProject.Ids(ws).Contains(d.Mod, StringComparer.Ordinal) ? ModProject.Open(ws, d.Mod) : null;
        var limit = SizeLimit.Of(destination?.Manifest.SizeRanges?.Limit);
        project = project with { SizeLimit = [limit.Min, limit.Max] };
        if (project.Sizes is not { } sizes) return project;
        var set = destination?.Sizes(d!.Species);
        var settings = set ?? new SizeSettings();
        var (male, female) = SizeMath.Sex(sizes.MaleOriginal, sizes.FemaleOriginal, settings);
        var growth = project.Growth;
        if (growth?.OriginalScale is { } original)
        {
            // As in game: the curve stays the game's own unless the adult or the baby changes.
            var changed = (settings.Adult ?? 1) != 1 || settings.Baby is not null;
            growth = growth with { Scale = changed ? Keys(SizeMath.Rebuild([.. original.Select(k => (k[0], k[1]))], settings.Adult ?? 1, settings.Baby)) : original };
        }
        return project with
        {
            Growth = growth,
            Sexes = project.Sexes is { } s ? new BlenderSexes(s.Male with { Size = (float)male }, s.Female with { Size = (float)female }) : null,
            Sizes = sizes with { Adult = set?.Adult, Baby = set?.Baby, Male = set?.Male, Female = set?.Female },
            SizesEditable = d is { Skin: null },
        };
    }

    private static float[][] Keys((float Time, float Value)[] keys) => [.. keys.Select(k => new[] { k.Time, k.Value })];

    /// <summary>The species' size in the data dump (a custom species' from its template); null without a dump or the species.</summary>
    private static OriginalSize? OriginalSizeOf(Tyrant.Core.Data.DataStore? store, string speciesId, ModProject? mod)
    {
        if (store is null) return null;
        try
        {
            return SpeciesSizes.Original(store, speciesId, mod);
        }
        catch (TyrantException)
        {
            return null;
        }
    }

    /// <summary>
    /// The project's .blend: the one recorded when it still exists, else (the workspace moved, the project file was replaced)
    /// the newest .blend in the project folder that is not a kept .old.blend.
    /// </summary>
    private static string? ExistingBlend(BlenderProject? old, string dir)
    {
        if (old?.Blend is { } recorded && File.Exists(recorded)) return Path.GetFullPath(recorded);
        if (!Directory.Exists(dir)) return null;
        return Directory.GetFiles(dir, "*.blend")
            .Where(f => !f.EndsWith(".old.blend", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// Any other GameObject (fences, scenery, buildings): its meshes with their pictures, for reference or as a base for new
    /// objects. No growth, sexes or destination (Send to Tyrant is for animals).
    /// </summary>
    private static BlenderProjectResult WriteObject(BlenderOpenRequest request, AssetRecord record, Workspace ws, GameInstall install,
        AssetIndex index, IAssetReader reader, string tyrantExe)
    {
        var name = record.Name.Length > 0 ? record.Name : $"object-{record.PathId}";
        var dir = Path.Combine(ws.Dir, "blender", "objects", TextureExporter.Sanitize($"{name}-{record.PathId}".ToLowerInvariant()));
        var projectFile = Path.Combine(dir, BlenderProjectFile.FileName);
        var old = File.Exists(projectFile) ? TryRead(projectFile) : null;
        var build = GameFingerprint.Compute(install).BuildGuid;
        var existing = ExistingBlend(old, dir);
        var keepModel = !request.Fresh && existing is not null;
        var modelBuild = keepModel && old is not null ? old.GameBuild : build;

        Directory.CreateDirectory(dir);
        var prefab = reader.ReadPrefabModel(install, record);
        // The most detailed level only (a prop's LOD1/LOD2 would sit on top of it), unless the far LODs were asked for.
        var renderers = prefab.Renderers.Where(r => request.Lods || !(GlbModelReader.HasLodSuffix(r.Name) || GlbModelReader.HasLodSuffix(r.Mesh.Name))
            || GlbModelReader.LodOf(GlbModelReader.HasLodSuffix(r.Name) ? r.Name : r.Mesh.Name) == 0).ToList();
        if (renderers.Count == 0)
            throw new TyrantException(TyrantErrorCode.AssetUnreadable, $"'{name}' has no mesh Tyrant can read, so there is nothing to open in Blender.");
        var modelGlb = Path.Combine(dir, ModelFile);
        if (!keepModel || !File.Exists(modelGlb))
            GltfModelWriter.WriteGlb(prefab, renderers, modelGlb, r => r.Materials.Select(m => new GltfMaterial(m.Name)).ToList());

        var materials = Materials(renderers, index, record.Bundle, install, reader, null, null, null, dir, request.Fresh);
        var project = new BlenderProject(BlenderProjectFile.CurrentVersion, ws.Dir, tyrantExe, modelBuild,
            new BlenderSource("object", name, null, null), null, materials, null, Rest(prefab.Root), existing, request.Lods)
        {
            GameChanged = modelBuild != build,
            Fresh = request.Fresh,
        };
        BlenderProjectFile.Write(projectFile, project);
        return new BlenderProjectResult(projectFile, dir, modelBuild != build);
    }

    private static BlenderProject? TryRead(string path)
    {
        try
        {
            return BlenderProjectFile.Read(path);
        }
        catch (TyrantException)
        {
            return null;
        }
    }

    private static Dictionary<string, BlenderMaterial> Materials(IReadOnlyList<RendererModel> renderers, AssetIndex index, string bundle,
        GameInstall install, IAssetReader reader, ModProject? mod, SkinEntry? ownSkin, VanillaSkin? vanilla, string dir, bool fresh)
    {
        var store = new ProjectTextures(Path.Combine(dir, "textures"), fresh);
        var colors = Colors(ownSkin);
        var result = new Dictionary<string, BlenderMaterial>(StringComparer.Ordinal);
        foreach (var material in renderers.SelectMany(r => r.Materials).Where(m => m.Name.Length > 0))
        {
            if (result.ContainsKey(material.Name)) continue;
            var resolved = MaterialResolver.Resolve(index, bundle, material);
            var maps = new Dictionary<string, string>(StringComparer.Ordinal);
            var femaleMaps = new Dictionary<string, string>(StringComparer.Ordinal);
            if (resolved.Animal)
            {
                var inherited = mod is not null && ownSkin is not null && StartingSkins.InheritsFrom(mod.Manifest, ownSkin, out var starting) ? starting : null;
                foreach (var slot in SkinMaps.Slots)
                {
                    var source = SkinMaps.Source(ownSkin, mod?.Dir, vanilla, slot, "male", inherited);
                    if (source is not null && store.Put(slot + ".png", SourceKey(source), png => SkinMaps.Write(source, install, index, reader, png) is not null))
                        maps[slot] = $"textures/{slot}.png";
                    // The female's own map, else the male's (as the male borrows hers).
                    var female = SkinMaps.Source(ownSkin, mod?.Dir, vanilla, slot, "female", inherited) ?? source;
                    if (female is not null && store.Put($"female/{slot}.png", SourceKey(female), png => SkinMaps.Write(female, install, index, reader, png) is not null))
                        femaleMaps[slot] = $"textures/female/{slot}.png";
                }
            }
            else
            {
                var stem = TextureExporter.Sanitize(material.Name);
                foreach (var (slot, record) in new[] { ("diffuse", resolved.BaseColor), ("normal", resolved.Normal) })
                {
                    if (record is null) continue;
                    var file = $"{stem}-{slot}.png";
                    if (store.Put(file, "asset:" + record.Ref, png => { reader.WriteTexture(install, record, png); return true; }))
                        maps[slot] = $"textures/{file}";
                }
            }
            result[material.Name] = new BlenderMaterial(resolved.Animal, resolved.Cutoff, maps, resolved.Animal ? colors : null) { FemaleMaps = femaleMaps };
        }
        store.Save();
        return result;
    }

    /// <summary>What a picture came from: a mod file with its size and time (a replaced PNG is a new source), or a game texture.</summary>
    private static string SourceKey(string source)
    {
        if (!source.StartsWith("file:", StringComparison.Ordinal)) return source;
        var info = new FileInfo(source[5..]);
        return $"{source}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
    }

    /// <summary>The colours the 3D view shows for a mod skin (the strip's first animal, seed 1); null = textures untouched.</summary>
    private static BlenderColors? Colors(SkinEntry? skin)
    {
        if (skin?.Colors is null) return null;
        var (set, tint) = ColorPreview.For(skin.Colors, "normal");
        var s = ColorPreview.Sample(set, tint, new Random(1 * 7919));
        return new BlenderColors(s.A?.ToString(), s.B?.ToString(), s.Secondary?.ToString(), s.Eye?.ToString(), s.Strength, s.Softness, s.Hue, s.Saturation, s.Value);
    }

    private static List<BlenderBoneRest> Rest(SkeletonNode root) =>
        root.DepthFirst().Skip(1).Select(n =>
        {
            var p = UnityToGltf.Position(n.LocalPosition);
            var q = UnityToGltf.Rotation(n.LocalRotation);
            return new BlenderBoneRest(n.Name, [p.X, p.Y, p.Z], [q.X, q.Y, q.Z, q.W], [n.LocalScale.X, n.LocalScale.Y, n.LocalScale.Z]);
        }).ToList();

    /// <summary>The species' animation list for the add-on; null when it cannot be worked out (opening goes on).</summary>
    private static IReadOnlyList<Animation.AnimationInfo>? AnimationsOf(Workspace ws, GameInstall install, AssetIndex index,
        IReadOnlyList<SpeciesSkins> species, IAssetReader reader, string speciesId)
    {
        try
        {
            return Animation.AnimationService.List(ws, install, index, species, reader, speciesId);
        }
        catch (TyrantException)
        {
            return null;
        }
    }

    private static bool SkinDecides(SkinEntry? skin) => skin is not null && (skin.Model is not null || skin.Rig is not null);

    /// <summary>The species' rig info for the add-on's warnings; null when it cannot be worked out (opening goes on).</summary>
    private static BlenderRigInfo? RigInfoOf(Workspace ws, GameInstall install, AssetIndex index, IReadOnlyList<SpeciesSkins> species, IAssetReader reader, string speciesId)
    {
        try
        {
            var info = Rigging.RigInfoService.For(ws, install, index, species, reader, speciesId);
            return new BlenderRigInfo(info.ClipMoved, info.GrowthMoved, info.GrowthScaled, Rigging.RigLimits.GrowthBonesSupported, info.Failures);
        }
        catch (TyrantException)
        {
            return null;
        }
    }
}
