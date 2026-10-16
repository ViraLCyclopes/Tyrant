using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;
using static Tyrant.Core.Tests.SkinDumps;

namespace Tyrant.Core.Tests;

public class StartingSkinPreviewTests
{
    private sealed class Setup : IDisposable
    {
        public FakeGame Game { get; } = new();
        public GameInstall Install { get; }
        public Workspace Ws { get; }
        public FakeAssetReader Reader { get; } = new();
        public ModProject Mod { get; }

        public Setup()
        {
            Install = new GameInstall(Game.Root, null);
            Ws = Workspace.Create(Path.Combine(Game.LibraryRoot, "workspace"), Install);
            Mod = ModProject.Create(Ws, "pack", null, null);
        }

        public SkinEntry Start(StartingSkinSource source)
        {
            Mod.CreateSpecies(Install, Index(), Reader, StartingSkinTests.Species(), "mira", "Allosaurus Anax", "Mirabilis", "Mira", "bilis", null, null, null, 0, source, null);
            return Mod.StartingSkinOf("mira")!;
        }

        public void Dispose() => Game.Dispose();
    }

    [Fact]
    public void Source_prefers_own_then_the_starting_skin_then_the_game()
    {
        using var s = new Setup();
        var dir = s.Mod.Dir;
        void Put(string file) { Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dir, file))!); File.WriteAllText(Path.Combine(dir, file), "x"); }
        Put("skins/own/male_D.png");
        Put("skins/start/male_D.png");
        Put("skins/start/male_N.png");
        var own = new SkinEntry { Id = "own", Male = new() { ["diffuse"] = "skins/own/male_D.png" } };
        var inherited = new SkinEntry { Id = "start", Male = new() { ["diffuse"] = "skins/start/male_D.png", ["normal"] = "skins/start/male_N.png" } };
        var vanilla = new VanillaSkin(0, "Default", new Dictionary<string, string> { ["diffuse"] = MaleDiffuse, ["normal"] = MaleNormal, ["pattern"] = MalePattern },
            new Dictionary<string, string>());

        Assert.Equal("file:" + Path.GetFullPath(Path.Combine(dir, "skins/own/male_D.png")), SkinMaps.Source(own, dir, vanilla, "diffuse", "male-only", inherited));
        Assert.Equal("file:" + Path.GetFullPath(Path.Combine(dir, "skins/start/male_N.png")), SkinMaps.Source(own, dir, vanilla, "normal", "male-only", inherited));
        Assert.Equal("guid:" + MalePattern, SkinMaps.Source(own, dir, vanilla, "pattern", "male-only", inherited));
        Assert.Equal("guid:" + MaleNormal, SkinMaps.Source(own, dir, vanilla, "normal", "male-only"));
    }

    [Fact]
    public void Edit_a_copy_of_a_cross_species_starting_skin_copies_from_the_copied_species()
    {
        using var s = new Setup();
        var start = s.Start(new CopySkin("Carcharodontosaurus", 1));
        File.Delete(Path.Combine(s.Mod.Dir, start.Male!["normal"]));
        start.Male.Remove("normal");
        var before = s.Reader.Textures;

        var file = s.Mod.CopyBaseFile(s.Install, Index(), s.Reader, StartingSkinTests.Species(), start.Id, "male", "normal");

        Assert.Equal(before + 1, s.Reader.Textures);
        Assert.Equal(file, start.Male["normal"]);
        Assert.True(File.Exists(Path.Combine(s.Mod.Dir, file)));
    }

    [Fact]
    public void Edit_a_copy_on_a_skin_based_on_the_starting_skin_copies_the_starting_skins_png()
    {
        using var s = new Setup();
        var start = s.Start(new CopySkin("Carcharodontosaurus", 1));
        File.WriteAllText(Path.Combine(s.Mod.Dir, start.Male!["pattern"]), "painted");
        var other = s.Mod.AddSkin(s.Ws, s.Install, Index(), s.Reader, StartingSkinTests.Species(), "tyrant.pack.mira", "Second", "0", new SkinTemplateOptions(true, false, false));
        var before = s.Reader.Textures;

        var file = s.Mod.CopyBaseFile(s.Install, Index(), s.Reader, StartingSkinTests.Species(), other.Id, "male", "pattern");

        Assert.Equal(before, s.Reader.Textures); // a file copy, not the game's texture
        Assert.Equal("painted", File.ReadAllText(Path.Combine(s.Mod.Dir, file)));
    }

    [Fact]
    public void Add_skin_on_a_species_with_a_starting_skin_starts_from_its_pngs()
    {
        using var s = new Setup();
        var start = s.Start(new CopySkin("Carcharodontosaurus", 1));
        File.WriteAllText(Path.Combine(s.Mod.Dir, start.Male!["diffuse"]), "painted");
        File.WriteAllText(Path.Combine(s.Mod.Dir, start.Male["normal"]), "painted normal");

        var other = s.Mod.AddSkin(s.Ws, s.Install, Index(), s.Reader, StartingSkinTests.Species(), "tyrant.pack.mira", "Second", "0", new SkinTemplateOptions(true, true, true));

        Assert.Equal("painted", File.ReadAllText(Path.Combine(s.Mod.Dir, other.Male!["diffuse"])));
        Assert.Equal("painted normal", File.ReadAllText(Path.Combine(s.Mod.Dir, other.Male["normal"])));
        Assert.True(File.Exists(Path.Combine(s.Mod.Dir, other.Female!["diffuse"]))); // the male-only starting skin has none: the template's
    }

    [Fact]
    public void A_skin_based_on_the_starting_skin_shows_its_portraits()
    {
        using var s = new Setup();
        var start = s.Start(new CopySkin("Allosaurus Anax", 0));
        var png = Path.Combine(s.Ws.Dir, "p.png");
        File.WriteAllBytes(png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jWWQAAAAASUVORK5CYII="));
        s.Mod.SetPortraits(start.Id, new Dictionary<string, PortraitSource?> { ["adultMale"] = new PortraitSource(png) });
        var other = s.Mod.AddSkin(s.Ws, s.Install, Index(), s.Reader, StartingSkinTests.Species(), "tyrant.pack.mira", "Second", "0", new SkinTemplateOptions(true, false, false));

        var shown = ShownPortraits.ForSkin(s.Mod, other, null).Single(p => p.Slot == "adultMale");

        Assert.True(shown.Inherited);
        Assert.Equal("file:" + Path.Combine(s.Mod.Dir, start.Portraits!["adultMale"]), shown.Source);
    }

    [Fact]
    public void Edit_a_copy_of_a_sex_the_copied_skin_lacks_takes_the_template_texture()
    {
        using var s = new Setup();
        var start = s.Start(new CopySkin("Carcharodontosaurus", 1)); // male only
        var before = s.Reader.Textures;

        var file = s.Mod.CopyBaseFile(s.Install, Index(), s.Reader, StartingSkinTests.Species(), start.Id, "female", "diffuse");

        Assert.Equal(before + 1, s.Reader.Textures); // the template's female diffuse
        Assert.Equal(file, start.Female!["diffuse"]);
    }

    [Fact]
    public void Check_reports_bad_starting_flags_and_notes_cross_species_textures_without_a_model()
    {
        using var s = new Setup();
        s.Start(new CopySkin("Carcharodontosaurus", 1));
        var stray = s.Mod.AddSkin(s.Ws, s.Install, Index(), s.Reader, StartingSkinTests.Species(), "Carcharodontosaurus", "Stray", "1", new SkinTemplateOptions(true, false, false));
        stray.Starting = true;
        s.Mod.Save();

        var result = new ModChecker(_ => (4, 4)).Check(ModProject.Open(s.Ws, "pack"), Index(), StartingSkinTests.Species());

        Assert.Contains(result.Errors, e => e.StartsWith($"Skin {stray.Id}: ") && e.Contains("not one of this mod's species"));
        Assert.Contains(result.Warnings, w => w.Contains("These textures fit Carcharodontosaurus's model; give this skin that model (Models on its page) or they will not line up."));
    }
}
