using Tyrant.Core.Blender;

namespace Tyrant.Core.Tests;

public class BlenderProcessTests
{
    [Fact]
    public void Blender_for_the_user_starts_detached_from_tyrants_pipes()
    {
        // Inheriting the sidecar's stdout would mix Blender's console into the app's protocol and keep the pipe open.
        var info = BlenderProcess.StartInfo(@"C:\b\blender.exe", ["--python-expr", "print('x y')"]);

        Assert.True(info.UseShellExecute);
        Assert.False(info.RedirectStandardOutput);
        Assert.Equal(["--python-expr", "print('x y')"], info.ArgumentList);
        Assert.Equal(@"C:\b", info.WorkingDirectory);
    }

    [Fact]
    public void Blender_run_by_tyrant_gets_its_own_closed_input()
    {
        // In the app the core's input is the app's request pipe; Blender inheriting it waited on it forever (install hung).
        var info = BlenderProcess.RunInfo(@"C:lender.exe", ["--command", "extension", "list"], null);

        Assert.True(info.RedirectStandardInput);
        Assert.True(info.RedirectStandardOutput);
        Assert.False(info.UseShellExecute);
    }
}
