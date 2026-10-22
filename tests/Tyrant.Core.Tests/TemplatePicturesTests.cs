using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Tests;

public class TemplatePicturesTests
{
    private static (FakeGame Game, Workspace Ws, GameInstall Install, FakeAssetReader Reader) Setup(bool dump = true, bool index = true)
    {
        var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), install);
        if (dump) PictureDumps.Write(ws.DataDir);
        if (index) PictureDumps.Index().Save(AssetIndex.PathIn(ws));
        return (game, ws, install, PictureDumps.Reader());
    }

    private static string Out(Workspace ws, string name = "picture.png") => Path.Combine(Path.GetDirectoryName(ws.Dir)!, "pictures", name);

    [Fact]
    public void The_nursery_picture_is_the_hero_sprites_texture_by_reference()
    {
        var (game, ws, install, reader) = Setup();
        using var _ = game;

        var saved = TemplatePictures.Save(ws, install, reader, "Allosaurus Anax", TemplatePictureKind.Nursery, Out(ws));

        Assert.Equal([PictureDumps.AlloHeroTexture.Ref], reader.TexturesWritten);
        Assert.Equal("Hero_Allosaurus_A_packed", saved.Texture);
        Assert.Equal((512, 256), (saved.Width, saved.Height));
        Assert.Equal("Allosaurus Anax", saved.Species);
        Assert.Equal(Path.GetFullPath(Out(ws)), saved.Path);
        Assert.True(File.Exists(saved.Path));
    }

    [Fact]
    public void A_hero_sprite_without_a_texture_reference_uses_the_texture_of_its_name()
    {
        var (game, ws, install, reader) = Setup();
        using var _ = game;

        var saved = TemplatePictures.Save(ws, install, reader, "Parasaurolophus Cyrtocristatus", TemplatePictureKind.Nursery, Out(ws));

        Assert.Equal([PictureDumps.ParaHeroTexture.Ref], reader.TexturesWritten);
        Assert.Equal("Hero_Parasaurolophus_C", saved.Texture);
    }

    [Fact]
    public void The_paleopedia_picture_is_found_through_the_info_signs_background_image()
    {
        var (game, ws, install, reader) = Setup();
        using var _ = game;

        var saved = TemplatePictures.Save(ws, install, reader, "Allosaurus Anax", TemplatePictureKind.Paleopedia, Out(ws));

        Assert.Equal([PictureDumps.AlloPicture.Ref], reader.TexturesWritten);
        Assert.Equal("Allosaurus Anax", saved.Texture);
        Assert.Equal((2048, 1152), (saved.Width, saved.Height));
    }

    [Fact]
    public void A_paleopedia_texture_named_unlike_its_species_is_still_found_by_reference()
    {
        var (game, ws, install, reader) = Setup();
        using var _ = game;

        var saved = TemplatePictures.Save(ws, install, reader, "parasaurolophus cyrtocristatus", TemplatePictureKind.Paleopedia, Out(ws));

        Assert.Equal([PictureDumps.ParaPicture.Ref], reader.TexturesWritten); // in sharedassets1, not the same-named decoy
        Assert.Equal("Parasarulophus Cyrtocristatus", saved.Texture);
        Assert.Equal("Parasaurolophus Cyrtocristatus", saved.Species);
    }

    [Fact]
    public void A_path_without_an_extension_gets_png()
    {
        var (game, ws, install, reader) = Setup();
        using var _ = game;

        var saved = TemplatePictures.Save(ws, install, reader, "Allosaurus Anax", TemplatePictureKind.Nursery, Out(ws, "allo"));

        Assert.EndsWith("allo.png", saved.Path);
        Assert.True(File.Exists(saved.Path));
    }

    [Fact]
    public void Without_the_data_dump_it_asks_for_the_dump()
    {
        var (game, ws, install, reader) = Setup(dump: false);
        using var _ = game;

        var ex = Assert.Throws<TyrantException>(() => TemplatePictures.Save(ws, install, reader, "Allosaurus Anax", TemplatePictureKind.Nursery, Out(ws)));

        Assert.Equal(TyrantErrorCode.DataMissing, ex.Code);
        Assert.Contains("data dump", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(reader.TexturesWritten);
    }

    [Fact]
    public void Without_the_asset_index_it_asks_to_index_the_assets()
    {
        var (game, ws, install, reader) = Setup(index: false);
        using var _ = game;

        var ex = Assert.Throws<TyrantException>(() => TemplatePictures.Save(ws, install, reader, "Allosaurus Anax", TemplatePictureKind.Paleopedia, Out(ws)));

        Assert.Equal(TyrantErrorCode.AssetIndexMissing, ex.Code);
        Assert.Contains("Index the assets", ex.Message);
    }

    [Fact]
    public void A_species_the_dump_lacks_is_named()
    {
        var (game, ws, install, reader) = Setup();
        using var _ = game;

        var ex = Assert.Throws<TyrantException>(() => TemplatePictures.Save(ws, install, reader, "Nopesaurus", TemplatePictureKind.Nursery, Out(ws)));

        Assert.Equal(TyrantErrorCode.TargetNotFound, ex.Code);
        Assert.Contains("'Nopesaurus'", ex.Message);
    }

    [Fact]
    public void A_custom_species_is_pointed_at_its_template()
    {
        var (game, ws, install, reader) = Setup();
        using var _ = game;

        var ex = Assert.Throws<TyrantException>(() => TemplatePictures.Save(ws, install, reader, "tyrant.red-spot.mirabilis", TemplatePictureKind.Nursery, Out(ws)));

        Assert.Equal(TyrantErrorCode.TargetNotFound, ex.Code);
        Assert.Contains("template", ex.Message);
    }

    [Fact]
    public void A_picture_the_index_cannot_lead_to_says_to_index_again()
    {
        var (game, ws, install, reader) = Setup(index: false);
        using var _ = game;
        new AssetIndex { Assets = PictureDumps.Records.Where(r => r != PictureDumps.ParaSign).ToList() }.Save(AssetIndex.PathIn(ws));

        var ex = Assert.Throws<TyrantException>(() => TemplatePictures.Save(ws, install, reader, "Parasaurolophus Cyrtocristatus", TemplatePictureKind.Paleopedia, Out(ws)));

        Assert.Equal(TyrantErrorCode.AssetNotFound, ex.Code);
        Assert.Contains("Paleopedia picture", ex.Message);
        Assert.Contains("index the assets again", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(reader.TexturesWritten);
    }

    [Fact]
    public void The_pictures_are_never_saved_into_a_mod_or_the_game_folder()
    {
        var (game, ws, install, reader) = Setup();
        using var _ = game;

        var intoMod = Assert.Throws<TyrantException>(() => TemplatePictures.Save(ws, install, reader, "Allosaurus Anax", TemplatePictureKind.Nursery,
            Path.Combine(ModProject.RootOf(ws), "red-spot", "icons", "allo.png")));
        var intoGame = Assert.Throws<TyrantException>(() => TemplatePictures.Save(ws, install, reader, "Allosaurus Anax", TemplatePictureKind.Nursery,
            Path.Combine(install.RootDir, "allo.png")));

        Assert.Contains("not to be shared", intoMod.Message);
        Assert.Equal(TyrantErrorCode.OutputInGameFolder, intoGame.Code);
        Assert.Empty(reader.TexturesWritten);
    }
}
