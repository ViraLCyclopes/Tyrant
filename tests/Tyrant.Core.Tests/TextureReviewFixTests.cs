using System.IO.Compression;
using StbImageWriteSharp;
using Tyrant.Core.Dumping;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Textures;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

/// <summary>Findings of the final texture-cooking review: path confinement, clearing overrides, the size limit, and the export and prune seams.</summary>
public class TextureReviewFixTests
{
    private static (FakeGame Game, GameInstall Install, Workspace Ws, ModProject Mod) Setup()
    {
        var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), install);
        Directory.CreateDirectory(Path.Combine(game.Root, "Mods"));
        File.WriteAllText(Path.Combine(game.Root, "Mods", ModLoaderInstaller.FrameworkFile), "framework");
        Directory.CreateDirectory(ModLoaderInstaller.RecordDir(install));
        File.WriteAllText(Path.Combine(ModLoaderInstaller.RecordDir(install), "install.json"), """{"installedLoader":true,"files":[],"createdDirs":[]}""");
        var mod = ModEditTests.WithSkins(ws, ModProject.Create(ws, "red-spot", "Red spot", null));
        return (game, install, ws, mod);
    }

    private static void WritePng(string path, int width, int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var rgba = new byte[width * height * 4];
        Array.Fill(rgba, (byte)120);
        using var stream = File.Create(path);
        new ImageWriter().WritePng(rgba, width, height, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
    }

    [Fact]
    public void Paths_outside_the_mod_folder_are_not_opened_for_status_or_sizes()
    {
        var (game, install, ws, mod) = Setup();
        using var _ = game;
        var cooker = ModTextureCooker.For(ws, null, null);
        var installed = Path.Combine(ModLoaderInstaller.ModsDir(install), mod.Id);
        new GameMods().Install(install, mod, staging => cooker.WritePack(mod.Manifest, staging, staging));
        var outside = Path.Combine(ws.Dir, "outside.png");
        WritePng(outside, 8, 8); // a real PNG next to the mod: opening it would show up as "stale" / an estimate
        var manifest = ModProject.Open(ws, mod.Id).Manifest;
        manifest.Skins.Add(new SkinEntry { Id = "evil", Species = "Carcharodontosaurus", Male = new()
        {
            ["diffuse"] = @"\host\share\a.png", ["normal"] = outside, ["extra"] = @"..\..\outside.png", ["pattern"] = "bad\0name.png",
        } });

        Assert.Equal("current", CookedPacks.StateOf(installed, manifest));
        var files = cooker.Estimate(manifest, installed).Select(e => e.File).ToList();
        Assert.DoesNotContain(files, f => f.Contains("host") || f.Contains("outside") || f.Contains('\0'));
        Assert.Contains("skins/blue/male_D.png", files);
    }

    [Fact]
    public void Clearing_a_compression_override_works_for_a_file_that_is_not_a_texture_any_more()
    {
        var (game, _, ws, mod) = Setup();
        using var _ = game;
        mod.Manifest.Textures.Overrides["skins/gone.png"] = TextureCompression.Bc7; // as a hand-edited mod.json could have it

        mod.SetCompression("skins/gone.png", null);

        Assert.Empty(ModProject.Open(ws, mod.Id).Manifest.Textures.Overrides);
        Assert.Equal(TyrantErrorCode.TargetNotFound, Assert.Throws<TyrantException>(() => mod.SetCompression("skins/gone.png", null)).Code);
        Assert.Equal(TyrantErrorCode.TargetNotFound, Assert.Throws<TyrantException>(() => mod.SetCompression("skins/gone.png", TextureCompression.Bc7)).Code);
    }

    [Fact]
    public void A_texture_over_the_pack_size_limit_fails_check_and_cooking_naming_the_file()
    {
        var (game, _, ws, mod) = Setup();
        using var _ = game;
        var wide = TexturePack.MaxSide + 1;
        WritePng(Path.Combine(mod.Dir, "skins", "blue", "male_D.png"), wide, 1);

        var result = new ModChecker(_ => null).Check(mod, null, null);
        Assert.Contains(result.Errors, e => e.Contains("male_D.png") && e.Contains("16384"));

        var cooker = ModTextureCooker.For(ws, null, null);
        var ex = Assert.Throws<TyrantException>(() => cooker.WritePack(mod.Manifest, mod.Dir, Path.Combine(ws.Dir, "out")));
        Assert.Contains("male_D.png", ex.Message);
    }

    [Fact]
    public void An_exported_zip_unzipped_into_the_game_has_a_current_pack()
    {
        var (game, install, ws, mod) = Setup();
        using var _ = game;
        var cooker = ModTextureCooker.For(ws, null, null);
        var zip = Path.Combine(ws.Dir, "exports", "red-spot.zip");
        ModSharing.Export(mod, zip, "0.3.0", dir => cooker.WritePack(mod.Manifest, mod.Dir, dir));

        ZipFile.ExtractToDirectory(zip, game.Root); // extraction stamps files with the zip's times, which must not matter

        Assert.Equal("current", CookedPacks.StateOf(Path.Combine(ModLoaderInstaller.ModsDir(install), mod.Id), mod.Manifest));
    }

    [Fact]
    public void Removing_a_skin_or_a_replacement_drops_their_compression_overrides()
    {
        var (game, _, ws, mod) = Setup();
        using var _ = game;
        mod.Manifest.Replace.Add(new TextureReplacement { Texture = "Fern", File = "textures/Fern.png" });
        mod.SetCompression("skins/blue/female_D.png", TextureCompression.Bc7);
        mod.SetCompression("textures/Fern.png", TextureCompression.Bc7);
        Assert.Equal(2, ModProject.Open(ws, mod.Id).Manifest.Textures.Overrides.Count);

        mod.RemoveReplacement("Fern");
        Assert.Equal(["skins/blue/female_D.png"], ModProject.Open(ws, mod.Id).Manifest.Textures.Overrides.Keys);

        mod.RemoveSkin("blue", deleteFiles: false);
        Assert.Empty(ModProject.Open(ws, mod.Id).Manifest.Textures.Overrides);
    }
}
