using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.ModelReplacements;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Blender;

/// <summary>Everything the Blender operations touch outside Tyrant; tests replace each part (a record, so one field can change with `with`).</summary>
public sealed record BlenderEnvironment
{
    public required IBlenderProcess Process { get; init; }
    public required IBlenderLink Link { get; init; }
    public required ISteamRootProvider Steam { get; init; }
    public string ProgramFiles { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    public string AppData { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>The add-on this Tyrant ships (blender\tyrant_blender.zip next to tyrant.exe).</summary>
    public string AddonZip { get; init; } = Path.Combine(AppContext.BaseDirectory, "blender", "tyrant_blender.zip");

    /// <summary>The tyrant.exe the add-on runs to send models back (this process in the app's sidecar and the CLI).</summary>
    public string TyrantExe { get; init; } = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "tyrant.exe");

    public static BlenderEnvironment Default() => new() { Process = new BlenderProcess(), Link = new BlenderLink(), Steam = new RegistrySteamRootProvider() };
}

/// <summary>The Blender card's facts. Addon: "missing", "older", "current", "newer", or "unknown" (no Blender).</summary>
public sealed record BlenderStatus(bool Found, string? Exe, string? Version, bool Supported, string? MissingConfigured,
    string Addon, string? AddonInstalled, string? AddonBundled, string? Problem);

/// <summary>How: "running" (the open Blender took it) or "started" (a new Blender).</summary>
public sealed record BlenderOpenResult(string ProjectFile, string How, bool GameChanged);

public sealed record BlenderSkinChoice(string Id, string Name);
public sealed record BlenderDestinationChoice(string Id, string Name, IReadOnlyList<BlenderSkinChoice> Skins);

/// <summary>Send to Tyrant's answer, shown in Blender's Tyrant panel.</summary>
public sealed record BlenderSendResult(bool Ok, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, IReadOnlyList<int> LodVertices, BlenderDestination? Destination)
{
    /// <summary>The image slots Send put into the mod, and where ("skin maps (male)", "texture replacements").</summary>
    public IReadOnlyList<string> Images { get; init; } = [];

    public string? ImagesTo { get; init; }
}

/// <summary>What the app's Blender card and Open in Blender buttons, and 'tyrant blender', do.</summary>
public sealed class BlenderService(BlenderEnvironment env)
{
    public BlenderStatus Status(Workspace ws)
    {
        var find = Locator().Find(ws.Data.BlenderPath);
        var bundled = BlenderAddon.BundledVersion(env.AddonZip);
        if (find.Install is not { } b)
            return new BlenderStatus(false, null, null, false, find.MissingConfigured, "unknown", null, bundled,
                find.MissingConfigured is { } gone ? $"Blender is not at {gone} any more, and no other Blender was found." : "Blender was not found.");
        var installed = BlenderAddon.InstalledVersion(b, env.AppData);
        return new BlenderStatus(true, b.Exe, b.Version.ToString(3), b.Supported, find.MissingConfigured,
            AddonState(b, installed, bundled).ToString().ToLowerInvariant(), installed, bundled, b.Supported ? null : BlenderAddon.TooOld(b));
    }

    /// <summary>Uses this blender.exe from now on (saved in the workspace); null finds Blender again.</summary>
    public BlenderStatus SetPath(Workspace ws, string? exe)
    {
        if (exe is not null && Locator().Probe(exe) is null)
            throw new TyrantException(TyrantErrorCode.BlenderMissing, $"'{exe}' is not Blender (it did not answer 'blender --version').");
        ws.Data.BlenderPath = exe;
        ws.Save();
        return Status(ws);
    }

    public BlenderStatus InstallAddon(Workspace ws)
    {
        var blender = RequireBlender(ws);
        if (!File.Exists(env.AddonZip))
            throw new TyrantException(TyrantErrorCode.BlenderFailed, "This Tyrant has no Blender add-on (blender\\tyrant_blender.zip next to tyrant.exe); reinstall Tyrant.");
        BlenderAddon.Install(env.Process, blender, env.AddonZip);
        return Status(ws);
    }

