using System.IO.Compression;
using PK.Cli;
using PK.Core.Tests;
using PK.Dumper.Serialization;

namespace PK.Cli.Tests;

public class DumpCliTests
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

    private static string Workspace(FakeGame game)
    {
        var dir = Path.Combine(Path.GetTempPath(), "pk-tests", Guid.NewGuid().ToString("N"), "ws");
        Assert.Equal(ExitCodes.Ok, Run("workspace", "init", dir, "--game", game.Root).Code);
        return dir;
    }

    private static void FakeDump(string ws)
    {
        var result = new DumpResult();
        result.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo("PrehistoricKingdom.AnimalData", "Stegosaurus", 1),
            """{"$type":"PrehistoricKingdom.AnimalData","$name":"Stegosaurus","$id":1,"cost":1200}"""));
        DumpWriter.Write(Path.Combine(ws, "data"), result, [], new DumpManifest { RequestId = "r" });
    }

    [Fact]
    public void Dump_run_without_install_reports_not_installed()
    {
        using var game = new FakeGame();
        var (code, _, err) = Run("dump", "run", "-w", Workspace(game), "--timeout", "1");
        Assert.Equal(ExitCodes.PkError, code);
        Assert.Contains("DUMPER_NOT_INSTALLED", err);
    }

    [Fact]
    public void Dump_install_with_a_tampered_archive_is_refused()
    {
        using var game = new FakeGame();
        var zip = Path.Combine(Path.GetTempPath(), "pk-tests", Guid.NewGuid().ToString("N") + ".zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) archive.CreateEntry("winhttp.dll");

        var (code, _, err) = Run("dump", "install", "-w", Workspace(game), "--bepinex-zip", zip);

        Assert.Equal(ExitCodes.PkError, code);
        Assert.Contains("DUMPER_INSTALL_FAILED", err);
        Assert.False(File.Exists(Path.Combine(game.Root, "winhttp.dll")));
    }

    [Fact]
    public void Data_commands_without_a_dump_report_data_missing()
    {
        using var game = new FakeGame();
        var (code, _, err) = Run("data", "types", "-w", Workspace(game));
        Assert.Equal(ExitCodes.PkError, code);
        Assert.Contains("DATA_MISSING", err);
    }

    [Fact]
    public void Data_types_show_and_export_work_on_a_dump()
    {
        using var game = new FakeGame();
        var ws = Workspace(game);
        FakeDump(ws);

        var types = Run("data", "types", "-w", ws);
        var list = Run("data", "show", "AnimalData", "-w", ws);
        var show = Run("data", "show", "AnimalData", "Stegosaurus", "-w", ws);
        var export = Run("data", "export", "AnimalData", "-w", ws);

        Assert.Equal(ExitCodes.Ok, types.Code);
        Assert.Contains("AnimalData", types.Out);
        Assert.Contains("Stegosaurus", list.Out);
        Assert.Contains("\"cost\": 1200", show.Out.Replace("\"cost\":1200", "\"cost\": 1200"));
        Assert.Equal(ExitCodes.Ok, export.Code);
        var csv = Path.Combine(ws, "exports", "AnimalData.csv");
        Assert.Contains("1200", File.ReadAllText(csv));
    }

    [Fact]
    public void Data_export_refuses_output_inside_game_folder()
    {
        using var game = new FakeGame();
        var ws = Workspace(game);
        FakeDump(ws);

        var (code, _, err) = Run("data", "export", "AnimalData", "-w", ws, "--out", Path.Combine(game.Root, "a.csv"));

        Assert.Equal(ExitCodes.PkError, code);
        Assert.Contains("OUTPUT_IN_GAME_FOLDER", err);
    }
}
