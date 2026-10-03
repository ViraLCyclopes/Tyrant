using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Tyrant.Cli;

namespace Tyrant.Cli.Tests;

public class RpcCliTests
{
    private static ProcessStartInfo Tyrant(string args)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "tyrant.exe");
        var psi = File.Exists(exe)
            ? new ProcessStartInfo(exe, args)
            : new ProcessStartInfo("dotnet", $"\"{Path.Combine(AppContext.BaseDirectory, "tyrant.dll")}\" {args}");
        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.StandardOutputEncoding = new UTF8Encoding(false);
        return psi;
    }

    [Fact]
    public async Task Rpc_mode_answers_on_stdout_and_exits_when_its_input_closes()
    {
        using var process = Process.Start(Tyrant("rpc"))!;
        process.StandardInput.NewLine = "\n";

        await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":1,"method":"app.info"}""");
        await process.StandardInput.FlushAsync();
        var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
        process.StandardInput.Close();
        var exited = process.WaitForExit(30_000);

        var response = JsonDocument.Parse(line!).RootElement;
        Assert.Equal(1, response.GetProperty("result").GetProperty("protocolVersion").GetInt32());
        Assert.True(exited, "tyrant rpc did not exit after its input closed");
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public void Emit_ts_writes_the_protocol_declarations()
    {
        var path = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "types.gen.ts");
        var (oldOut, oldErr) = (Console.Out, Console.Error);
        Console.SetOut(new StringWriter());
        Console.SetError(new StringWriter());
        int code;
        try { code = CliApp.Run(["rpc", "--emit-ts", path]); }
        finally { Console.SetOut(oldOut); Console.SetError(oldErr); }

        Assert.Equal(ExitCodes.Ok, code);
        var ts = File.ReadAllText(path);
        Assert.Contains("export interface RpcMethods", ts);
        Assert.Contains("\"workspace.status\"", ts);
    }
}