    /// <summary>Throws the reason Open in Blender cannot work now (no Blender, Blender 4, add-on missing or older), naming the fix.</summary>
    public BlenderInstall CheckReady(Workspace ws)
    {
        var blender = RequireBlender(ws);
        var installed = BlenderAddon.InstalledVersion(blender, env.AppData);
        var state = AddonState(blender, installed, BlenderAddon.BundledVersion(env.AddonZip));
        if (state is Blender.AddonState.Missing or Blender.AddonState.Older or Blender.AddonState.Changed)
            throw new TyrantException(TyrantErrorCode.BlenderMissing, state switch
            {
                Blender.AddonState.Missing => $"Tyrant's add-on is not installed in Blender {blender.MajorMinor}: click Install add-on on the Workspace tab (Blender card), or run 'tyrant blender install-addon'.",
                Blender.AddonState.Older => $"Tyrant's add-on in Blender is older ({installed}) than this Tyrant's: click Update add-on on the Workspace tab (Blender card), or run 'tyrant blender install-addon'.",
                _ => "Tyrant's add-on in Blender is an older build than this Tyrant's: click Update add-on on the Workspace tab (Blender card), or run 'tyrant blender install-addon'.",
            });
        return blender;
    }

    public BlenderOpenResult Open(Workspace ws, GameInstall install, AssetIndex index, IReadOnlyList<SpeciesSkins> species, IAssetReader reader, BlenderOpenRequest request)
    {
        var blender = CheckReady(ws);
        var project = BlenderProjectWriter.Write(request, ws, install, index, species, reader, env.TyrantExe);
        if (env.Link.TryOpen(project.ProjectFile)) return new BlenderOpenResult(project.ProjectFile, "running", project.GameChanged);
        env.Process.Start(blender.Exe, ["--python-expr", StartScript(project.ProjectFile)]);
        return new BlenderOpenResult(project.ProjectFile, "started", project.GameChanged);
    }

    /// <summary>Run by a fresh Blender: enable the add-on, then open the project once Blender's window is up.</summary>
    internal static string StartScript(string projectFile)
    {
        if (projectFile.Contains('\'') || projectFile.EndsWith('\\'))
            throw new TyrantException(TyrantErrorCode.BlenderFailed, $"Tyrant cannot pass '{projectFile}' to Blender (it contains '); move the workspace to a folder without it.");
        return string.Join("\n",
            "import addon_utils, bpy, importlib",
            $"addon_utils.enable('{BlenderAddon.ModuleName}', default_set=True)",
            "def _tyrant_open():",
            $"    importlib.import_module('{BlenderAddon.ModuleName}.ui').open_project(r'{projectFile}', started=True)",
            "bpy.app.timers.register(_tyrant_open, first_interval=1.0)");
    }

    /// <summary>Every workspace mod with that species' skins, for Send to Tyrant's first "where to?".</summary>
    public static IReadOnlyList<BlenderDestinationChoice> Destinations(Workspace ws, string speciesId) =>
        ModProject.Ids(ws).Select(id => ModProject.Open(ws, id)).Select(m => new BlenderDestinationChoice(m.Id, m.Manifest.Name,
            m.Manifest.Skins.Where(s => string.Equals(s.Species, speciesId, StringComparison.OrdinalIgnoreCase))
                .Select(s => new BlenderSkinChoice(s.Id, s.Name)).ToList())).ToList();

    /// <summary>
    /// Writes the game's IK chains into an existing project (one written before Tyrant read them), for the add-on's Add IK
    /// controls; returns how many chains the model has.
    /// </summary>
    public static int RefreshIk(GameInstall install, AssetIndex index, IReadOnlyList<SpeciesSkins> species, IAssetReader reader, string projectFile)
    {
        var project = BlenderProjectFile.Read(projectFile);
        BlenderIk? ik = null;
        if (project.Source.Kind != "object")
        {
            var (_, prefabRecord) = ModProject.ResolveModelTarget(index, species, project.Source.Species, null);
            ik = BlenderIkReader.From(reader.ReadPrefabModel(install, prefabRecord));
        }
        BlenderProjectFile.Write(projectFile, project with { Ik = ik });
        return ik?.Chains.Count ?? 0;
    }

