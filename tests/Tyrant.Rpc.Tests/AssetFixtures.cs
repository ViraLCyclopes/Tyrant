using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Tests;

namespace Tyrant.Rpc.Tests;

/// <summary>A tiny synthetic asset index (names only — no game content).</summary>
public static class AssetFixtures
{
    public static readonly AssetRecord StegoD = new("animals/stego_assets_assets/textures.bundle", 1, "Texture2D", "T_Stego_D",
        "Assets/Art/Animals/Dinosaurs/Stegosaurus/Textures/T_Stego_D.png", "0123456789abcdef0123456789abcdef", null);
    public static readonly AssetRecord StegoN = new("animals/stego_assets_assets/textures.bundle", 2, "Texture2D", "T_Stego_N",
        "Assets/Art/Animals/Dinosaurs/Stegosaurus/Textures/T_Stego_N.png", null, null);
    public static readonly AssetRecord StegoPrefab = new("animals/stego_assets_assets/prefabs.bundle", 3, "GameObject", "Stegosaurus",
        "Assets/Prefabs/Animals/V2-MainPrefabs/Stegosaurus.V2.prefab", null, null);
    public static readonly AssetRecord RexD = new("animals/rex_assets_assets/textures.bundle", 4, "Texture2D", "T_Rex_D",
        "Assets/Art/Animals/Dinosaurs/Rex/Textures/T_Rex_D.png", null, null);
    public static readonly AssetRecord Menu = new("ui_assets_all.bundle", 5, "MonoBehaviour", "Menu", null, null, "MenuScript");
    public static readonly AssetRecord[] All = [StegoD, StegoN, StegoPrefab, RexD, Menu];

    public static async Task<(RpcHarness Harness, string Workspace, FakeAssetReader Reader)> Opened(FakeGame game,
        IEnumerable<AssetRecord>? assets = null, GameFingerprint? fingerprint = null)
    {
        var reader = new FakeAssetReader();
        var harness = new RpcHarness(TestStudio.Options(reader: reader));
        var ws = TestStudio.TempDir();
        await harness.Call("workspace.create", new { dir = ws, gamePath = game.Root });
        WriteIndex(ws, assets ?? All, fingerprint ?? GameFingerprint.Compute(new GameInstall(game.Root, null)));
        return (harness, ws, reader);
    }

    public static void WriteIndex(string ws, IEnumerable<AssetRecord> assets, GameFingerprint? fingerprint) =>
        new AssetIndex { Assets = assets.ToList(), Fingerprint = fingerprint }.Save(Path.Combine(ws, "cache", AssetIndex.FileName));
}
