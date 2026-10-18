using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;
using static Tyrant.Core.Tests.SkinDumps;

namespace Tyrant.Core.Tests;

public class MakeStartingSkinTests
{
    private const string Template = "Allosaurus Anax";
    private const string Key = "tyrant.pack.mira";

    private sealed class Setup : IDisposable
    {
        public FakeGame Game { get; } = new();
        public Workspace Ws { get; }
        public ModProject Mod { get; }

        public Setup()
        {
            var install = new GameInstall(Game.Root, null);
            Ws = Workspace.Create(Path.Combine(Game.LibraryRoot, "workspace"), install);
            Mod = ModProject.Create(Ws, "pack", null, null);
            Mod.CreateSpecies(install, Index(), new FakeAssetReader(), StartingSkinTests.Species(), "mira", Template, "Mirabilis", "Mira", "bilis",
                null, null, null, 0, new CopySkin(Template, 0), null);
        }

        public SkinEntry Start => Mod.Manifest.Skins.Single(k => k.Starting);

        /// <summary>A second skin of the species with only its own male diffuse.</summary>
        public SkinEntry Brown(string baseSkin = "0")
        {
            var file = "skins/brown/male_D.png";
            var path = Full(file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "brown");
            var skin = new SkinEntry { Id = "brown", Species = Key, Name = "Brown", Base = baseSkin, Male = new() { ["diffuse"] = file } };
            Mod.Manifest.Skins.Add(skin);
            Mod.Save();
            return skin;
        }

        public string Full(string relative) => Path.Combine(Mod.Dir, relative.Replace('/', Path.DirectorySeparatorChar));

        public void Dispose() => Game.Dispose();
    }

    [Fact]
    public void A_skin_made_default_becomes_the_starting_skin_and_the_old_one_an_ordinary_skin()
    {
        using var s = new Setup();
        var old = s.Start;
        s.Brown();

        var (made, previous) = s.Mod.MakeStartingSkin("brown");

        var m = ModProject.Open(s.Ws, "pack").Manifest;
        Assert.Equal("brown", made.Id);
        Assert.Equal(old.Id, previous!.Id);
        Assert.Equal("brown", StartingSkins.Of(m, Key)!.Id);
        Assert.Equal("brown", m.Skins[0].Id); // listed first
        var demoted = m.Skins.Single(k => k.Id == old.Id);
        Assert.False(demoted.Starting);
        Assert.Equal("0", demoted.Base);
        Assert.Empty(StartingSkins.Problems(m));
    }

    [Fact]
    public void It_keeps_the_maps_colours_portraits_and_swatch_it_had_from_the_old_starting_skin()
    {
        using var s = new Setup();
        var old = s.Start;
        var portrait = $"skins/{old.Id}/portraits/adultMale.png";
        Directory.CreateDirectory(Path.GetDirectoryName(s.Full(portrait))!);
        File.WriteAllText(s.Full(portrait), "p");
        old.Portraits = new() { ["adultMale"] = portrait };
        old.Thumbnail = old.Male!["diffuse"];
        old.Colors = new SkinColors { Tint = new SkinTint() };
        s.Mod.Save();
        s.Brown();

        var (made, _) = s.Mod.MakeStartingSkin("brown");

        Assert.Equal("skins/brown/male_D.png", made.Male!["diffuse"]); // its own stays
        Assert.Equal("brown", File.ReadAllText(s.Full(made.Male["diffuse"])));
        foreach (var (slot, file) in old.Male.Where(p => p.Key != "diffuse"))
        {
            Assert.StartsWith("skins/brown/", made.Male[slot]);
            Assert.Equal(File.ReadAllBytes(s.Full(file)), File.ReadAllBytes(s.Full(made.Male[slot])));
        }
        Assert.NotNull(made.Female);
        Assert.Equal(old.Female!.Keys.Order(), made.Female!.Keys.Order());
        Assert.Equal(portrait, made.Portraits!["adultMale"]);
        Assert.Equal(old.Thumbnail, made.Thumbnail);
        Assert.NotNull(made.Colors);
        Assert.NotSame(old.Colors, made.Colors);
    }

    [Fact]
    public void Without_a_starting_skin_the_skin_just_becomes_it_with_base_0()
    {
        using var s = new Setup();
        s.Mod.RemoveSkin(s.Start.Id, deleteFiles: true);
        s.Brown(baseSkin: "Default");

        var (made, previous) = s.Mod.MakeStartingSkin("brown");

        Assert.Null(previous);
        Assert.True(made.Starting);
        Assert.Equal("0", made.Base);
        Assert.Equal(["diffuse"], made.Male!.Keys);
        Assert.Null(made.Female);
    }

    [Fact]
    public void Only_another_skin_of_this_mods_species_can_be_made_default()
    {
        using var s = new Setup();
        s.Mod.Manifest.Skins.Add(new SkinEntry { Id = "anax-red", Species = Template, Name = "Red", Base = "0" });
        s.Mod.Save();

        Assert.Equal(TyrantErrorCode.TargetNotFound, Assert.Throws<TyrantException>(() => s.Mod.MakeStartingSkin("nope")).Code);
        var vanilla = Assert.Throws<TyrantException>(() => s.Mod.MakeStartingSkin("anax-red"));
        Assert.Contains("only a skin of one of this mod's own species", vanilla.Message);
        var already = Assert.Throws<TyrantException>(() => s.Mod.MakeStartingSkin(s.Start.Id));
        Assert.Contains("is already", already.Message);
    }
}