    /// <summary>
    /// Writes the asked animations (clip ids) as files next to a Blender project, for the add-on's Add animations; ids that
    /// cannot be read come back as errors naming them, the rest are written.
    /// </summary>
    public static (IReadOnlyList<string> Files, IReadOnlyList<string> Errors) Animations(Workspace ws, GameInstall install, AssetIndex index,
        IReadOnlyList<SpeciesSkins> species, IAssetReader reader, string projectFile, IReadOnlyList<string> ids)
    {
        var project = BlenderProjectFile.Read(projectFile);
        if (project.Source.Kind == "object")
            throw new TyrantException(TyrantErrorCode.ModInvalid, "Game objects have no animations: open an animal to add animations.");
        return WriteAnimations(ws, install, index, species, reader, project.Source.Species, Path.GetDirectoryName(projectFile)!, ids);
    }

    internal static (IReadOnlyList<string> Files, IReadOnlyList<string> Errors) WriteAnimations(Workspace ws, GameInstall install, AssetIndex index,
        IReadOnlyList<SpeciesSkins> species, IAssetReader reader, string speciesId, string projectDir, IReadOnlyList<string> ids)
    {
        var files = new List<string>();
        var errors = new List<string>();
        foreach (var (id, clip, failure) in Animation.AnimationService.Clips(ws, install, index, species, reader, speciesId, ids))
        {
            if (clip is null)
            {
                errors.Add(failure ?? $"'{id}' could not be read.");
                continue;
            }
            var file = Path.Combine(projectDir, "animations", TextureExporter.Sanitize(id) + ".json");
            Animation.AnimationService.WriteClip(file, clip);
            files.Add(file);
        }
        return (files, errors);
    }

    /// <summary>Writes the species' animation list into an existing project (one written before Tyrant listed them); returns how many.</summary>
    public static int RefreshAnimations(Workspace ws, GameInstall install, AssetIndex index, IReadOnlyList<SpeciesSkins> species, IAssetReader reader, string projectFile)
    {
        var project = BlenderProjectFile.Read(projectFile);
        var list = project.Source.Kind == "object" ? [] : Animation.AnimationService.List(ws, install, index, species, reader, project.Source.Species);
        BlenderProjectFile.Write(projectFile, project with { Animations = list });
        return list.Count;
    }

