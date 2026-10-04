using Tyrant.Core.Errors;
using Tyrant.Core.Mods;
using Tyrant.Core.Sounds;
using Tyrant.Core.Workspaces;
using Tyrant.Rpc.Protocol;

namespace Tyrant.Rpc.Mods;

/// <summary>mods.* for sound replacements: the species panel's Replace in a mod and the mod editor's Sounds section.</summary>
public sealed partial class ModsMethods
{
    /// <summary>Revision is null from the species panel (no editor open); volume and age pitch are optional first settings.</summary>
    [RpcMethod("mods.replaceSound")]
    public ModDetail ReplaceSound(ModReplaceSoundParams p)
    {
        var (ws, _) = session.Current();
        var mod = ModProject.Open(ws, p.Id, p.Revision);
        var entry = mod.ReplaceSound(p.Event, p.Files, p.Species, p.Skin);
        if (p.Volume is not null || p.AgePitch is not null)
            mod.SetSound(entry.Event, entry.Species, entry.Skin, p.Volume, p.AgePitch, null, null, null);
        session.Log($"'{p.Id}' replaces {SoundCatalog.NameOf(entry.Event).ToLowerInvariant()} ({entry.Event}) {ModProject.ScopeText(entry.Species, entry.Skin)} with {entry.Files.Count} file(s).");
        return DetailOf(ws, mod);
    }

    [RpcMethod("mods.removeSound")]
    public ModDetail RemoveSound(ModRemoveSoundParams p) =>
        Edit(p.Id, p.Revision, mod => mod.RemoveSound(p.Event, p.Species, p.Skin),
            $"no longer replaces {p.Event} {ModProject.ScopeText(Blank(p.Species), Blank(p.Skin))}");

    [RpcMethod("mods.setSound")]
    public ModDetail SetSound(ModSetSoundParams p) =>
        Edit(p.Id, p.Revision, mod => mod.SetSound(p.Event, p.Species, p.Skin, p.Volume, p.AgePitch, p.NewSpecies, p.NewSkin, p.ForEveryone),
            $"changed its replacement of {p.Event}");

    private static ModSoundDto SoundDto(Tyrant.Framework.Core.SoundReplacement s) =>
        new(s.Event, SoundCatalog.NameOf(s.Event), SoundCatalog.GroupOf(s.Event), s.Species, s.Skin, s.Files, s.Volume, s.AgePitch);

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>The dump's sound lists for Check; null without a dump (sounds are then checked without them).</summary>
    private static SoundCatalog? TrySounds(Workspace ws)
    {
        try
        {
            return SoundCatalog.Load(ws);
        }
        catch (TyrantException)
        {
            return null;
        }
    }

    /// <summary>Check as the Mods tab, Install and Export run it: the index, and the dump for skins and sounds when there is one.</summary>
    private ModCheckResult CheckMod(Workspace ws, Tyrant.Core.Install.GameInstall install, ModProject mod)
    {
        var m = mod.Manifest;
        return ModChecker.ForGame(install).Check(mod, TryIndex(ws), m.Skins.Count > 0 || m.Sounds.Count > 0 ? TrySpecies(ws) : null,
            m.Sounds.Count > 0 ? TrySounds(ws) : null);
    }
}
