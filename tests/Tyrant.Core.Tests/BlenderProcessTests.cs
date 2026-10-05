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
}