    /// <summary>
    /// Adds the exported .glb to the project's destination (or the one chosen now, saved into the project), creating the mod
    /// when newModName is given. Never throws for Tyrant's own errors: they come back in the result for Blender's panel.
    /// <paramref name="rig"/>: the armature's rig edit (an empty one clears the destination's; null keeps it). With
    /// <paramref name="rigOnly"/> only the rig edit goes in and the destination wears the game's mesh.
    /// </summary>
    public static BlenderSendResult Send(Workspace ws, GameInstall install, AssetIndex index, IReadOnlyList<SpeciesSkins> species, IAssetReader reader,
        string projectFile, string glb, BlenderDestination? choose, string? newModName, IReadOnlyList<BlenderImage>? images = null, string sex = "male",
        IReadOnlyDictionary<string, RigOffset>? rig = null, bool rigOnly = false)
    {
        BlenderDestination? destination = null;
        try
        {
            var project = BlenderProjectFile.Read(projectFile);
            destination = choose ?? project.Destination;
            if (destination is null)
                return new BlenderSendResult(false, ["Choose where to send it first: a mod, and the species model or one of its skins."], [], [], null);
            var rigWarnings = new List<string>();
            if (rigOnly && rig is not { Count: > 0 })
                return new BlenderSendResult(false, ["Rig edit only sends a rig edit, and this armature has none: make one with Start rig edit and Apply rig edit, or untick Rig edit only."], [], [], destination);
            if (rig is { Count: > 0 })
            {
                // Checked before anything is written (or a new mod made): a refused rig edit leaves everything as it was.
                var info = Rigging.RigInfoService.For(ws, install, index, species, reader, destination.Species);
                var where = destination.Skin is null ? $"Rig edit of {destination.Species}" : $"Skin '{destination.Skin}'";
                var (rigErrors, warnings) = Rigging.RigRules.Problems(rig, info, hasModel: !rigOnly, where);
                if (rigErrors.Count > 0) return new BlenderSendResult(false, rigErrors, warnings, [], destination);
                rigWarnings.AddRange(warnings);
            }
            var mod = ModProject.Ids(ws).Contains(destination.Mod, StringComparer.Ordinal) ? ModProject.Open(ws, destination.Mod)
                : newModName is not null ? ModProject.Create(ws, destination.Mod, newModName, null)
                : throw new TyrantException(TyrantErrorCode.ModNotFound, $"There is no mod '{destination.Mod}' in this workspace.");
            ModelReport? report;
            if (rigOnly)
            {
                // The game's mesh with the rig edit: the destination's own model (if any) is no longer used.
                if (destination.Skin is not null) mod.Skin(destination.Skin).Model = null;
                else if (mod.Manifest.Models.FirstOrDefault(m => string.Equals(m.Target, destination.Species, StringComparison.Ordinal)) is { } entry) entry.File = "";
                mod.SetRig(install, index, species, reader, destination.Species, destination.Skin, rig);
                report = null;
            }
            else report = mod.ReplaceModel(install, index, species, reader, glb, destination.Skin is null ? destination.Species : null, null, destination.Skin, rig: rig);
            BlenderProjectFile.Write(projectFile, project with { Destination = destination });
            // Only after the model went in: a refused model leaves the mod as it was.
            var pictures = BlenderImages.Apply(mod, ws, index, species, destination, images ?? [], sex, project.Source.Kind == "game" ? project.Source.Skin : null);
            return new BlenderSendResult(true, [], [.. report?.Warnings ?? [], .. rigWarnings, .. pictures.Warnings], report?.Lods.Select(l => l.Vertices).ToList() ?? [], destination)
            {
                Images = pictures.Written,
                ImagesTo = pictures.To,
            };
        }
        catch (TyrantException ex)
        {
            return new BlenderSendResult(false, [ex.Message], [], [], destination);
        }
    }

    /// <summary>The user's Blender as Tyrant's FBX converter; without Blender, says FBX needs it (glb works without).</summary>
    public IModelConverter Converter(Workspace ws)
    {
        try
        {
            return new BlenderModelConverter(env.Process, RequireBlender(ws).Exe);
        }
        catch (TyrantException ex) when (ex.Code == TyrantErrorCode.BlenderMissing)
        {
            throw new TyrantException(TyrantErrorCode.BlenderMissing, "FBX needs Blender: " + ex.Message, ex.Fix, ex);
        }
    }

    private BlenderInstall RequireBlender(Workspace ws)
    {
        var find = Locator().Find(ws.Data.BlenderPath);
        if (find.Install is not { } blender)
            throw new TyrantException(TyrantErrorCode.BlenderMissing,
                "Blender was not found: choose blender.exe on the Workspace tab (Blender card), or run 'tyrant blender set-path <blender.exe>'.");
        if (!blender.Supported) throw new TyrantException(TyrantErrorCode.BlenderMissing, BlenderAddon.TooOld(blender));
        return blender;
    }

    /// <summary>The version comparison, and for the same version whether the installed files are this Tyrant's.</summary>
    private AddonState AddonState(BlenderInstall blender, string? installed, string? bundled)
    {
        var state = BlenderAddon.State(installed, bundled);
        // Without this Tyrant's zip there is nothing to compare the files with: the version decides.
        return state == Blender.AddonState.Current && File.Exists(env.AddonZip)
            && !BlenderAddon.SameFiles(env.AddonZip, BlenderAddon.InstalledDir(blender, env.AppData))
            ? Blender.AddonState.Changed
            : state;
    }

    private BlenderLocator Locator() => new(env.Steam, env.ProgramFiles, env.Process);
}
