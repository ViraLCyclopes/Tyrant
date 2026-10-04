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
}
