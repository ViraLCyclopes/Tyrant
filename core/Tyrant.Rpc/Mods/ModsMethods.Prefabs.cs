using System.Text.Json;
using Tyrant.Core.Mods;
using Tyrant.Rpc.Protocol;

namespace Tyrant.Rpc.Mods;

public sealed record ModPrefabFieldDto(string Object, string Component, string Field, string Kind, string GameValue, string? Value, bool Editable,
    IReadOnlyList<string>? EnumNames, IReadOnlyList<long>? EnumValues);
public sealed record ModPrefabFields(string Target, IReadOnlyList<ModPrefabFieldDto> Fields);
public sealed record ModPrefabTargetDto(string Target, string File, int Fields);
public sealed record ModPrefabFieldsParams(string Id, string Target);
public sealed record ModSetPrefabFieldParams(string Id, string Revision, string Target, string Object, string Component, string Field, JsonElement Value);
public sealed record ModResetPrefabFieldParams(string Id, string Revision, string Target, string Object, string Component, string Field);
public sealed record ModRemovePrefabTargetParams(string Id, string Revision, string Target);
public sealed record ModRestoreSnapshotParams(string Id, string Revision, string Manifest, IReadOnlyDictionary<string, string> Prefabs);

/// <summary>mods.prefabFields and the prefab settings edits; mods.restoreSnapshot for undo and redo.</summary>
public sealed partial class ModsMethods
{
    [RpcMethod("mods.prefabFields")]
    public ModPrefabFields PrefabFieldsOf(ModPrefabFieldsParams p)
    {
        var (ws, install) = session.Current();
        var mod = ModProject.Open(ws, p.Id);
        var tree = TreeOf(ws, install, mod, p.Target);
        var overrides = mod.PrefabTargets().FirstOrDefault(t => string.Equals(t.Overrides.Target, p.Target, StringComparison.OrdinalIgnoreCase)).Overrides;
        string? ValueOf(PrefabFieldInfo f) =>
            overrides?.Fields.FirstOrDefault(o => o.Object == f.Object && o.Component == f.Component && o.Field == f.Field) is { } o ? JsonSerializer.Serialize(JsonDocument.Parse(Tyrant.Framework.Core.Json.Write(o.Value)).RootElement) : null;
        return new ModPrefabFields(p.Target, tree.Select(f => new ModPrefabFieldDto(f.Object, f.Component, f.Field, f.Kind, f.Value, ValueOf(f), f.Editable, f.EnumNames, f.EnumValues)).ToList());
    }

    [RpcMethod("mods.setPrefabField")]
    public ModDetail SetPrefabField(ModSetPrefabFieldParams p)
    {
        var (ws, install) = session.Current();
        var tree = TreeOf(ws, install, ModProject.Open(ws, p.Id), p.Target); // before the edit opens with the revision check
        return Edit(p.Id, p.Revision, mod => mod.SetPrefabField(p.Target, p.Object, p.Component, p.Field, p.Value, tree), $"set {p.Component}.{p.Field} of {p.Target}");
    }

    [RpcMethod("mods.resetPrefabField")]
    public ModDetail ResetPrefabField(ModResetPrefabFieldParams p) =>
        Edit(p.Id, p.Revision, mod => mod.ResetPrefabField(p.Target, p.Object, p.Component, p.Field), $"reset {p.Component}.{p.Field} of {p.Target}");

    [RpcMethod("mods.removePrefabTarget")]
    public ModDetail RemovePrefabTarget(ModRemovePrefabTargetParams p) =>
        Edit(p.Id, p.Revision, mod => mod.RemovePrefabTarget(p.Target), $"removed the prefab settings of {p.Target}");

    [RpcMethod("mods.restoreSnapshot")]
    public ModDetail RestoreSnapshot(ModRestoreSnapshotParams p)
    {
        var (ws, _) = session.Current();
        var mod = ModProject.RestoreSnapshot(ws, p.Id, p.Manifest, p.Prefabs, p.Revision);
        session.Log($"'{p.Id}': put back to an earlier version (undo/redo).");
        return DetailOf(ws, mod);
    }

    private IReadOnlyList<PrefabFieldInfo> TreeOf(Tyrant.Core.Workspaces.Workspace ws, Tyrant.Core.Install.GameInstall install, ModProject mod, string target) =>
        PrefabFields.Read(ws, install, RequireIndex(ws), ModProject.WithSpecies(RequireSpecies(ws), mod.Manifest), Options.AssetReader, target);
}
