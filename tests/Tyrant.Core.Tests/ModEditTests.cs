using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class ModEditTests
{
    private static (FakeGame Game, Workspace Ws, ModProject Mod) Setup()
    {
        var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), new GameInstall(game.Root, null));
        return (game, ws, ModProject.Create(ws, "red-spot", "Red spot", null));
    }

    [Fact]
    public void The_revision_changes_when_mod_json_changes()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        var before = mod.Revision();

        mod.SetDetails("Red spots", "1.1.0", "Shadow", "Spotty carchs.");

        Assert.NotEqual(before, mod.Revision());
        Assert.Equal(mod.Revision(), ModProject.Open(ws, "red-spot").Revision());
    }

    [Fact]
    public void Opening_with_an_old_revision_is_refused()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        var old = mod.Revision();
        File.AppendAllText(Path.Combine(mod.Dir, ModManifest.FileName), " ");

        var ex = Assert.Throws<TyrantException>(() => ModProject.Open(ws, "red-spot", old));

        Assert.Equal(TyrantErrorCode.ModChanged, ex.Code);
        Assert.Contains("changed", ex.Message);
    }

    [Fact]
    public void Details_are_saved_and_a_name_is_required()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;

        mod.SetDetails("  Red spots  ", "2.0", null, "");
        var reopened = ModProject.Open(ws, "red-spot").Manifest;

        Assert.Equal(("Red spots", "2.0", null as string, null as string), (reopened.Name, reopened.Version, reopened.Author, reopened.Description));
        Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() => mod.SetDetails(" ", "1.0", null, null)).Code);
        Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() => mod.SetDetails("Red", "", null, null)).Code);
    }

    [Fact]
    public void Saving_leaves_no_temp_file_and_replaces_mod_json_whole()
    {
        var (game, _, mod) = Setup();
        using var _ = game;

        mod.SetDetails("Red spots", "1.0.0", null, null);

        Assert.Equal([ModManifest.FileName], Directory.GetFiles(mod.Dir).Select(Path.GetFileName));
    }

    [Fact]
    public void A_whole_manifest_is_saved_after_a_strict_check()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        var json = mod.Manifest.ToJson().Replace("\"Red spot\"", "\"Renamed\"");

        var saved = ModProject.SaveManifest(ws, "red-spot", json, mod.Revision());

        Assert.Equal("Renamed", saved.Manifest.Name);
        Assert.Equal("Renamed", ModProject.Open(ws, "red-spot").Manifest.Name);
    }

    [Fact]
    public void A_broken_or_foreign_manifest_is_refused()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        var revision = mod.Revision();

        Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() => ModProject.SaveManifest(ws, "red-spot", "{ nope", revision)).Code);
        Assert.Equal(TyrantErrorCode.ModInvalid,
            Assert.Throws<TyrantException>(() => ModProject.SaveManifest(ws, "red-spot", mod.Manifest.ToJson().Replace("\"red-spot\"", "\"other-mod\""), revision)).Code);
        Assert.Equal(TyrantErrorCode.ModChanged, Assert.Throws<TyrantException>(() => ModProject.SaveManifest(ws, "red-spot", mod.Manifest.ToJson(), "0000")).Code);
        Assert.Equal("Red spot", ModProject.Open(ws, "red-spot").Manifest.Name);
    }

    [Fact]
    public void Removing_a_replacement_keeps_its_png()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        Directory.CreateDirectory(Path.Combine(mod.Dir, "textures"));
        File.WriteAllBytes(Path.Combine(mod.Dir, "textures", "a.png"), [1]);
        mod.Manifest.Replace.Add(new TextureReplacement { Texture = "T_A_D", File = "textures/a.png" });
        mod.Save();

        mod.RemoveReplacement("t_a_d");

        Assert.Empty(ModProject.Open(ws, "red-spot").Manifest.Replace);
        Assert.True(File.Exists(Path.Combine(mod.Dir, "textures", "a.png")));
        Assert.Equal(TyrantErrorCode.TargetNotFound, Assert.Throws<TyrantException>(() => mod.RemoveReplacement("T_A_D")).Code);
    }

    private static string Png(string dir, string name = "in.png")
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        using var stream = File.Create(path);
        new StbImageWriteSharp.ImageWriter().WritePng(new byte[4 * 4 * 4], 4, 4, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        return path;
    }

    private static ModProject WithSkins(Workspace ws, ModProject mod)
    {
        var skins = Path.Combine(mod.Dir, "skins", "blue");
        Png(skins, "male_D.png");
        Png(skins, "male_pattern.png");
        Png(skins, "female_D.png");
        mod.Manifest.Skins.Add(new SkinEntry { Id = "blue", Species = "Carcharodontosaurus", Name = "Blue", Base = "Alt 1",
            Male = new() { ["diffuse"] = "skins/blue/male_D.png", ["pattern"] = "skins/blue/male_pattern.png" },
            Female = new() { ["diffuse"] = "skins/blue/female_D.png" } });
        mod.Manifest.Skins.Add(new SkinEntry { Id = "green", Species = "Carcharodontosaurus", Name = "Green", Base = "Base",
            Male = new() { ["diffuse"] = "skins/blue/male_D.png" } }); // shares a file with blue
        mod.Save();
        return ModProject.Open(ws, mod.Id);
    }

    [Fact]
    public void Renaming_a_skin_keeps_its_id_and_refuses_a_name_already_used()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        mod = WithSkins(ws, mod);

        mod.RenameSkin("blue", "  Ocean blue ");

        var skin = ModProject.Open(ws, "red-spot").Skin("blue");
        Assert.Equal(("blue", "Ocean blue"), (skin.Id, skin.Name));
        var ex = Assert.Throws<TyrantException>(() => mod.RenameSkin("blue", "green"));
        Assert.Equal(TyrantErrorCode.ModInvalid, ex.Code);
        Assert.Contains("already called", ex.Message);
        Assert.Equal(TyrantErrorCode.TargetNotFound, Assert.Throws<TyrantException>(() => mod.RenameSkin("nope", "X")).Code);
    }

    [Fact]
    public void Removing_a_skin_keeps_its_files_unless_asked_and_never_deletes_shared_ones()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        mod = WithSkins(ws, mod);

        var deleted = mod.RemoveSkin("blue", deleteFiles: true);

        Assert.Equal(["green"], ModProject.Open(ws, "red-spot").Manifest.Skins.Select(s => s.Id));
        Assert.Equal(["skins/blue/female_D.png", "skins/blue/male_pattern.png"], deleted.Order(StringComparer.Ordinal));
        Assert.True(File.Exists(Path.Combine(mod.Dir, "skins", "blue", "male_D.png"))); // green uses it
        mod.RemoveSkin("green", deleteFiles: false);
        Assert.True(File.Exists(Path.Combine(mod.Dir, "skins", "blue", "male_D.png")));
    }

    [Fact]
    public void Colours_are_validated_and_an_empty_section_is_removed()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        mod = WithSkins(ws, mod);

        mod.SetColors("blue", "{\"pattern\":{\"a\":\"#3060ff\",\"strength\":[0.6,0.8]}}");
        Assert.Equal("#3060ff", ModProject.Open(ws, "red-spot").Skin("blue").Colors!.Pattern!.A![0].ToString());

        Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() => mod.SetColors("blue", "{\"pattern\":{\"strength\":2}}")).Code);
        mod.SetColors("blue", "{}");
        Assert.Null(ModProject.Open(ws, "red-spot").Skin("blue").Colors);
        mod.SetColors("blue", "{\"tint\":{\"hue\":0}}");
        mod.SetColors("blue", null);
        Assert.Null(ModProject.Open(ws, "red-spot").Skin("blue").Colors);
    }

    [Fact]
    public void A_skin_file_is_copied_into_the_skin_folder_and_can_go_back_to_the_base()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        mod = WithSkins(ws, mod);
        var source = Png(Path.Combine(ws.Dir, "art"), "my extra.png");

        var file = mod.SetSkinFile("blue", "female", "extra", source);

        Assert.Equal("skins/blue/female_extra.png", file);
        Assert.True(File.Exists(Path.Combine(mod.Dir, "skins", "blue", "female_extra.png")));
        Assert.Equal(file, ModProject.Open(ws, "red-spot").Skin("blue").Female!["extra"]);
        Assert.Null(mod.SetSkinFile("blue", "male", "pattern", null));
        Assert.False(ModProject.Open(ws, "red-spot").Skin("blue").Male!.ContainsKey("pattern"));
        Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() => mod.SetSkinFile("blue", "male", "glow", source)).Code);
        Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() => mod.SetSkinFile("blue", "both", "diffuse", source)).Code);
    }

    [Fact]
    public void Replacing_a_skin_file_under_the_same_name_changes_the_files_stamp()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        mod = WithSkins(ws, mod);
        var first = Png(Path.Combine(ws.Dir, "art"), "first.png");
        mod.SetSkinFile("blue", "female", "diffuse", first);
        var before = mod.FilesStamp();
        var second = Path.Combine(ws.Dir, "art", "second.png");
        File.Copy(first, second);
        File.AppendAllText(second, "x"); // other content
        File.SetLastWriteTimeUtc(second, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)); // copies keep their source's date

        var file = mod.SetSkinFile("blue", "female", "diffuse", second);

        Assert.Equal("skins/blue/female_D.png", file);
        Assert.True(File.GetLastWriteTimeUtc(Path.Combine(mod.Dir, "skins", "blue", "female_D.png")).Year > 2020); // stamped now
        Assert.NotEqual(before, mod.FilesStamp());
    }

    [Fact]
    public void Clearing_the_last_file_of_a_skin_is_refused()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        mod = WithSkins(ws, mod);

        var ex = Assert.Throws<TyrantException>(() => mod.SetSkinFile("green", "male", "diffuse", null));

        Assert.Equal(TyrantErrorCode.ModInvalid, ex.Code);
        Assert.Contains("remove the skin instead", ex.Message);
        Assert.NotNull(ModProject.Open(ws, "red-spot").Skin("green").Male);
    }

    [Fact]
    public void Clearing_the_last_file_of_one_sex_drops_that_sex()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        mod = WithSkins(ws, mod);

        mod.SetSkinFile("blue", "female", "diffuse", null);

        Assert.Null(ModProject.Open(ws, "red-spot").Skin("blue").Female);
    }

    [Fact]
    public void A_non_png_skin_file_is_refused()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        mod = WithSkins(ws, mod);
        var jpeg = Path.Combine(ws.Dir, "photo.png");
        File.WriteAllBytes(jpeg, [0xFF, 0xD8, 0xFF, 0xE0, 1, 2]);

        var ex = Assert.Throws<TyrantException>(() => mod.SetSkinFile("blue", "male", "diffuse", jpeg));

        Assert.Equal(TyrantErrorCode.ModInvalid, ex.Code);
        Assert.Contains("not a PNG", ex.Message);
        Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() => mod.SetSkinFile("blue", "male", "diffuse", Path.Combine(ws.Dir, "missing.png"))).Code);
    }

    [Fact]
    public void A_png_with_spaces_and_accents_in_its_path_is_copied()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        mod = WithSkins(ws, mod);
        var source = Png(Path.Combine(ws.Dir, "Mes dessins é"), "peau bleue.png");

        Assert.Equal("skins/blue/male_N.png", mod.SetSkinFile("blue", "male", "normal", source));
    }

    [Fact]
    public void The_thumbnail_is_copied_or_removed()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        mod = WithSkins(ws, mod);

        Assert.Equal("skins/blue/thumbnail.png", mod.SetThumbnail("blue", Png(Path.Combine(ws.Dir, "art"))));
        Assert.Equal("skins/blue/thumbnail.png", ModProject.Open(ws, "red-spot").Skin("blue").Thumbnail);
        Assert.Null(mod.SetThumbnail("blue", null));
        Assert.Null(ModProject.Open(ws, "red-spot").Skin("blue").Thumbnail);
    }

    [Theory]
    [InlineData("male", "diffuse", "male_D.png")]
    [InlineData("female", "normal", "female_N.png")]
    [InlineData("male", "extra", "male_extra.png")]
    [InlineData("male", "infantDiffuse", "male_infant_D.png")]
    [InlineData("female", "infantPattern", "female_infant_pattern.png")]
    public void Skin_files_are_named_like_the_templates(string sex, string slot, string expected)
    {
        Assert.Equal(expected, ModProject.SkinFileName(sex, slot));
    }
}
