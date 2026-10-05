using Tyrant.Core.Errors;
using Tyrant.Core.Mods;
using Tyrant.Core.Rigging;
using Tyrant.Rpc.Protocol;

namespace Tyrant.Rpc.Mods;

/// <summary>mods.* for rig edits (made in Blender): show one and clear it.</summary>
public sealed partial class ModsMethods
{
    [RpcMethod("mods.rig")]
    public ModRigView Rig(ModRigParams p)
    {
        var (ws, install) = session.Current();
        var mod = ModProject.Open(ws, p.Id);
        var skin = p.Skin is null ? null : mod.Skin(p.Skin);
        var species = skin?.Species ?? p.Target;
        var rig = mod.RigOf(species, skin?.Id);
        var hasModel = skin is not null ? skin.Model is not null
            : mod.Manifest.Models.Any(m => string.Equals(m.Target, species, StringComparison.Ordinal) && m.File.Length > 0);
        if (rig is not { Count: > 0 }) return new ModRigView([], hasModel, [], []);
        var where = skin is null ? $"Rig edit of {species}" : $"Skin '{skin.Id}'";
        RigInfo? info = null;
        if (TryIndex(ws) is { } index && TrySpecies(ws) is { } all)
        {
            try
            {
                info = RigInfoService.For(ws, install, index, all, Options.AssetReader, species);
            }
            catch (TyrantException)
            {
                // shown without Check's findings
            }
        }
        var (errors, warnings) = info is null
            ? ([], hasModel ? [] : [$"{where}: the rig edit has no model of its own: the game's mesh stretches with the moved bones."])
            : RigRules.Problems(rig, info, hasModel, where);
        return new ModRigView(ModRigBone.Of(rig)!, hasModel, errors, warnings);
    }

    [RpcMethod("mods.clearRig")]
    public ModDetail ClearRig(ModClearRigParams p)
    {
        var (ws, install) = session.Current();
        return Edit(p.Id, p.Revision, mod => mod.SetRig(install, RequireIndex(ws), RequireSpecies(ws), Options.AssetReader, p.Skin is null ? p.Target : mod.Skin(p.Skin).Species, p.Skin, null),
            p.Skin is null ? $"cleared the rig edit of {p.Target}" : $"cleared the rig edit of skin '{p.Skin}'");
    }
}
