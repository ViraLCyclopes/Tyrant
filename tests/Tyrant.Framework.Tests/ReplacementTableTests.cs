using System.IO;
using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class ReplacementTableTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "tyrant-tests", "mods");

    private static LoadedMod Mod(string id, params (string Texture, string File)[] replacements)
    {
        var manifest = new ModManifest { Id = id, Name = id };
        foreach (var (texture, file) in replacements) manifest.Replace.Add(new TextureReplacement { Texture = texture, File = file });
        return new LoadedMod(manifest, Path.Combine(Root, id));
    }

    [Fact]
    public void Textures_match_by_name_ignoring_case()
    {
        var table = ReplacementTable.Build([Mod("red", ("T_Carcharo_D", "textures/a.png"))], out var messages);

        Assert.True(table.TryGet("t_carcharo_d", out var r));
        Assert.Equal(("red", Path.Combine(Root, "red", "textures", "a.png")), (r.ModId, r.FilePath));
        Assert.False(table.TryGet("T_Other_D", out _));
        Assert.Empty(messages);
        Assert.Equal(1, table.Count);
    }

    [Fact]
    public void The_later_mod_wins_a_conflict_and_it_is_reported()
    {
        var table = ReplacementTable.Build([Mod("first", ("T_X_D", "a.png")), Mod("second", ("T_X_D", "b.png"))], out var messages);

        Assert.True(table.TryGet("T_X_D", out var r));
        Assert.Equal("second", r.ModId);
        var message = Assert.Single(messages);
        Assert.Contains("first", message);
        Assert.Contains("second", message);
    }

    [Theory]
    [InlineData("../other/x.png")]
    [InlineData(@"..\..\x.png")]
    [InlineData(@"C:\Windows\x.png")]
    public void Files_outside_the_mod_folder_are_refused(string file)
    {
        var table = ReplacementTable.Build([Mod("sneaky", ("T_X_D", file))], out var messages);

        Assert.Equal(0, table.Count);
        Assert.Contains("outside the mod folder", Assert.Single(messages));
    }
}
