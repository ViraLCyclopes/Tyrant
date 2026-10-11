using System.ComponentModel;
using System.Diagnostics;

namespace Tyrant.Core.Textures;

public sealed class TexconvException(string message) : Exception(message);

/// <summary>
/// Microsoft's texconv (DirectXTex, MIT; shipped next to tyrant.exe): compresses BC7 on the graphics card, or with its quick
/// CPU encoder when there is none. Tyrant's own mips are handed over and kept as they are.
/// </summary>
public sealed class Texconv(string exePath, string? tempRoot = null)
{
    public const string FileName = "texconv.exe";

    /// <summary>The copy shipped with Tyrant.</summary>
    public static Texconv Default { get; } = new(Path.Combine(AppContext.BaseDirectory, FileName));

    public string ExePath { get; } = exePath;

    public bool Available => OperatingSystem.IsWindows() && File.Exists(ExePath);

    /// <summary>The levels compressed to BC7, largest first, back to back (Unity's raw layout).</summary>
    internal byte[] CompressBc7(IReadOnlyList<MipLevel> levels, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!Available) throw new TexconvException($"{ExePath} is missing");
        var dir = Path.Combine(tempRoot ?? Path.GetTempPath(), "tyrant-texconv-" + Guid.NewGuid().ToString("N"));
        try
        {
            var outDir = Directory.CreateDirectory(Path.Combine(dir, "out")).FullName;
            var input = Path.Combine(dir, "in.dds");
            using (var stream = File.Create(input)) Dds.WriteRgba(stream, levels);
            var start = new ProcessStartInfo(ExePath)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var arg in new[] { "-nologo", "-y", "-bc", "q", "-f", "BC7_UNORM", "-o", outDir, input }) start.ArgumentList.Add(arg);
            var last = "";
            using var process = Process.Start(start) ?? throw new TexconvException("texconv did not start");
            process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) last = e.Data.Trim(); };
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) last = e.Data.Trim(); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            using (ct.Register(() => Stop(process))) process.WaitForExit();
            ct.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new TexconvException($"texconv exited with {process.ExitCode}: {last}");
            var output = Path.Combine(outDir, "in.dds");
            if (!File.Exists(output)) throw new TexconvException($"texconv wrote nothing: {last}");
            try { return Dds.ReadBc7(File.ReadAllBytes(output), levels[0].Width, levels[0].Height, levels.Count); }
            catch (InvalidDataException ex) { throw new TexconvException($"texconv's output is not as expected ({ex.Message})"); }
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            throw new TexconvException($"texconv could not run ({ex.Message})");
        }
        finally
        {
            DeleteQuietly(dir);
        }
    }

    private static void Stop(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { } // already gone
    }

    /// <summary>A killed process can hold its files for a moment.</summary>
    private static void DeleteQuietly(string dir)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Thread.Sleep(100); }
        }
    }
}
