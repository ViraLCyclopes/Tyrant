using Tyrant.Cli;
using Tyrant.Core.Assets;
using Tyrant.Core.Tests;

namespace Tyrant.Cli.Tests;

public class SpeciesPictureCliTests : IDisposable
{
    public void Dispose() => CliServices.AssetReader = new BundleAssetReader();

    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var (oldOut, oldErr) = (Console.Out, Console.Error);
        var (o, e) = (new StringWriter(), new StringWriter());
        Console.SetOut(o);
        Console.SetError(e);
        try { return (CliApp.Run(args), o.ToString(), e.ToString()); }
        finally { Console.SetOut(oldOut); Console.SetError(oldErr); }
    }

    /// <summary>A workspace with PictureDumps' dump and index, read by its fake reader.</summary>
    private static (string Ws, FakeAssetReader Reader) Setup(FakeGame game, bool dump = true)
    {
        var ws = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws");
        Assert.Equal(ExitCodes.Ok, Run("workspace", "init", ws, "--game", game.Root).Code);
        if (dump) PictureDumps.Write(Path.Combine(ws, "data"));
        var index = PictureDumps.Index();
        index.Fingerprint = Tyrant.Core.Install.GameFingerprint.Compute(new Tyrant.Core.Install.GameInstall(game.Root, null));
        index.Save(Path.Combine(ws, "cache", AssetIndex.FileName));
        var reader = PictureDumps.Reader();
        CliServices.AssetReader = reader;
        return (ws, reader);
    }

    [Fact]
    public void Save_picture_writes_a_templates_paleopedia_picture()
    {
        using var game = new FakeGame();
        var (ws, reader) = Setup(game);
        var png = Path.Combine(Path.GetDirectoryName(ws)!, "para.png");

        var (code, output, err) = Run("species", "save-picture", "Parasaurolophus Cyrtocristatus", "--paleopedia", "--out", png, "-w", ws);

        Assert.True(code == ExitCodes.Ok, err);
        Assert.Contains($"Saved Parasaurolophus Cyrtocristatus's Paleopedia picture (Parasarulophus Cyrtocristatus, 2048x1152) to {png}", output);
        Assert.Contains("don't share it or put it in a mod", output);
        Assert.True(File.Exists(png));
        Assert.Equal([PictureDumps.ParaPicture.Ref], reader.TexturesWritten);
    }

    [Fact]
    public void Save_picture_writes_a_templates_nursery_picture()
    {
        using var game = new FakeGame();
        var (ws, reader) = Setup(game);
        var png = Path.Combine(Path.GetDirectoryName(ws)!, "allo.png");

        var (code, output, err) = Run("species", "save-picture", "Allosaurus Anax", "--nursery", "--out", png, "-w", ws);

        Assert.True(code == ExitCodes.Ok, err);
        Assert.Contains("Nursery picture (Hero_Allosaurus_A_packed, 512x256)", output);
        Assert.Equal([PictureDumps.AlloHeroTexture.Ref], reader.TexturesWritten);
    }

    [Fact]
    public void Save_picture_needs_exactly_one_kind()
    {
        using var game = new FakeGame();
        var (ws, _) = Setup(game);
        var png = Path.Combine(Path.GetDirectoryName(ws)!, "x.png");

        var neither = Run("species", "save-picture", "Allosaurus Anax", "--out", png, "-w", ws);
        var both = Run("species", "save-picture", "Allosaurus Anax", "--nursery", "--paleopedia", "--out", png, "-w", ws);

        Assert.NotEqual(ExitCodes.Ok, neither.Code);
        Assert.Contains("--nursery or --paleopedia", neither.Err + neither.Out);
        Assert.NotEqual(ExitCodes.Ok, both.Code);
        Assert.False(File.Exists(png));
    }

    [Fact]
    public void Save_picture_without_the_dump_says_to_run_it()
    {
        using var game = new FakeGame();
        var (ws, _) = Setup(game, dump: false);

        var (code, _, err) = Run("species", "save-picture", "Allosaurus Anax", "--nursery", "--out", Path.Combine(Path.GetDirectoryName(ws)!, "a.png"), "-w", ws);

        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("DATA_MISSING", err);
    }
}
