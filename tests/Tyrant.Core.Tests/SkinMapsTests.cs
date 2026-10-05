using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class SkinMapsTests
{
    private static IReadOnlyList<SpeciesSkins> Species()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "data");
        SkinDumps.Write(dir);
        return SpeciesSkinsReader.Read(Tyrant.Core.Data.DataStore.OpenDirectory(dir));
    }

    [Fact]
    public void Picks_a_vanilla_skin_by_name_or_index_and_defaults_to_the_first()
    {
        var species = Species();
        Assert.Equal("Alt 1", SkinMaps.Vanilla(species, "Carcharodontosaurus", "alt 1")!.Name);
        Assert.Equal("Alt 1", SkinMaps.Vanilla(species, "Carcharodontosaurus", "1")!.Name);
        Assert.Equal("Base", SkinMaps.Vanilla(species, "Carcharodontosaurus", null)!.Name);
        Assert.Null(SkinMaps.Vanilla(species, "Carcharodontosaurus", "nope"));
    }

    [Fact]
    public void Own_files_win_then_male_vanilla_then_female_vanilla()
    {
        var alt1 = SkinMaps.Vanilla(Species(), "Carcharodontosaurus", "Alt 1");
        var modDir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(modDir, "skins"));
        File.WriteAllText(Path.Combine(modDir, "skins", "d.png"), "png");
        var own = new SkinEntry { Id = "red", Species = "Carcharodontosaurus", Base = "1", Male = new() { ["diffuse"] = "skins/d.png" } };

        Assert.Equal("file:" + Path.Combine(modDir, "skins", "d.png"), SkinMaps.Source(own, modDir, alt1, "diffuse", "male"));
        Assert.Equal("guid:" + SkinDumps.MaleNormal, SkinMaps.Source(own, modDir, alt1, "normal", "male"));
        Assert.Equal("guid:" + SkinDumps.FemaleExtra, SkinMaps.Source(own, modDir, alt1, "extra", "male")); // male has none
        Assert.Null(SkinMaps.Source(own, modDir, alt1, "fur", "male"));
    }

    [Fact]
    public void The_3d_views_male_only_choice_never_borrows_the_female_map()
    {
        var alt1 = SkinMaps.Vanilla(Species(), "Carcharodontosaurus", "Alt 1");
        Assert.Null(SkinMaps.Source(null, null, alt1, "extra", "male-only"));
    }

    [Fact]
    public void An_own_file_outside_the_mod_is_ignored()
    {
        var modDir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));
        var own = new SkinEntry { Id = "x", Species = "Carcharodontosaurus", Male = new() { ["diffuse"] = "../../evil.png" } };
        Assert.Null(SkinMaps.Source(own, modDir, null, "diffuse", "male"));
    }

    [Fact]
    public void Writes_a_vanilla_texture_once()
    {
        var reader = new FakeAssetReader();
        var png = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "t", "normal.png");
        var install = new GameInstall(@"C:\game", null);

        Assert.Equal(png, SkinMaps.Write("guid:" + SkinDumps.MaleNormal, install, SkinDumps.Index(), reader, png));
        Assert.True(File.Exists(png));
        Assert.Equal(png, SkinMaps.Write("guid:" + SkinDumps.MaleNormal, install, SkinDumps.Index(), reader, png));
        Assert.Equal(1, reader.Textures);
        Assert.Null(SkinMaps.Write("guid:ffff", install, SkinDumps.Index(), reader, png + "2"));
    }
}
