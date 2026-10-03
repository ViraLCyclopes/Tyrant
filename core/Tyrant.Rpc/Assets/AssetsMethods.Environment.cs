using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Rpc.Protocol;

namespace Tyrant.Rpc.Assets;

/// <summary>assets.environments / assets.environment — the game's own ground and sky textures for the 3D preview.</summary>
public sealed partial class AssetsMethods
{
    [RpcMethod("assets.environments")]
    public EnvironmentList Environments() => new(OptionsOf(EnvironmentKind.Ground), OptionsOf(EnvironmentKind.Sky));

    [RpcMethod("assets.environment")]
    public EnvironmentTexture GetEnvironment(EnvironmentParams p)
    {
        var preset = EnvironmentPresets.Find(p.Id); // an unknown id is invalid params
        var (ws, install, index) = Open();
        return _previews.GetOrCreate(ws, GameFingerprint.Compute(install), $"environment:{preset.Id}", preset.ObjectName,
            dir => new EnvironmentTexture(preset.Id, preset.Kind == EnvironmentKind.Sky ? "sky" : "ground", Reader.WriteEnvironment(install, index, preset, dir)));
    }

    private static IReadOnlyList<EnvironmentOption> OptionsOf(EnvironmentKind kind) =>
        EnvironmentPresets.All.Where(p => p.Kind == kind).Select(p => new EnvironmentOption(p.Id, p.Label)).ToList();
}
