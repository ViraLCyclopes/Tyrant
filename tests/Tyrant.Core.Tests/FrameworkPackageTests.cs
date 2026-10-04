using System.IO.Compression;
using Tyrant.Core.Dumping;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class FrameworkPackageTests
{
    [Fact]
    public void The_framework_zip_unzips_into_the_game_folder_with_a_readme()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Tyrant.Framework.dll"), "f");
        File.WriteAllText(Path.Combine(dir, "Tyrant.Framework.Core.dll"), "c");
        File.WriteAllText(Path.Combine(dir, "Tyrant.Dumper.dll"), "d");
        var zip = Path.Combine(dir, "out", FrameworkPackage.FileName);

        using var archive = ZipFile.OpenRead(FrameworkPackage.Write(dir, zip));
        var names = archive.Entries.Select(e => e.FullName).OrderBy(n => n).ToArray();
        using var reader = new StreamReader(archive.GetEntry("README.txt")!.Open());
        var readme = reader.ReadToEnd();

        Assert.Equal(["Mods/Tyrant.Framework.dll", "README.txt", "UserLibs/Tyrant.Framework.Core.dll"], names);
        Assert.Contains("MelonLoader 0.7.3", readme);
        Assert.Contains(FrameworkInfo.Version, readme);
        Assert.Contains("UserData\\Tyrant\\Mods", readme);
        Assert.EndsWith($"Tyrant-Framework-{FrameworkInfo.Version}.zip", zip);
    }

    [Fact]
    public void Without_the_framework_files_it_says_to_rebuild()
    {
        var empty = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        Assert.Throws<Tyrant.Core.Errors.TyrantException>(() => FrameworkPackage.Write(empty, Path.Combine(empty, "x.zip")));
    }
}
