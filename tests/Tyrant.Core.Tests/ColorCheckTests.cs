using StbImageSharp;
using StbImageWriteSharp;
using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;
using static Tyrant.Core.Tests.SkinDumps;
using ColorComponents = StbImageWriteSharp.ColorComponents;

namespace Tyrant.Core.Tests;

public class ColorCheckTests
{
    private static (FakeGame Game, ModProject Mod, IReadOnlyList<SpeciesSkins> Species) Setup()
    {
        var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), new GameInstall(game.Root, null));
        Write(ws.DataDir);
        return (game, ModProject.Create(ws, "red-spot-carcharo", null, null), SpeciesSkinsReader.Load(ws));
    }

    private static byte[] Png(int size, Func<int, (byte R, byte G, byte B)> pixel)
    {
        var rgba = Enumerable.Range(0, size * size).SelectMany(i => { var p = pixel(i); return new byte[] { p.R, p.G, p.B, 255 }; }).ToArray();
        using var stream = new MemoryStream();
        new ImageWriter().WritePng(rgba, size, size, ColorComponents.RedGreenBlueAlpha, stream);
        return stream.ToArray();
    }

    private static ImageResult Image(int size, Func<int, (byte R, byte G, byte B)> pixel) =>
        ImageResult.FromMemory(Png(size, pixel), StbImageSharp.ColorComponents.RedGreenBlueAlpha);

    private static void File(ModProject mod, string relative, byte[] png)
    {
        var path = Path.Combine(mod.Dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, png);
    }

    private static SkinColors PatternColours() => new() { Pattern = new SkinColorSet { A = [new Rgb(0.2f, 0.4f, 1f)] } };

    [Fact]
    public void Pattern_colours_over_a_map_without_red_are_warned()
    {
        var (game, mod, species) = Setup();
        using var _ = game;
        File(mod, "skins/red-spot/m.png", Png(4, _ => (200, 100, 50)));
        File(mod, "skins/red-spot/p.png", Png(4, _ => (0, 120, 255))); // no red anywhere
        mod.Manifest.Skins.Add(new SkinEntry { Id = "red-spot", Species = "Carcharodontosaurus", Name = "Red spot", Base = "Alt 1", Male = new() { ["diffuse"] = "skins/red-spot/m.png", ["pattern"] = "skins/red-spot/p.png" }, Colors = PatternColours() });
        mod.Save();

        var result = new ModChecker(_ => (4, 4)).Check(mod, Index(), species);

        Assert.Contains(result.Warnings, w => w.Contains("no red") && w.Contains("red-spot"));
    }

    [Fact]
    public void Without_its_own_pattern_map_the_base_skins_map_is_checked()
    {
        var (game, mod, species) = Setup();
        using var _ = game;
        File(mod, "skins/red-spot/m.png", Png(4, _ => (200, 100, 50)));
        mod.Manifest.Skins.Add(new SkinEntry { Id = "red-spot", Species = "Carcharodontosaurus", Name = "Red spot", Base = "Alt 1", Male = new() { ["diffuse"] = "skins/red-spot/m.png" }, Colors = PatternColours() });
        mod.Save();
        AssetRecord? asked = null;

        var withRed = new ModChecker(_ => (4, 4), null, a => { asked = a; return Image(4, i => ((byte)(i % 2 == 0 ? 180 : 0), 0, 0)); }).Check(mod, Index(), species);

        Assert.Equal(MalePattern, asked?.Guid);
        Assert.DoesNotContain(withRed.Warnings, w => w.Contains("no red"));
    }

    [Fact]
    public void An_extra_map_brighter_than_90_percent_red_outside_the_base_eyes_is_warned()
    {
        var (game, mod, species) = Setup();
        using var _ = game;
        File(mod, "skins/red-spot/f.png", Png(4, _ => (200, 100, 50)));
        File(mod, "skins/red-spot/fx.png", Png(4, _ => (250, 128, 255))); // all "eye" red
        mod.Manifest.Skins.Add(new SkinEntry { Id = "red-spot", Species = "Carcharodontosaurus", Name = "Red spot", Base = "Alt 1", Female = new() { ["diffuse"] = "skins/red-spot/f.png", ["extra"] = "skins/red-spot/fx.png" } });
        mod.Save();

        var result = new ModChecker(_ => (4, 4), null, _ => Image(4, i => ((byte)(i == 0 ? 255 : 60), 200, 255))).Check(mod, Index(), species); // base: one eye pixel

        Assert.Contains(result.Warnings, w => w.Contains("fx.png") && w.Contains("eyes"));
    }

    [Fact]
    public void An_extra_map_that_keeps_the_eyes_where_they_were_is_fine()
    {
        var (game, mod, species) = Setup();
        using var _ = game;
        File(mod, "skins/red-spot/f.png", Png(4, _ => (200, 100, 50)));
        File(mod, "skins/red-spot/fx.png", Png(4, i => ((byte)(i == 0 ? 255 : 90), 128, 255)));
        mod.Manifest.Skins.Add(new SkinEntry { Id = "red-spot", Species = "Carcharodontosaurus", Name = "Red spot", Base = "Alt 1", Female = new() { ["diffuse"] = "skins/red-spot/f.png", ["extra"] = "skins/red-spot/fx.png" } });
        mod.Save();

        var result = new ModChecker(_ => (4, 4), null, _ => Image(4, i => ((byte)(i == 0 ? 255 : 60), 200, 255))).Check(mod, Index(), species);

        Assert.DoesNotContain(result.Warnings, w => w.Contains("eyes"));
    }
}
