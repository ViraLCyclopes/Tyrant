using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Species;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class BuiltInAssetsTests
{
    private static readonly AssetRecord Fence = new("@data/sharedassets0.assets", 11, "Texture2D", "T_Fence_Adobe_D", null, null, null);
    private static readonly AssetRecord FenceCopy = new("@data/sharedassets3.assets", 12, "Texture2D", "T_Fence_Adobe_D", null, null, null);

    private static void Png(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var s = File.Create(path);
        new StbImageWriteSharp.ImageWriter().WritePng(new byte[16], 2, 2, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, s);
    }

    private static (FakeGame Game, Workspace Ws, ModProject Mod) Setup()
    {
        var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), new GameInstall(game.Root, null));
        return (game, ws, ModProject.Create(ws, "fences", null, null));
    }

    [Fact]
    public void Each_built_in_file_is_its_own_group()
    {
        Assert.Equal("@data/sharedassets0.assets", SpeciesCatalog.BundleGroup(Fence.Bundle));
        Assert.Equal("StandaloneWindows64/carch_assets_assets", SpeciesCatalog.BundleGroup("StandaloneWindows64/carch_assets_assets/x.bundle"));
    }

    [Fact]
    public void Replacing_a_built_in_texture_writes_its_name_only()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        var png = Path.Combine(ws.Dir, "fence.png");
        Png(png);

        var entry = mod.Replace(ws, new AssetIndex { Assets = [Fence] }, Fence.Ref, png);

        Assert.Equal(("T_Fence_Adobe_D", null as string, null as string), (entry.Texture, entry.Key, entry.Guid));
    }

    [Fact]
    public void Replacing_by_a_name_found_in_several_files_works()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        var png = Path.Combine(ws.Dir, "fence.png");
        Png(png);

        var entry = mod.Replace(ws, new AssetIndex { Assets = [Fence, FenceCopy] }, "T_Fence_Adobe_D", png);

        Assert.Equal("T_Fence_Adobe_D", entry.Texture);
    }

    [Fact]
    public void A_texture_name_in_several_files_is_a_warning_naming_them()
    {
        var (game, _, mod) = Setup();
        using var _ = game;
        Png(Path.Combine(mod.Dir, "textures", "f.png"));
        mod.Manifest.Replace.Add(new TextureReplacement { Texture = "T_Fence_Adobe_D", File = "textures/f.png" });
        mod.Save();

        var result = new ModChecker(_ => (2, 2)).Check(mod, new AssetIndex { Assets = [Fence, FenceCopy] });

        Assert.Empty(result.Errors);
        Assert.Contains(result.Warnings, w => w.Contains("T_Fence_Adobe_D") && w.Contains("sharedassets0.assets") && w.Contains("sharedassets3.assets"));
    }
}
