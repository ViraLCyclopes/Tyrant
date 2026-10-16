using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;
using static Tyrant.Core.Tests.SkinDumps;

namespace Tyrant.Core.Tests;

public class RemoveSpeciesTests
{
    private const string Template = "Allosaurus Anax";
    private const string Key = "tyrant.pack.mira";

    private sealed class Setup : IDisposable
    {
        public FakeGame Game { get; } = new();
        public Workspace Ws { get; }
        public GameInstall Install { get; }
        public ModProject Mod { get; }

        public Setup()
        {
            Install = new GameInstall(Game.Root, null);
            Ws = Workspace.Create(Path.Combine(Game.LibraryRoot, "workspace"), Install);
            Mod = ModProject.Create(Ws, "pack", null, null);
            Mod.CreateSpecies(Install, Index(), new FakeAssetReader(), StartingSkinTests.Species(), "mira", Template, "Mirabilis", "Mira", "bilis",
                null, null, null, 0, new CopySkin(Template, 0), null);
        }

        public string File(string relative, string text = "x")
        {
            var path = Path.Combine(Mod.Dir, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, text);
            return relative;
        }

        public string Full(string relative) => Path.Combine(Mod.Dir, relative.Replace('/', Path.DirectorySeparatorChar));

        /// <summary>A second skin of the species, a model and sounds for it, and things of other species that must stay.</summary>
        public void Dress()
        {
            Mod.Manifest.Skins.Add(new SkinEntry { Id = "mira-blue", Species = Key, Name = "Blue", Base = "0", Male = new() { ["diffuse"] = File("skins/mira-blue/male_D.png") } });
            Mod.Manifest.Skins.Add(new SkinEntry { Id = "anax-red", Species = Template, Name = "Red", Base = "0", Male = new() { ["diffuse"] = File("skins/anax-red/male_D.png") } });
            Mod.Manifest.Models.Add(new ModelReplacement { Target = Key, File = File("models/mira.glb") });
            var mine = new SoundReplacement { Event = "event:/Animals/Roar", Species = Key };
            mine.Files.Add(File("sounds/mira.wav"));
            var skinOnly = new SoundReplacement { Event = "event:/Animals/Call", Skin = "pack/mira-blue" };
            skinOnly.Files.Add(File("sounds/blue.wav"));
            var everyone = new SoundReplacement { Event = "event:/Animals/Step" };
            everyone.Files.Add(File("sounds/step.wav"));
            Mod.Manifest.Sounds.AddRange([mine, skinOnly, everyone]);
            Mod.Save();
            Mod.SetSpeciesIcon("mira", Png("icon.png"));
            var prefab = new PrefabOverrides { Target = Key };
            prefab.Fields.Add(new PrefabField { Component = "Animal", Field = "speed", Value = 2.0 });
            File("prefabs/" + PrefabOverrides.FileName(Key), prefab.ToJson());
        }

        public string Png(string name)
        {
            var path = Path.Combine(Ws.Dir, name);
            System.IO.File.WriteAllBytes(path, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jWWQAAAAASUVORK5CYII="));
            return path;
        }

        public void Dispose() => Game.Dispose();
    }

    [Fact]
    public void Removing_a_species_takes_its_skins_models_sounds_and_prefab_settings()
    {
        using var s = new Setup();
        s.Dress();

        s.Mod.RemoveSpecies("mira", deleteFiles: false);

        var m = ModProject.Open(s.Ws, "pack").Manifest;
        Assert.Empty(m.Species);
        Assert.Equal(["anax-red"], m.Skins.Select(k => k.Id));
        Assert.Empty(m.Models);
        Assert.Equal(["event:/Animals/Step"], m.Sounds.Select(k => k.Event));
        Assert.False(File.Exists(s.Full("prefabs/" + PrefabOverrides.FileName(Key))));
        Assert.True(File.Exists(s.Full("skins/mira-blue/male_D.png"))); // files stay without deleteFiles
        Assert.True(File.Exists(AnimalLocalization.FileFor(s.Mod.Dir, "en-US", "mira/name")));
    }

    [Fact]
    public void With_delete_files_its_files_and_language_files_go_to_the_recycle_bin()
    {
        using var s = new Setup();
        s.Dress();
        var start = s.Mod.Manifest.Skins.Single(k => k.Starting);
        var startFile = start.Male!["diffuse"];

        var removed = s.Mod.RemoveSpecies("mira", deleteFiles: true);

        foreach (var file in new[] { startFile, "skins/mira-blue/male_D.png", "models/mira.glb", "sounds/mira.wav", "sounds/blue.wav", "icons/mira.png" })
        {
            Assert.True(TestRecycleBin.Instance.Holds(s.Full(file)), file);
            Assert.Contains(file, removed.Recycled);
        }
        foreach (var code in AnimalLocalization.Codes)
        {
            Assert.True(TestRecycleBin.Instance.Holds(AnimalLocalization.FileFor(s.Mod.Dir, code, "mira/name")), code);
            Assert.False(Directory.Exists(Path.Combine(s.Mod.Dir, "localization", code, "mira")), code);
        }
        Assert.False(Directory.Exists(s.Full("skins/" + start.Id)));
        Assert.False(Directory.Exists(s.Full("skins/mira-blue")));
        Assert.True(File.Exists(s.Full("skins/anax-red/male_D.png")));
        Assert.True(File.Exists(s.Full("sounds/step.wav")));
        Assert.Empty(removed.Kept);
    }

    [Fact]
    public void Files_another_entry_still_uses_stay()
    {
        using var s = new Setup();
        s.Dress();
        var shared = s.Mod.Manifest.Skins.Single(k => k.Id == "mira-blue").Male!["diffuse"];
        s.Mod.Manifest.Skins.Single(k => k.Id == "anax-red").Female = new() { ["diffuse"] = shared };
        s.Mod.Save();

        var removed = s.Mod.RemoveSpecies("mira", deleteFiles: true);

        Assert.True(File.Exists(s.Full(shared)));
        Assert.DoesNotContain(shared, removed.Recycled);
    }

    [Fact]
    public void An_unknown_species_is_refused()
    {
        using var s = new Setup();

        var ex = Assert.Throws<TyrantException>(() => s.Mod.RemoveSpecies("nobody", deleteFiles: true));

        Assert.Equal(TyrantErrorCode.TargetNotFound, ex.Code);
        Assert.Single(ModProject.Open(s.Ws, "pack").Manifest.Species);
    }

    [Fact]
    public void The_removal_preview_names_what_goes_and_which_other_mods_use_the_species()
    {
        using var s = new Setup();
        s.Dress();
        var other = ModProject.Create(s.Ws, "fans", null, null);
        other.Manifest.Skins.Add(new SkinEntry { Id = "fan-skin", Species = Key, Name = "Fan", Base = "0", Male = new() { ["diffuse"] = "skins/fan-skin/male_D.png" } });
        other.Save();

        var plan = s.Mod.SpeciesRemovalOf(s.Ws, "mira");

        Assert.Equal(Key, plan.Key);
        Assert.Equal(["Blue", "Default"], plan.Skins.Order());
        Assert.Equal((1, 2, true), (plan.Models, plan.Sounds, plan.PrefabSettings));
        var user = Assert.Single(plan.UsedBy);
        Assert.Equal(("fans", 1), (user.ModId, user.Skins));
    }
}
