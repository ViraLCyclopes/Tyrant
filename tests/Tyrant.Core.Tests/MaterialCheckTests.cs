using StbImageWriteSharp;
using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Models;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;
using static Tyrant.Core.Tests.SkinDumps;

namespace Tyrant.Core.Tests;

public class MaterialCheckTests
{
    private const string Animal = "Carch_Mat";
    private const string Custom = "tyrant.red-spot-carcharo.anax";
    private static readonly AssetRecord RampTexture = new("carch.bundle", 6, "Texture2D", "ramp", "Assets/ramp.png", "66666666666666666666666666666666", null);

    private sealed record Setup(FakeGame Game, GameInstall Install, ModProject Mod, IReadOnlyList<SpeciesSkins> Species, AssetIndex Index, FakeAssetReader Reader) : IDisposable
    {
        public void Dispose() => Game.Dispose();

        public ModCheckResult Check(bool withReader = true) =>
            new ModChecker(_ => (4, 4)).Check(Mod, Index, Species,
                animalMaterialOf: withReader ? (index, prefab) => AnimalMaterial.Of(Install, Reader, index, prefab) : null);
    }

    /// <summary>A mod on Carcharodontosaurus whose prefab has one animal material (with a ramp when baseRamp).</summary>
    private static Setup Make(bool baseRamp = false)
    {
        var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), install);
        Write(ws.DataDir);
        var mod = ModProject.Create(ws, "red-spot-carcharo", null, null);
        Png(mod, "skins/blue/m.png", 4);
        var slots = new List<Tyrant.Core.Models.TextureSlot> { new Tyrant.Core.Models.TextureSlot("_AdultDiffuse", null, 1) };
        if (baseRamp) slots.Add(new Tyrant.Core.Models.TextureSlot(AnimalMaterial.RampSlot, null, 6));
        var reader = new FakeAssetReader { PrefabMaterials = [new MaterialModel(Animal, slots)] };
        return new Setup(game, install, mod, SpeciesSkinsReader.Load(ws), new AssetIndex { Assets = [.. SkinDumps.Textures, Prefab, RampTexture] }, reader);
    }

    private static void Png(ModProject mod, string relative, int width)
    {
        var path = Path.Combine(mod.Dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        new ImageWriter().WritePng(new byte[width * 4], width, 1, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
    }

    private static MaterialOverride Iridescent(IridescenceSettings iridescence) => new() { Iridescence = iridescence };

    private static SkinEntry Skin(Setup s, IridescenceSettings iridescence, string material = Animal)
    {
        var skin = new SkinEntry
        {
            Id = "blue", Species = "Carcharodontosaurus", Name = "Blue", Base = "Alt 1", Male = new() { ["diffuse"] = "skins/blue/m.png" },
            Materials = new() { [material] = Iridescent(iridescence) },
        };
        s.Mod.Manifest.Skins.Add(skin);
        s.Mod.Save();
        return skin;
    }

    [Fact]
    public void A_missing_ramp_or_mask_file_is_an_error()
    {
        using var s = Make();
        Skin(s, new IridescenceSettings { Ramp = new RampSource { File = "materials/blue/ramp.png" }, Mask = new MaskSource { File = "materials/blue/mask.png" } });

        var result = s.Check();

        Assert.Contains("red-spot-carcharo/blue iridescence ramp: materials/blue/ramp.png is missing.", result.Errors);
        Assert.Contains("red-spot-carcharo/blue iridescence mask: materials/blue/mask.png is missing.", result.Errors);
    }

    [Fact]
    public void A_ramp_outside_the_mod_uses_the_usual_wording()
    {
        using var s = Make();
        Skin(s, new IridescenceSettings { Ramp = new RampSource { File = "../ramp.png" } });

        Assert.Contains(s.Check().Errors, e => e.Contains("iridescence ramp") && e.Contains("points outside the mod folder."));
    }

    [Fact]
    public void A_mask_taken_from_a_map_the_skin_lacks_is_an_error_until_it_has_one()
    {
        using var s = Make();
        var skin = Skin(s, new IridescenceSettings { Mask = new MaskSource { From = "extra" } });
        const string text = "red-spot-carcharo/blue iridescence mask: the skin has no extra map in the mod; import one or use Edit a copy first.";

        Assert.Contains(text, s.Check().Errors);

        Png(s.Mod, "skins/blue/f_extra.png", 4);
        skin.Female = new() { ["extra"] = "skins/blue/f_extra.png" };
        s.Mod.Save();
        Assert.DoesNotContain(text, s.Check().Errors);
    }

    [Fact]
    public void A_mask_taken_from_the_starting_skins_map_counts()
    {
        using var s = Make();
        Png(s.Mod, "skins/start/m_extra.png", 4);
        s.Mod.Manifest.Species.Add(new SpeciesEntry { Id = "anax", Base = "Carcharodontosaurus" });
        s.Mod.Manifest.Skins.Add(new SkinEntry { Id = "start", Species = Custom, Name = "Start", Base = "0", Starting = true, Male = new() { ["extra"] = "skins/start/m_extra.png" } });
        s.Mod.Manifest.Skins.Add(new SkinEntry
        {
            Id = "child", Species = Custom, Name = "Child", Base = "0", Male = new() { ["diffuse"] = "skins/blue/m.png" },
            Materials = new() { [Animal] = Iridescent(new IridescenceSettings { Mask = new MaskSource { From = "extra" } }) },
        });
        s.Mod.Save();

        Assert.DoesNotContain(s.Check().Errors, e => e.Contains("iridescence mask"));
    }

    [Fact]
    public void A_material_that_is_not_the_animals_is_an_error()
    {
        using var s = Make();
        Skin(s, new IridescenceSettings { Scale = 1 }, material: "TopHat");

        var result = s.Check();

        Assert.Contains(result.Errors, e => e.Contains("\"TopHat\" is not this animal's material (Carch_Mat); extra materials come in a later Tyrant."));
    }

    [Fact]
    public void Without_a_reader_material_names_are_not_checked()
    {
        using var s = Make();
        Skin(s, new IridescenceSettings { Scale = 1 }, material: "TopHat");

        Assert.DoesNotContain(s.Check(withReader: false).Errors, e => e.Contains("extra materials"));
    }

    [Fact]
    public void Strength_zero_everywhere_warns_unless_the_base_material_has_a_ramp_to_turn_off()
    {
        const string text = "red-spot-carcharo/blue: iridescence strength is 0 everywhere, so it is off.";
        using (var plain = Make())
        {
            Skin(plain, new IridescenceSettings { Strength = new VariantNumbers { Normal = 0, Albino = 0 } });
            Assert.Contains(text, plain.Check().Warnings);
        }
        using (var ramped = Make(baseRamp: true))
        {
            Skin(ramped, new IridescenceSettings { Strength = new VariantNumbers { Normal = 0 } });
            Assert.DoesNotContain(text, ramped.Check().Warnings);
        }
        using (var somewhere = Make())
        {
            Skin(somewhere, new IridescenceSettings { Strength = new VariantNumbers { Normal = 0, Leucistic = 0.5f } });
            Assert.DoesNotContain(text, somewhere.Check().Warnings);
        }
    }

    [Fact]
    public void A_ramp_narrower_than_32_pixels_is_warned()
    {
        using var s = Make();
        Png(s.Mod, "materials/blue/ramp.png", 16);
        Skin(s, new IridescenceSettings { Ramp = new RampSource { File = "materials/blue/ramp.png" } });

        var result = s.Check();

        Assert.Contains("red-spot-carcharo/blue iridescence ramp: materials/blue/ramp.png is 16 px wide; the game samples 32 or more.", result.Warnings);
        Assert.DoesNotContain(result.Errors, e => e.Contains("ramp"));
    }

    [Fact]
    public void A_species_entry_with_only_materials_is_not_a_broken_model_or_rig()
    {
        using var s = Make();
        s.Mod.Manifest.SpeciesMaterials["Carcharodontosaurus"] = new() { [Animal] = Iridescent(new IridescenceSettings { Scale = 1 }) };
        s.Mod.Save();

        var result = s.Check();

        Assert.Empty(result.Errors);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("Model of") || w.Contains("Rig edit"));
    }

    [Fact]
    public void A_species_entry_with_only_materials_does_not_count_as_the_model_a_copied_starting_skin_needs()
    {
        using var s = Make();
        s.Mod.Manifest.Species.Add(new SpeciesEntry { Id = "anax", Base = "Carcharodontosaurus" });
        s.Mod.Manifest.Skins.Add(new SkinEntry
        {
            Id = "start", Species = Custom, Name = "Start", Base = "0", Starting = true, Male = new() { ["diffuse"] = "skins/blue/m.png" },
            CopiedFrom = new SkinSource { Species = "Spinosaurus", Skin = 0 },
        });
        s.Mod.Manifest.SpeciesMaterials[Custom] = new() { [Animal] = Iridescent(new IridescenceSettings { Scale = 1 }) };
        s.Mod.Save();

        Assert.Contains(s.Check().Warnings, w => w.Contains("give this skin that model"));
    }
}
