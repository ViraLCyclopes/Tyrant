using StbImageWriteSharp;
using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Mods;
using Tyrant.Core.Textures;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class ModTextureCookerTests
{
    private static string Dir() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"))).FullName;

    private static void Png(string dir, string file, int size = 8, byte alpha = 255, byte red = 200)
    {
        var path = Path.Combine(dir, file.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var rgba = new byte[size * size * 4];
        for (var i = 0; i < rgba.Length; i += 4) (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = (red, 60, 30, alpha);
        using var stream = File.Create(path);
        new ImageWriter().WritePng(rgba, size, size, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
    }

    private static ModManifest Manifest()
    {
        var m = new ModManifest { Id = "red-spot" };
        m.Skins.Add(new SkinEntry { Id = "blue", Species = "Carcharodontosaurus", Male = new() { ["diffuse"] = "skins/blue/male_D.png", ["normal"] = "skins/blue/male_N.png" } });
        m.Skins.Add(new SkinEntry { Id = "green", Species = "Carcharodontosaurus", Female = new() { ["diffuse"] = "skins/blue/male_D.png" } }); // shared
        m.Replace.Add(new TextureReplacement { Texture = "T_Carch_Alt1_E", File = "textures/T_Carch_Alt1_E.png" });
        m.Replace.Add(new TextureReplacement { Texture = "Rock_N", File = "textures/Rock_N.png" });
        m.Replace.Add(new TextureReplacement { Texture = "Fern_Mask", File = "textures/Fern_Mask.png" });
        m.Replace.Add(new TextureReplacement { Texture = "Fern", File = "textures/Fern.png" });
        return m;
    }

    [Fact]
    public void Targets_cover_skin_slots_and_replacements_once_each_with_kind_and_mode()
    {
        var m = Manifest();
        m.Textures.Compression = TextureCompression.Bc7;
        m.Textures.Overrides["skins/blue/male_N.png"] = TextureCompression.Auto;
        var cooker = new ModTextureCooker(Dir(), name => name == "T_Carch_Alt1_E" ? SlotKind.Data : null); // the dump says: an extra map

        var targets = cooker.Targets(m);

        Assert.Equal(new[]
        {
            new CookTarget("skins/blue/male_D.png", SlotKind.Color, TextureCompression.Bc7),
            new CookTarget("skins/blue/male_N.png", SlotKind.Normal, TextureCompression.Auto),
            new CookTarget("textures/T_Carch_Alt1_E.png", SlotKind.Data, TextureCompression.Bc7),
            new CookTarget("textures/Rock_N.png", SlotKind.Normal, TextureCompression.Bc7),
            new CookTarget("textures/Fern_Mask.png", SlotKind.Data, TextureCompression.Bc7),
            new CookTarget("textures/Fern.png", SlotKind.Color, TextureCompression.Bc7),
        }, targets);
        Assert.Equal(6, ModTextureCooker.TextureFiles(m).Count);
    }

    [Fact]
    public void Material_masks_are_cooked_as_data_and_ramps_never()
    {
        var m = new ModManifest { Id = "red-spot" };
        m.Skins.Add(new SkinEntry
        {
            Id = "blue", Species = "Carcharodontosaurus", Male = new() { ["diffuse"] = "skins/blue/male_D.png" },
            Materials = new()
            {
                ["Carch"] = new MaterialOverride
                {
                    Iridescence = new IridescenceSettings
                    {
                        Mask = new MaskSource { File = "materials/blue/mask.png" },
                        Ramp = new RampSource { File = "materials/blue/ramp.png" },
                    },
                },
            },
        });
        m.SpeciesMaterials["Carcharodontosaurus"] = new() { ["Carch"] = new MaterialOverride { Iridescence = new IridescenceSettings { Mask = new MaskSource { File = "materials/species/mask.png" } } } };

        var targets = new ModTextureCooker(Dir()).Targets(m);
        var files = ModTextureCooker.TextureFiles(m);

        Assert.Contains(new CookTarget("materials/blue/mask.png", SlotKind.Data, TextureCompression.Auto), targets);
        Assert.Contains(new CookTarget("materials/species/mask.png", SlotKind.Data, TextureCompression.Auto), targets);
        Assert.DoesNotContain(targets, t => t.File.Contains("ramp"));
        Assert.Contains("materials/blue/mask.png", files);
        Assert.Contains("materials/species/mask.png", files);
        Assert.DoesNotContain(files, f => f.Contains("ramp"));
    }

    [Fact]
    public void Replacement_kinds_come_from_the_vanilla_skin_slot_the_texture_fills()
    {
        var assets = new[]
        {
            new AssetRecord("b", 1, "Texture2D", "T_Carch_D", null, "g-d", null),
            new AssetRecord("b", 2, "Texture2D", "T_Carch_N", null, "g-n", null),
            new AssetRecord("b", 3, "Texture2D", "T_Carch_E", null, "g-e", null),
        };
        var species = new[] { new SpeciesSkins("Carcharodontosaurus", false, [new VanillaSkin(0, "Base",
            new Dictionary<string, string> { ["diffuse"] = "g-d", ["normal"] = "g-n" }, new Dictionary<string, string> { ["extra"] = "g-e" })]) };

        var kinds = ModTextureCooker.ReplacementKinds(assets, species)!;

        Assert.Equal(SlotKind.Color, kinds("T_Carch_D"));
        Assert.Equal(SlotKind.Normal, kinds("t_carch_n"));
        Assert.Equal(SlotKind.Data, kinds("T_Carch_E"));
        Assert.Null(kinds("Fern"));
        Assert.Null(ModTextureCooker.ReplacementKinds(null, species));
    }

    [Fact]
    public void A_pack_holds_every_target_with_its_pngs_fingerprint_and_reuses_the_cache()
    {
        var mod = Dir();
        var files = ModTextureCooker.TextureFiles(Manifest());
        for (var i = 0; i < files.Count; i++) Png(mod, files[i], red: (byte)(20 + i * 30)); // distinct: the cache shares identical PNGs
        var cache = Dir();
        var cooker = new ModTextureCooker(cache);
        var progress = new List<CookProgress>();

        var path = cooker.WritePack(Manifest(), mod, mod, new SyncProgress(progress))!;

        Assert.Equal(Path.Combine(mod, TexturePack.FileName), path);
        using (var stream = File.OpenRead(path))
        {
            var pack = TexturePack.Read(stream);
            Assert.Equal(6, pack.Entries.Count);
            var diffuse = pack.Find("skins/blue/male_D.png", SlotKind.Color)!;
            Assert.Equal(TextureFingerprint.Of(Path.Combine(mod, "skins", "blue", "male_D.png")), diffuse.Fingerprint);
            Assert.Equal(UnityTextureFormats.Dxt1, diffuse.Format);
        }
        Assert.Equal(cooker.Targets(Manifest()).Select((t, i) => (i + 1, 6, t.File)), progress.Select(p => (p.Number, p.Total, p.File)));
        Assert.Equal("Cooking textures 1/6 (skins/blue/male_D.png)...", progress[0].Message);
        Assert.Equal((0.0, 5 / 6.0), (progress[0].Fraction, progress[^1].Fraction));
        Assert.Equal(6, cooker.Cooked);

        Png(mod, "textures/Fern.png", red: 10); // one edit
        var again = new ModTextureCooker(cache);
        var cached = new List<CookProgress>();
        again.WritePack(Manifest(), mod, mod, new SyncProgress(cached));
        Assert.Equal(6, cached.Count); // textures from the cache report too
        Assert.Equal(1, again.Cooked);
    }

    [Fact]
    public void A_missing_or_broken_png_is_named_and_the_old_pack_stays()
    {
        var mod = Dir();
        foreach (var file in ModTextureCooker.TextureFiles(Manifest())) Png(mod, file);
        var cooker = new ModTextureCooker(Dir());
        cooker.WritePack(Manifest(), mod, mod);
        var before = File.ReadAllBytes(Path.Combine(mod, TexturePack.FileName));

        File.WriteAllBytes(Path.Combine(mod, "textures", "Fern.png"), [1, 2, 3]);
        var broken = Assert.Throws<TyrantException>(() => cooker.WritePack(Manifest(), mod, mod));
        Assert.Contains("textures/Fern.png", broken.Message);

        File.Delete(Path.Combine(mod, "textures", "Fern.png"));
        Assert.Contains("textures/Fern.png", Assert.Throws<TyrantException>(() => cooker.WritePack(Manifest(), mod, mod)).Message);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(mod, TexturePack.FileName)));
    }

    [Fact]
    public void A_mod_without_textures_gets_no_pack_and_loses_an_old_one()
    {
        var mod = Dir();
        File.WriteAllText(Path.Combine(mod, TexturePack.FileName), "old");

        Assert.Null(new ModTextureCooker(Dir()).WritePack(new ModManifest { Id = "red-spot" }, mod, mod));
        Assert.False(File.Exists(Path.Combine(mod, TexturePack.FileName)));
    }

    [Fact]
    public void Cancelling_stops_before_the_next_texture()
    {
        var mod = Dir();
        foreach (var file in ModTextureCooker.TextureFiles(Manifest())) Png(mod, file);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => new ModTextureCooker(Dir()).WritePack(Manifest(), mod, mod, null, cts.Token));
        Assert.False(File.Exists(Path.Combine(mod, TexturePack.FileName)));
    }

    [Fact]
    public void Estimates_give_format_cooked_size_and_bytes_per_texture()
    {
        var mod = Dir();
        var m = Manifest();
        foreach (var file in ModTextureCooker.TextureFiles(m)) Png(mod, file, size: 6);
        Png(mod, "textures/Fern.png", size: 8, alpha: 100);
        m.Textures.Overrides["textures/Rock_N.png"] = TextureCompression.Bc7;

        var estimates = new ModTextureCooker(Dir()).Estimate(m, mod);

        var diffuse = estimates.Single(e => e.File == "skins/blue/male_D.png");
        Assert.Equal((UnityTextureFormats.Dxt1, 8, 8, false), (diffuse.Format, diffuse.Width, diffuse.Height, diffuse.Override));
        Assert.Equal(UnityTextureFormats.DataLength(UnityTextureFormats.Dxt1, 8, 8, 4), diffuse.Bytes);
        Assert.Equal(UnityTextureFormats.Dxt5, estimates.Single(e => e.File == "textures/Fern.png").Format);
        var rock = estimates.Single(e => e.File == "textures/Rock_N.png");
        Assert.Equal((UnityTextureFormats.Bc7, true), (rock.Format, rock.Override));
    }

    [Fact]
    public void A_bc7_texture_without_texconv_still_cooks_and_progress_says_it_is_slower()
    {
        var mod = Dir();
        var m = Manifest();
        foreach (var file in ModTextureCooker.TextureFiles(m)) Png(mod, file);
        m.Textures.Overrides["textures/Fern.png"] = TextureCompression.Bc7;
        var progress = new List<CookProgress>();
        var cooker = new ModTextureCooker(Dir(), null, new Texconv(Path.Combine(Dir(), Texconv.FileName)));

        cooker.WritePack(m, mod, mod, new SyncProgress(progress));

        var slower = Assert.Single(progress, p => p.Slower);
        Assert.Equal("textures/Fern.png", slower.File);
        Assert.Equal($"Cooking textures {slower.Number}/6 (textures/Fern.png, without the graphics card: slower)...", slower.Message);
        using var stream = File.OpenRead(Path.Combine(mod, TexturePack.FileName));
        Assert.Equal(UnityTextureFormats.Bc7, TexturePack.Read(stream).Find("textures/Fern.png", SlotKind.Color)!.Format);
    }

    [Fact]
    public void A_bc7_texture_that_fell_back_is_cooked_again_next_time_rather_than_reused()
    {
        var mod = Dir();
        var m = Manifest();
        foreach (var file in ModTextureCooker.TextureFiles(m)) Png(mod, file);
        m.Textures.Overrides["textures/Fern.png"] = TextureCompression.Bc7;
        var cache = Dir();
        new ModTextureCooker(cache, null, new Texconv(Path.Combine(Dir(), Texconv.FileName))).WritePack(m, mod, mod);

        var progress = new List<CookProgress>();
        var again = new ModTextureCooker(cache, null, Texconv.Default);
        again.WritePack(m, mod, mod, new SyncProgress(progress));

        Assert.Equal(1, again.Cooked); // only the fallback BC7 texture, now through texconv
        Assert.DoesNotContain(progress, p => p.Slower);
    }

    private sealed class SyncProgress(List<CookProgress> seen) : IProgress<CookProgress>
    {
        public void Report(CookProgress value) => seen.Add(value);
    }
}
