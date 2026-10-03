using System.Text.RegularExpressions;

namespace Tyrant.Core.Tests;

/// <summary>Core error messages reach app users too: one that names a 'tyrant …' command also names the app action.</summary>
public class MessageWordingTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tyrant.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Tyrant.slnx not found above the test folder");
    }

    [Fact]
    public void Messages_that_name_a_cli_command_also_name_what_to_click_in_the_app()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "core", "Tyrant.Core"), "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!Regex.IsMatch(line, @"'tyrant [a-z]") || line.TrimStart().StartsWith("//") || line.TrimStart().StartsWith("///")) continue;
                if (Regex.IsMatch(line, @"Workspace|Home|tab|in the app|Mods tab|Data tab|Assets tab|Species tab", RegexOptions.IgnoreCase)) continue;
                offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {line.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0, "CLI-only wording:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void Messages_point_to_the_workspace_tab_not_home()
    {
        var offenders = new List<string>();
        foreach (var dir in new[] { Path.Combine("core", "Tyrant.Core"), Path.Combine("core", "Tyrant.Rpc") })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot(), dir), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (Regex.IsMatch(lines[i], @"Home (→|tab)|on the Home|on Home\b"))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
        }

        Assert.True(offenders.Count == 0, "Home is only the start screen now; point to the Workspace tab:\n" + string.Join("\n", offenders));
    }
}
