using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class MaskExportsTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ---- names ----

    [Fact]
    public void A_skin_export_is_named_after_the_species_the_skin_and_the_kind()
    {
        Assert.Equal("Tyrannosaurus Leatherhide fur mask.png", MaskExports.FileName(LookScope.Skin, "Tyrannosaurus", "Leatherhide", "Rexy", ExportKind.FurMask));
        Assert.Equal("Tyrannosaurus Leatherhide iridescence mask.png", MaskExports.FileName(LookScope.Skin, "Tyrannosaurus", "Leatherhide", null, ExportKind.IridescenceMask));
        Assert.Equal("Tyrannosaurus Leatherhide baby fur mask.png", MaskExports.FileName(LookScope.Skin, "Tyrannosaurus", "Leatherhide", null, ExportKind.BabyFurMask));
        Assert.Equal("Tyrannosaurus Leatherhide colours.png", MaskExports.FileName(LookScope.Skin, "Tyrannosaurus", "Leatherhide", null, ExportKind.Colours));
    }

    [Fact]
    public void An_animal_export_adds_the_animal_s_name_when_it_has_one()
    {
        Assert.Equal("Tyrannosaurus Leatherhide Rexy fur mask.png", MaskExports.FileName(LookScope.Animal, "Tyrannosaurus", "Leatherhide", "Rexy", ExportKind.FurMask));
        Assert.Equal("Tyrannosaurus Leatherhide fur mask.png", MaskExports.FileName(LookScope.Animal, "Tyrannosaurus", "Leatherhide", null, ExportKind.FurMask));
        Assert.Equal("Tyrannosaurus Leatherhide fur mask.png", MaskExports.FileName(LookScope.Animal, "Tyrannosaurus", "Leatherhide", "  ", ExportKind.FurMask));
    }

    [Fact]
    public void A_species_export_uses_only_the_species_name()
    {
        Assert.Equal("Tyrannosaurus colours.png", MaskExports.FileName(LookScope.Species, "Tyrannosaurus", "Leatherhide", "Rexy", ExportKind.Colours));
    }

    [Fact]
    public void A_missing_skin_name_is_left_out()
    {
        Assert.Equal("Tyrannosaurus Rexy fur mask.png", MaskExports.FileName(LookScope.Animal, "Tyrannosaurus", null, "Rexy", ExportKind.FurMask));
        Assert.Equal("Tyrannosaurus fur mask.png", MaskExports.FileName(LookScope.Skin, "Tyrannosaurus", "", null, ExportKind.FurMask));
    }

    [Fact]
    public void Characters_a_file_name_cannot_hold_are_replaced()
    {
        Assert.Equal("T_ rex Red_Blue _Big_ fur mask.png", MaskExports.FileName(LookScope.Animal, "T: rex", "Red/Blue", "\"Big\"", ExportKind.FurMask));
        Assert.Equal("A_B_C_D_E_F_G colours.png", MaskExports.FileName(LookScope.Species, "A<B>C|D?E*F\\G", null, null, ExportKind.Colours));
        Assert.Equal("Tab Name colours.png", MaskExports.FileName(LookScope.Skin, "Tab\t", " Name.", null, ExportKind.Colours)); // control characters, trailing dots and spaces
    }

    [Fact]
    public void A_name_already_taken_gets_the_next_number()
    {
        Assert.Equal("Rex fur mask.png", MaskExports.Unique("Rex fur mask.png", new[] { "other.png" }));
        Assert.Equal("Rex fur mask 2.png", MaskExports.Unique("Rex fur mask.png", new[] { "Rex fur mask.png" }));
        Assert.Equal("Rex fur mask 3.png", MaskExports.Unique("Rex fur mask.png", new[] { "rex FUR mask.png", "Rex fur mask 2.png" })); // names in any case are taken
        Assert.Equal("Rex fur mask 2.png", MaskExports.Unique("Rex fur mask.png", new[] { "Rex fur mask.png", "Rex fur mask 3.png" })); // the first free one
    }

    [Fact]
    public void Each_kind_saves_into_a_mod_under_its_fixed_name()
    {
        Assert.Equal(LookEditing.IridescenceMaskFile, MaskExports.FixedName(ExportKind.IridescenceMask));
        Assert.Equal(LookEditing.FurMaskFile, MaskExports.FixedName(ExportKind.FurMask));
        Assert.Equal("fur-infant-mask.png", MaskExports.FixedName(ExportKind.BabyFurMask));
        Assert.Equal("iridescence-ramp.png", MaskExports.FixedName(ExportKind.Colours));
    }

    // ---- what Export mask PNG writes ----

    [Fact]
    public void Everywhere_exports_a_white_mask_even_with_a_texture()
    {
        Assert.Equal(MaskExportMode.White, MaskExports.IridescenceMode(new MaskSource { Everywhere = true }, hasTexture: true));
        Assert.Equal(MaskExportMode.White, MaskExports.IridescenceMode(new MaskSource { Everywhere = true }, hasTexture: false));
    }

    [Fact]
    public void No_mask_texture_from_the_game_exports_a_white_mask()
    {
        Assert.Equal(MaskExportMode.White, MaskExports.IridescenceMode(null, hasTexture: false));
        Assert.Equal(MaskExportMode.White, MaskExports.IridescenceMode(new MaskSource { Game = true }, hasTexture: false));
    }

    [Fact]
    public void A_mask_the_animal_shows_as_a_texture_exports_that_image()
    {
        Assert.Equal(MaskExportMode.Image, MaskExports.IridescenceMode(null, hasTexture: true));
        Assert.Equal(MaskExportMode.Image, MaskExports.IridescenceMode(new MaskSource { From = "extra", Channel = 'g' }, hasTexture: true));
        Assert.Equal(MaskExportMode.Image, MaskExports.IridescenceMode(new MaskSource { File = "x.png" }, hasTexture: true));
    }

    [Fact]
    public void A_channel_or_file_mask_not_read_yet_exports_nothing()
    {
        Assert.Equal(MaskExportMode.None, MaskExports.IridescenceMode(new MaskSource { From = "extra", Channel = 'r' }, hasTexture: false));
        Assert.Equal(MaskExportMode.None, MaskExports.IridescenceMode(new MaskSource { File = "x.png" }, hasTexture: false));
    }

    [Fact]
    public void A_species_mask_stays_a_reference()
    {
        Assert.Equal(MaskExportMode.SpeciesReference, MaskExports.IridescenceMode(new MaskSource { Species = "Microraptor" }, hasTexture: true));
        Assert.Equal(MaskExportMode.SpeciesReference, MaskExports.IridescenceMode(new MaskSource { Species = "Microraptor" }, hasTexture: false));
    }

    // ---- Export colours PNG ----

    [Fact]
    public void Colours_and_a_thin_film_export_their_ramp()
    {
        var stops = new RampSource { Stops = new List<Rgb> { new Rgb(1, 0, 0), new Rgb(0, 0, 1) } };
        Assert.Equal(ColoursExport.Ok, MaskExports.ColoursFrom(stops, out var colours));
        Assert.Equal(stops.Stops, colours);
        Assert.Equal(ColoursExport.Ok, MaskExports.ColoursFrom(new RampSource { ThinFilm = new ThinFilmSettings { Thickness = 420 } }, out var film));
        Assert.NotEmpty(film!);
    }

    [Fact]
    public void The_game_s_colours_a_species_or_a_png_are_not_exported()
    {
        Assert.Equal(ColoursExport.Game, MaskExports.ColoursFrom(null, out var none));
        Assert.Null(none);
        Assert.Equal(ColoursExport.Game, MaskExports.ColoursFrom(new RampSource { Game = true }, out _));
        Assert.Equal(ColoursExport.Species, MaskExports.ColoursFrom(new RampSource { Species = "Microraptor" }, out _));
        Assert.Equal(ColoursExport.File, MaskExports.ColoursFrom(new RampSource { File = "ramp.png" }, out _));
    }

    [Fact]
    public void A_ramp_strip_is_wider_than_it_is_tall_with_every_row_the_same()
    {
        var stops = new List<Rgb> { new Rgb(0, 0, 0), new Rgb(1, 1, 1) };
        var pixels = IridescenceRamp.Pixels(stops, MaskExports.RampWidth, MaskExports.RampHeight);
        Assert.Equal(256, MaskExports.RampWidth);
        Assert.Equal(8, MaskExports.RampHeight);
        Assert.Equal(256 * 8 * 4, pixels.Length);
        Assert.Equal(0, pixels[0]);
        Assert.Equal(255, pixels[255 * 4]);
        Assert.Equal(pixels.Take(256 * 4), pixels.Skip(7 * 256 * 4)); // the last row is the first
        Assert.Equal(IridescenceRamp.Pixels(stops), IridescenceRamp.Pixels(stops, 32, 32)); // the square one is unchanged
    }

    // ---- Save ----

    [Fact]
    public void Save_names_an_export_by_its_fixed_name_in_the_owner_s_folder_and_copies_it_there()
    {
        var masks = TempDir();
        var owner = Path.Combine(TempDir(), "materials", "skins", "rex");
        Directory.CreateDirectory(owner);
        File.WriteAllBytes(Path.Combine(owner, LookEditing.FurMaskFile), new byte[] { 1 }); // the saved look's own
        var fur = Path.Combine(masks, "Rex fur mask.png");
        File.WriteAllBytes(fur, new byte[] { 2 });
        var exports = new ExportedFiles();
        exports.Add(fur, ExportKind.FurMask);
        var block = new MaterialOverride { Fur = new FurMasks { Mask = new MaskSource { File = fur } } };

        var final = exports.Final(block, owner);
        Assert.Equal(Path.Combine(owner, LookEditing.FurMaskFile), final.Fur!.Mask!.File);
        Assert.Equal(fur, block.Fur!.Mask!.File); // the input is not changed
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(owner, LookEditing.FurMaskFile))); // naming copies nothing

        exports.Commit(block, owner);
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(Path.Combine(owner, LookEditing.FurMaskFile)));
        Assert.True(File.Exists(fur)); // the export stays in Masks
    }

    /// <summary>A fur mask export of <paramref name="bytes"/>, its exports and a block using it.</summary>
    private static (ExportedFiles Exports, MaterialOverride Block) FurExport(byte[] bytes)
    {
        var fur = Path.Combine(TempDir(), "Rex fur mask.png");
        File.WriteAllBytes(fur, bytes);
        var exports = new ExportedFiles();
        exports.Add(fur, ExportKind.FurMask);
        return (exports, new MaterialOverride { Fur = new FurMasks { Mask = new MaskSource { File = fur } } });
    }

    [Fact]
    public void A_save_that_fails_after_the_commit_puts_the_saved_look_s_image_back()
    {
        var owner = TempDir();
        var target = Path.Combine(owner, LookEditing.FurMaskFile);
        File.WriteAllBytes(target, new byte[] { 1 }); // the saved look's own
        var (exports, block) = FurExport(new byte[] { 2 });

        var commit = exports.Commit(block, owner);
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(target));
        commit.Undo(); // e.g. writing materials.json failed
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(target));
        Assert.Equal(new[] { target }, Directory.GetFiles(owner)); // no backup left behind
    }

    [Fact]
    public void A_save_that_goes_through_keeps_the_new_image_and_no_backup()
    {
        var owner = TempDir();
        var target = Path.Combine(owner, LookEditing.FurMaskFile);
        File.WriteAllBytes(target, new byte[] { 1 });
        var (exports, block) = FurExport(new byte[] { 2 });

        exports.Commit(block, owner).Keep();
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(target));
        Assert.Equal(new[] { target }, Directory.GetFiles(owner));
    }

    [Fact]
    public void A_first_save_writes_the_fixed_name_and_undoing_it_removes_it()
    {
        var owner = Path.Combine(TempDir(), "materials", "skins", "rex"); // not made yet: the mod has no image of its own
        var target = Path.Combine(owner, LookEditing.FurMaskFile);
        var (exports, block) = FurExport(new byte[] { 2 });

        var commit = exports.Commit(block, owner);
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(target));
        Assert.Equal(new[] { target }, Directory.GetFiles(owner)); // no temp file left behind
        commit.Undo();
        Assert.False(File.Exists(target));

        exports.Commit(block, owner).Keep();
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(target));
    }

    [Fact]
    public void Only_an_export_in_its_own_slot_takes_the_fixed_name()
    {
        var masks = TempDir();
        var owner = TempDir();
        var iridescence = Path.Combine(masks, "Rex iridescence mask.png");
        var picked = Path.Combine(masks, "spots.png");
        File.WriteAllBytes(iridescence, new byte[] { 1 });
        File.WriteAllBytes(picked, new byte[] { 2 });
        var exports = new ExportedFiles();
        exports.Add(iridescence, ExportKind.IridescenceMask);
        var block = new MaterialOverride
        {
            Iridescence = new IridescenceSettings { Mask = new MaskSource { File = picked } },
            Fur = new FurMasks { Mask = new MaskSource { File = iridescence } }, // the iridescence export picked as the fur mask
        };

        var final = exports.Final(block, owner);
        Assert.Equal(picked, final.Iridescence!.Mask!.File);
        Assert.Equal(iridescence, final.Fur!.Mask!.File);
        exports.Commit(block, owner);
        Assert.Empty(Directory.GetFiles(owner));
    }

    [Fact]
    public void Colours_save_as_the_ramp_and_forgetting_keeps_the_files()
    {
        var masks = TempDir();
        var owner = TempDir();
        var ramp = Path.Combine(masks, "Rex colours.png");
        File.WriteAllBytes(ramp, new byte[] { 3 });
        var exports = new ExportedFiles();
        exports.Add(ramp, ExportKind.Colours);
        var block = new MaterialOverride { Iridescence = new IridescenceSettings { Ramp = new RampSource { File = ramp } } };
        Assert.Equal(Path.Combine(owner, "iridescence-ramp.png"), exports.Final(block, owner).Iridescence!.Ramp!.File);

        exports.Forget(); // Reset, Discard or a new draft: nothing is deleted, the file is the player's
        Assert.True(File.Exists(ramp));
        Assert.Equal(ramp, exports.Final(block, owner).Iridescence!.Ramp!.File);
        exports.Commit(block, owner);
        Assert.Empty(Directory.GetFiles(owner));
    }
}
