using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class ModProjectTests
{
    private const string Key = "Assets/Art/Animals/Dinosaurs/Carcharodontosaurus/Textures/T_carcharodontosaurus_alt1_male_D.png";

    private static readonly AssetRecord Texture = new("StandaloneWindows64/carch_assets_assets/t_carch_d.bundle", 7, "Texture2D",
        "T_carcharodontosaurus_alt1_male_D", Key, "e3583acd2b3b5b14c875f42d110d97ce", null);

    private static (FakeGame Game, Workspace Ws) Setup()
    {
        var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), new GameInstall(game.Root, null));
        return (game, ws);
    }

    private static string Png(string dir, string name = "spots.png")
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        using var stream = File.Create(path);
        new StbImageWriteSharp.ImageWriter().WritePng(new byte[4 * 4 * 4], 4, 4, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        return path;
    }

    private static AssetIndex Index() => new() { Assets = [Texture, Texture with { PathId = 8, Type = "Sprite" }] };

    [Fact]
    public void Create_writes_the_manifest_in_the_workspace_mods_folder()
    {
        var (game, ws) = Setup();
        using var _ = game;

        var mod = ModProject.Create(ws, "red-spot-carcharo", "Red-spot Carcharodontosaurus", "you");

        Assert.Equal(Path.Combine(ws.Dir, "mods", "red-spot-carcharo"), mod.Dir);
        var manifest = ModManifest.Parse(File.ReadAllText(Path.Combine(mod.Dir, "mod.json")));
        Assert.Equal(("red-spot-carcharo", "Red-spot Carcharodontosaurus", "you"), (manifest.Id, manifest.Name, manifest.Author));
        Assert.Equal(new[] { "red-spot-carcharo" }, ModProject.Ids(ws));
    }

    [Theory]
    [InlineData("Red Spot")]
    [InlineData("ab")]
    public void Invalid_ids_are_refused(string id)
    {
        var (game, ws) = Setup();
        using var _ = game;

        var ex = Assert.Throws<TyrantException>(() => ModProject.Create(ws, id, null, null));

        Assert.Equal(TyrantErrorCode.ModIdInvalid, ex.Code);
    }

    [Fact]
    public void An_id_that_is_taken_is_refused()
    {
        var (game, ws) = Setup();
        using var _ = game;
        ModProject.Create(ws, "red-spot", null, null);

        var ex = Assert.Throws<TyrantException>(() => ModProject.Create(ws, "red-spot", null, null));

        Assert.Equal(TyrantErrorCode.ModIdInvalid, ex.Code);
    }

    [Theory]
    [InlineData("T_carcharodontosaurus_alt1_male_D")]
    [InlineData("t_CARCHARODONTOSAURUS_alt1_male_d")]
    [InlineData(Key)]
    [InlineData("e3583acd2b3b5b14c875f42d110d97ce")]
    public void Replace_finds_the_texture_by_name_key_or_guid_and_copies_the_png(string texture)
    {
        var (game, ws) = Setup();
        using var _ = game;
        var mod = ModProject.Create(ws, "red-spot", null, null);

        var entry = mod.Replace(ws, Index(), texture, Png(Path.Combine(ws.Dir, "edits")));

        Assert.Equal(("T_carcharodontosaurus_alt1_male_D", Key, "e3583acd2b3b5b14c875f42d110d97ce", "textures/T_carcharodontosaurus_alt1_male_D.png"),
            (entry.Texture, entry.Key, entry.Guid, entry.File));
        Assert.True(File.Exists(Path.Combine(mod.Dir, "textures", "T_carcharodontosaurus_alt1_male_D.png")));
        Assert.Single(ModProject.Open(ws, "red-spot").Manifest.Replace);
    }

    [Fact]
    public void Replacing_the_same_texture_again_updates_the_entry()
    {
        var (game, ws) = Setup();
        using var _ = game;
        var mod = ModProject.Create(ws, "red-spot", null, null);
        mod.Replace(ws, Index(), Texture.Name, Png(Path.Combine(ws.Dir, "edits"), "a.png"));

        mod.Replace(ws, Index(), Texture.Name, Png(Path.Combine(ws.Dir, "edits"), "b.png"));

        Assert.Single(ModProject.Open(ws, "red-spot").Manifest.Replace);
    }

    [Fact]
    public void Without_a_png_the_exported_texture_is_used()
    {
        var (game, ws) = Setup();
        using var _ = game;
        var exported = TextureExporter.OutputPathFor(Texture, ws.AssetsDir);
        Png(Path.GetDirectoryName(exported)!, Path.GetFileName(exported));
        var mod = ModProject.Create(ws, "red-spot", null, null);

        mod.Replace(ws, Index(), Texture.Name, null);

        Assert.True(File.Exists(Path.Combine(mod.Dir, "textures", "T_carcharodontosaurus_alt1_male_D.png")));
    }

    [Fact]
    public void Unknown_textures_and_non_png_files_are_refused()
    {
        var (game, ws) = Setup();
        using var _ = game;
        var mod = ModProject.Create(ws, "red-spot", null, null);
        var notPng = Path.Combine(ws.Dir, "notes.png");
        File.WriteAllText(notPng, "hello");

        Assert.Equal(TyrantErrorCode.TargetNotFound, Assert.Throws<TyrantException>(() => mod.Replace(ws, Index(), "T_Nope_D", Png(ws.Dir))).Code);
        Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() => mod.Replace(ws, Index(), Texture.Name, notPng)).Code);
        Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() => mod.Replace(ws, Index(), Texture.Name, null)).Code); // nothing exported
    }

    [Fact]
    public void Opening_a_missing_or_broken_mod_says_so()
    {
        var (game, ws) = Setup();
        using var _ = game;
        Directory.CreateDirectory(Path.Combine(ws.Dir, "mods", "broken"));
        File.WriteAllText(Path.Combine(ws.Dir, "mods", "broken", "mod.json"), "{");

        Assert.Equal(TyrantErrorCode.ModNotFound, Assert.Throws<TyrantException>(() => ModProject.Open(ws, "nope")).Code);
        Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() => ModProject.Open(ws, "broken")).Code);
    }
}
