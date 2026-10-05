using Tyrant.Cli;
using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Models;
using Tyrant.Core.Tests;

namespace Tyrant.Cli.Tests;

public class ModModelCliTests
{
    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var (oldOut, oldErr) = (Console.Out, Console.Error);
        var (o, e) = (new StringWriter(), new StringWriter());
        Console.SetOut(o);
        Console.SetError(e);
        try { return (CliApp.Run(args), o.ToString(), e.ToString()); }
        finally { Console.SetOut(oldOut); Console.SetError(oldErr); }
    }

    /// <summary>A workspace with the species data, an index holding Carcharodontosaurus' prefab, mod 'big-carch' and a .glb; the CLI reads the prefab through a fake reader.</summary>
    private static (string Ws, string Glb) Setup(FakeGame game)
    {
        var ws = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws");
        Assert.Equal(ExitCodes.Ok, Run("workspace", "init", ws, "--game", game.Root).Code);
        Assert.Equal(ExitCodes.Ok, Run("mod", "new", "big-carch", "--name", "Big Carch", "-w", ws).Code);
        SkinDumps.Write(Path.Combine(ws, "data"));
        new AssetIndex { Assets = [.. SkinDumps.Textures, SkinDumps.Prefab], Fingerprint = GameFingerprint.Compute(new GameInstall(game.Root, null)) }
            .Save(Path.Combine(ws, "cache", AssetIndex.FileName));
        var prefab = ModelFixture.Prefab(ModelFixture.Triangle(name: "Carch_LOD00"), skinned: true);
        prefab = prefab with { Renderers = [prefab.Renderers[0] with { Materials = [new MaterialModel("Carch", [])] }] };
        CliServices.AssetReader = new FakeAssetReader { PrefabModelToReturn = prefab };
        var glb = Path.Combine(ws, "carch.glb");
        GltfModelWriter.WriteGlb(prefab, prefab.Renderers[0], glb, [new GltfMaterial("Carch")]);
        return (ws, glb);
    }

    [Fact]
    public void Species_textures_lists_a_species_skin_textures()
    {
        using var game = new FakeGame();
        var (ws, _) = Setup(game);
        try
        {
            var list = Run("species", "textures", "Carcharodontosaurus", "-w", ws);

            Assert.True(list.Code == ExitCodes.Ok, list.Err);
            Assert.Contains("T_carch_alt1_male_D", list.Out);
            Assert.Contains("adult colour", list.Out);
            Assert.Contains("Alt 1", list.Out);
        }
        finally
        {
            CliServices.AssetReader = new BundleAssetReader();
        }
    }

    [Fact]
    public void Skin_file_copy_base_puts_the_base_texture_in_the_skin()
    {
        using var game = new FakeGame();
        var (ws, _) = Setup(game);
        try
        {
            var dir = Path.Combine(ws, "mods", "big-carch");
            Directory.CreateDirectory(Path.Combine(dir, "skins", "spiked"));
            File.WriteAllText(Path.Combine(dir, "skins", "spiked", "male_D.png"), "png");
            File.WriteAllText(Path.Combine(dir, "mod.json"),
                "{\"format\":1,\"id\":\"big-carch\",\"name\":\"Big Carch\",\"version\":\"1.0.0\",\"replace\":[],\"skins\":[{\"id\":\"spiked\",\"species\":\"Carcharodontosaurus\",\"name\":\"Spiked\",\"base\":\"1\",\"male\":{\"diffuse\":\"skins/spiked/male_D.png\"}}]}");

            var copy = Run("mod", "skin-file", "big-carch", "spiked", "male", "normal", "--copy-base", "-w", ws);

            Assert.True(copy.Code == ExitCodes.Ok, copy.Err + copy.Out);
            Assert.Contains("skins/spiked/male_N.png", File.ReadAllText(Path.Combine(ws, "mods", "big-carch", "mod.json")));
        }
        finally
        {
            CliServices.AssetReader = new BundleAssetReader();
        }
    }

    [Fact]
    public void Replace_model_and_remove_model_work_from_the_command_line()
    {
        using var game = new FakeGame();
        var (ws, glb) = Setup(game);
        try
        {
            var add = Run("mod", "replace-model", "big-carch", "Carcharodontosaurus", glb, "-w", ws);
            var show = Run("mod", "show", "big-carch", "-w", ws);
            var rebuild = Run("mod", "rebuild-models", "big-carch", "-w", ws);
            var remove = Run("mod", "remove-model", "big-carch", "Carcharodontosaurus", "-w", ws);

            Assert.True(add.Code == ExitCodes.Ok, add.Err);
            Assert.Contains("LOD 0", add.Out);
            Assert.Contains("Models (1)", show.Out);
            Assert.Contains("Carcharodontosaurus", show.Out);
            Assert.True(rebuild.Code == ExitCodes.Ok, rebuild.Err);
            Assert.True(remove.Code == ExitCodes.Ok, remove.Err);
        }
        finally
        {
            CliServices.AssetReader = new BundleAssetReader();
        }
    }
}
