using Tyrant.Core.Mods;
using Tyrant.Rpc.Protocol;

namespace Tyrant.Rpc.Mods;

/// <summary>mods.arrangeSkins and mods.delete: organizing a mod's skins, and deleting a whole mod.</summary>
public sealed partial class ModsMethods
{
    [RpcMethod("mods.arrangeSkins")]
    public ModDetail ArrangeSkins(ModArrangeSkinsParams p) =>
        Edit(p.Id, p.Revision, mod => mod.ArrangeSkins(p.Order.Select(s => new SkinPlace(s.Id, s.Group)).ToList()), "arranged its skins");

    [RpcMethod("mods.delete")]
    public ModsListResult Delete(ModDeleteParams p)
    {
        var (ws, install) = session.Current();
        var removed = ModProject.Delete(ws, install, p.Id, p.RemoveFromGame, Game);
        session.Log($"Deleted '{p.Id}': {string.Join(", ", removed.Recycled)} went to the Recycle Bin.");
        foreach (var line in removed.Kept) session.Log($"'{p.Id}': {line} stayed.");
        return ListOf(ws, install);
    }
}
