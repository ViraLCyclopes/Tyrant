using Tyrant.Core.Assets;
using Tyrant.Core.Blender;
using Tyrant.Core.Errors;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Rpc.Protocol;
using Tyrant.Rpc.Studio;

namespace Tyrant.Rpc.Blender;

/// <summary>The Workspace tab's Blender card: the Blender found, its version, Tyrant's add-on (missing, older, current, newer, unknown).</summary>
public sealed record BlenderStatusDto(bool Found, string? Exe, string? Version, bool Supported, string? MissingConfigured,
    string Addon, string? AddonInstalled, string? AddonBundled, string? Problem);

public sealed record BlenderSetPathParams(string? Path);

/// <summary>Sex: "male" or "female", the sex Blender shows first. PrefabRef: any GameObject from the Assets tab instead of a species.</summary>
public sealed record BlenderOpenParams(string Species = "", string? Skin = null, string? Mod = null, bool Fresh = false, bool Lods = false, string Sex = "male",
    string? PrefabRef = null, bool Ik = true);

/// <summary>How: "running" (the open Blender took it) or "started" (a new Blender).</summary>
public sealed record BlenderOpenDto(string ProjectFile, string How, bool GameChanged);

/// <summary>blender.* — find Blender, install Tyrant's add-on, open models in Blender.</summary>
public sealed class BlenderMethods(StudioSession session)
{
    private BlenderService Service => new(session.Options.Blender);

    [RpcMethod("blender.status")]
    public BlenderStatusDto Status()
    {
        var (ws, _) = session.Current();
        return Dto(Service.Status(ws));
    }

    [RpcMethod("blender.setPath")]
    public BlenderStatusDto SetPath(BlenderSetPathParams p)
    {
        var (ws, _) = session.Current();
        var status = Service.SetPath(ws, string.IsNullOrWhiteSpace(p.Path) ? null : p.Path);
        session.Log(p.Path is null ? "Tyrant finds Blender by itself again." : $"Tyrant uses Blender at {p.Path}.");
        return Dto(status);
    }

    [RpcMethod("blender.installAddon")]
    public BlenderStatusDto InstallAddon()
    {
        var (ws, _) = session.Current();
        var status = Service.InstallAddon(ws);
        session.Log($"Installed Tyrant's add-on {status.AddonInstalled} into Blender {status.Version}. Restart Blender if it is open.");
        return Dto(status);
    }

    [RpcMethod("blender.open")]
    public BlenderOpenDto Open(BlenderOpenParams p)
    {
        var (ws, install) = session.Current();
        var service = Service;
        service.CheckReady(ws);
        var index = TryIndex(ws) ?? throw new TyrantException(TyrantErrorCode.AssetIndexMissing,
            "Open in Blender needs the asset index: on the Workspace tab click Index assets (or run 'tyrant assets index').", FixAction.ReindexAssets);
        IReadOnlyList<SpeciesSkins> species;
        try
        {
            species = SpeciesSkinsReader.Load(ws);
        }
        catch (TyrantException ex) when (ex.Code == TyrantErrorCode.DataMissing)
        {
            throw new TyrantException(TyrantErrorCode.DataMissing,
                "Open in Blender needs the game's data: on the Workspace tab click Run data dump (or run 'tyrant dump run').", FixAction.RefreshWorkspace, ex);
        }
        var result = service.Open(ws, install, index, species, session.Options.AssetReader, new BlenderOpenRequest(p.Species, p.Skin, p.Mod, p.Fresh, p.Lods, p.Sex) { PrefabRef = p.PrefabRef, Ik = p.Ik });
        var what = p.PrefabRef is not null ? "the object" : p.Mod is null ? p.Species + (p.Skin is null ? "" : $" ({p.Skin})") : $"{p.Skin ?? p.Species} from '{p.Mod}'";
        session.Log(result.How == "running" ? $"Opened {what} in the running Blender." : $"Started Blender with {what}.");
        if (result.GameChanged) session.Log("The game was updated since this Blender project was made: use Start fresh for the new model.", "warn");
        return new BlenderOpenDto(result.ProjectFile, result.How, result.GameChanged);
    }

    private static AssetIndex? TryIndex(Workspace ws)
    {
        var path = AssetIndex.PathIn(ws);
        return File.Exists(path) ? AssetIndex.Load(path) : null;
    }

    private static BlenderStatusDto Dto(BlenderStatus s) =>
        new(s.Found, s.Exe, s.Version, s.Supported, s.MissingConfigured, s.Addon, s.AddonInstalled, s.AddonBundled, s.Problem);
}
