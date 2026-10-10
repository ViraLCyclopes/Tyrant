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
        var progress = new List<(int, int)>();

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
        Assert.Equal((6, 6), progress[^1]);
        Assert.Equal(6, cooker.Cooked);

        Png(mod, "textures/Fern.png", red: 10); // one edit
        var again = new ModTextureCooker(cache);
        again.WritePack(Manifest(), mod, mod);
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

    private sealed class SyncProgress(List<(int, int)> seen) : IProgress<(int Done, int Total)>
    {
        public void Report((int Done, int Total) value) => seen.Add(value);
    }
}
