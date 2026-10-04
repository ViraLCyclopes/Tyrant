using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Tyrant.Core.Tests;

public class VersionAgreementTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tyrant.slnx"))) dir = dir.Parent;
        return dir!.FullName;
    }

    [Fact]
    public void The_app_version_is_the_same_everywhere_it_is_written()
    {
        var root = Root();
        var package = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "studio", "package.json"))).RootElement.GetProperty("version").GetString();
        var tauri = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "studio", "src-tauri", "tauri.conf.json"))).RootElement.GetProperty("version").GetString();
        var cargo = Regex.Match(File.ReadAllText(Path.Combine(root, "studio", "src-tauri", "Cargo.toml")), @"(?m)^version = ""([^""]+)""").Groups[1].Value;
        var props = XDocument.Load(Path.Combine(root, "Directory.Build.props")).Descendants("Version").First().Value;

        Assert.Equal(new[] { package, package, package }, new[] { tauri, cargo, props });
    }

    [Fact]
    public void Set_version_has_a_script()
    {
        var package = File.ReadAllText(Path.Combine(Root(), "studio", "package.json"));
        Assert.Contains("\"set-version\": \"node scripts/set-version.mjs\"", package);
        Assert.True(File.Exists(Path.Combine(Root(), "studio", "scripts", "set-version.mjs")));
    }
}
