using Tyrant.Core.Assets;
using Tyrant.Core.Blender;
using Tyrant.Core.Install;
using Tyrant.Core.Models;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Tests;

public class BlenderProjectTests
{
    private sealed record Ctx(FakeGame Game, Workspace Ws, GameInstall Install, AssetIndex Index, IReadOnlyList<SpeciesSkins> Species, FakeAssetReader Reader) : IDisposable
    {
        public void Dispose() => Game.Dispose();
        public BlenderProjectResult Write(BlenderOpenRequest r) => BlenderProjectWriter.Write(r, Ws, Install, Index, Species, Reader, @"C:\Tyrant\sidecar\tyrant.exe");
    }

    private static Ctx Setup()
    {
        var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), install);
        SkinDumps.Write(ws.DataDir);
        var prefab = ModelFixture.Prefab(ModelFixture.Triangle(name: "Carch_LOD0"));
        var lod0 = prefab.Renderers[0] with { Name = "Carch_LOD0", Materials = [new MaterialModel("Carch", [new TextureSlot("_AdultDiffuse", null, 1)])] };
        var lod1 = prefab.Renderers[0] with { Name = "Carch_LOD1", Mesh = ModelFixture.Triangle(name: "Carch_LOD1"), Materials = [new MaterialModel("Carch", [new TextureSlot("_AdultDiffuse", null, 1)])] };
        prefab = prefab with { Renderers = [lod0, lod1] };
        var reader = new FakeAssetReader { PrefabModelToReturn = prefab };
        var index = new AssetIndex { Assets = [.. SkinDumps.Textures, SkinDumps.Prefab] };
        return new Ctx(game, ws, install, index, SpeciesSkinsReader.Load(ws), reader);
    }

    [Fact]
    public void A_game_species_gets_a_project_with_lod0_maps_growth_and_rest()
    {
        using var c = Setup();

        var result = c.Write(new BlenderOpenRequest("Carcharodontosaurus", "Alt 1", null, false, false));

        Assert.Equal(Path.Combine(c.Ws.Dir, "blender", "game", "carcharodontosaurus-alt 1"), result.Dir, ignoreCase: true);
        var project = BlenderProjectFile.Read(result.ProjectFile);
        Assert.Equal("game", project.Source.Kind);
        Assert.Null(project.Destination);
        Assert.Equal(@"C:\Tyrant\sidecar\tyrant.exe", project.Tyrant);
        var carch = project.Materials["Carch"];
        Assert.True(carch.Animal);
        Assert.Equal("textures/diffuse.png", carch.Maps["diffuse"]);
        Assert.Equal("textures/pattern.png", carch.Maps["pattern"]);
        Assert.False(carch.Maps.ContainsKey("fur"));
        Assert.Null(carch.Colors); // vanilla skins keep their textures
        Assert.True(File.Exists(Path.Combine(result.Dir, "textures", "diffuse.png")));
        Assert.NotNull(project.Growth);
        Assert.Contains(project.Rest, b => b.Name == "Hip");
        var meshes = Tyrant.Core.ModelReplacements.GlbModelReader.Read(Path.Combine(result.Dir, "model.glb"));
        Assert.Equal(["Carch_LOD0"], meshes.Select(m => m.Name));
    }

    [Fact]
    public void Both_sexes_get_their_maps_growth_limit_and_size_and_the_project_starts_as_asked()
    {
        using var c = Setup();

        var result = c.Write(new BlenderOpenRequest("Carcharodontosaurus", "Alt 1", null, false, false, "female"));

        var project = BlenderProjectFile.Read(result.ProjectFile);
        Assert.Equal("female", project.Sex);
        Assert.Equal(new BlenderSex(1f, 1.2f), project.Sexes!.Male);
        Assert.Equal(new BlenderSex(0.7f, 1.25f), project.Sexes.Female);
        var carch = project.Materials["Carch"];
        Assert.Equal("textures/female/diffuse.png", carch.FemaleMaps["diffuse"]); // Alt 1's female diffuse
        Assert.Equal("textures/female/normal.png", carch.FemaleMaps["normal"]);   // she has none: the male's
        Assert.True(File.Exists(Path.Combine(result.Dir, "textures", "female", "diffuse.png")));
    }

    [Fact]
    public void Without_skin_data_both_sexes_grow_fully()
    {
        using var c = Setup();
        var result = c.Write(new BlenderOpenRequest("Carcharodontosaurus", null, null, false, false));
        var project = BlenderProjectFile.Read(result.ProjectFile);
        Assert.Equal("male", project.Sex);
        Assert.Equal(new BlenderSex(1f, 1.3f), project.Sexes!.Male); // the first skin (Base)
    }

    [Fact]
    public void Any_game_object_opens_as_an_object_project_with_its_meshes_and_materials()
    {
        using var c = Setup();
        var fence = new AssetRecord("fences.bundle", 42, "GameObject", "Fence_AdobeClay_Post_End_1m", "Assets/Fence.prefab", "abcabcabcabcabcabcabcabcabcabcab", null);
        var picture = new AssetRecord("fences.bundle", 43, "Texture2D", "T_Fence_D", null, "cdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcd", null);
        var post = ModelFixture.Prefab(ModelFixture.Triangle(name: "FencePost", skinned: false, withShape: false), skinned: false);
        post = post with { Renderers = [post.Renderers[0] with { Name = "FencePost", Materials = [new MaterialModel("Adobe", [new TextureSlot("_DiffuseTex", null, 43)])] }] };
        var reader = new FakeAssetReader { PrefabModelToReturn = post };
        var index = new AssetIndex { Assets = [.. SkinDumps.Textures, SkinDumps.Prefab, fence, picture] };

        var result = BlenderProjectWriter.Write(new BlenderOpenRequest("", null, null, false, false) { PrefabRef = fence.Ref }, c.Ws, c.Install, index, c.Species, reader, "t.exe");

        var project = BlenderProjectFile.Read(result.ProjectFile);
        Assert.Equal("object", project.Source.Kind);
        Assert.Equal("Fence_AdobeClay_Post_End_1m", project.Source.Species); // its name, for the scene
        Assert.Null(project.Destination);
        Assert.Null(project.Growth);
        Assert.Null(project.Sexes);
        Assert.False(project.Materials["Adobe"].Animal);
        Assert.Equal("textures/Adobe-diffuse.png", project.Materials["Adobe"].Maps["diffuse"]);
        Assert.StartsWith(Path.Combine(c.Ws.Dir, "blender", "objects"), result.Dir, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["FencePost"], SharpGLTF.Schema2.ModelRoot.Load(Path.Combine(result.Dir, "model.glb")).LogicalMeshes.Select(m => m.Name)); // a prop has no armature
    }

    [Fact]
    public void An_animal_prefab_given_by_reference_opens_as_its_species()
    {
        using var c = Setup();
        var result = c.Write(new BlenderOpenRequest("", null, null, false, false) { PrefabRef = SkinDumps.Prefab.Ref });
        var project = BlenderProjectFile.Read(result.ProjectFile);
        Assert.Equal("game", project.Source.Kind);
        Assert.Equal("Carcharodontosaurus", project.Source.Species);
    }

    [Fact]
    public void Far_lods_come_along_when_asked()
    {
        using var c = Setup();
        var result = c.Write(new BlenderOpenRequest("Carcharodontosaurus", null, null, false, true));
        var meshes = Tyrant.Core.ModelReplacements.GlbModelReader.Read(Path.Combine(result.Dir, "model.glb"));
        Assert.Equal(["Carch_LOD0", "Carch_LOD1"], meshes.Select(m => m.Name));
    }

    [Fact]
    public void A_mod_skin_gets_its_colours_and_its_destination()
    {
        using var c = Setup();
        var mod = ModProject.Create(c.Ws, "reds", null, null);
        var skin = mod.AddSkin(c.Ws, c.Install, c.Index, c.Reader, c.Species, "Carcharodontosaurus", "Red", "1", new SkinTemplateOptions(true, false, false));
        mod.SetColors(skin.Id, "{\"pattern\":{\"a\":\"#ff0000\"}}");

        var result = c.Write(new BlenderOpenRequest("Carcharodontosaurus", skin.Id, "reds", false, false));

        var project = BlenderProjectFile.Read(result.ProjectFile);
        Assert.Equal(new BlenderDestination("reds", "Carcharodontosaurus", skin.Id), project.Destination);
        Assert.Equal("skin", project.Source.Kind);
        Assert.Equal("#ff0000", project.Materials["Carch"].Colors!.A);
    }

    [Fact]
    public void Reopening_keeps_the_destination_and_blend_and_leaves_model_glb_alone()
    {
        using var c = Setup();
        var first = c.Write(new BlenderOpenRequest("Carcharodontosaurus", null, null, false, false));
        var blend = Path.Combine(first.Dir, "carch.blend");
        File.WriteAllText(blend, "user work");
        var p = BlenderProjectFile.Read(first.ProjectFile) with { Blend = blend, Destination = new BlenderDestination("m", "Carcharodontosaurus", null) };
        BlenderProjectFile.Write(first.ProjectFile, p);
        var glbStamp = File.GetLastWriteTimeUtc(Path.Combine(first.Dir, "model.glb"));
        Thread.Sleep(20);

        var again = c.Write(new BlenderOpenRequest("Carcharodontosaurus", null, null, false, false));

        var project = BlenderProjectFile.Read(again.ProjectFile);
        Assert.Equal(blend, project.Blend);
        Assert.Equal("m", project.Destination!.Mod);
        Assert.Equal(glbStamp, File.GetLastWriteTimeUtc(Path.Combine(first.Dir, "model.glb")));
    }

    [Fact]
    public void A_moved_workspace_still_finds_the_projects_blend()
    {
        using var c = Setup();
        var first = c.Write(new BlenderOpenRequest("Carcharodontosaurus", null, null, false, false));
        var blend = Path.Combine(first.Dir, "carch.blend");
        File.WriteAllText(blend, "user work");
        // Recorded where the workspace used to be (another drive letter, a renamed folder).
        BlenderProjectFile.Write(first.ProjectFile, BlenderProjectFile.Read(first.ProjectFile) with { Blend = @"Z:\old-place\carch.blend" });
        var glbStamp = File.GetLastWriteTimeUtc(Path.Combine(first.Dir, "model.glb"));
        Thread.Sleep(20);

        var again = c.Write(new BlenderOpenRequest("Carcharodontosaurus", null, null, false, false));

        Assert.Equal(blend, BlenderProjectFile.Read(again.ProjectFile).Blend);
        Assert.Equal(glbStamp, File.GetLastWriteTimeUtc(Path.Combine(first.Dir, "model.glb")));
    }

    [Fact]
    public void Start_fresh_never_moves_a_blend_outside_the_project_folder()
    {
        using var c = Setup();
        var first = c.Write(new BlenderOpenRequest("Carcharodontosaurus", null, null, false, false));
        var elsewhere = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "mine.blend");
        Directory.CreateDirectory(Path.GetDirectoryName(elsewhere)!);
        File.WriteAllText(elsewhere, "someone else's work");
        BlenderProjectFile.Write(first.ProjectFile, BlenderProjectFile.Read(first.ProjectFile) with { Blend = elsewhere });

        c.Write(new BlenderOpenRequest("Carcharodontosaurus", null, null, true, false));

        Assert.True(File.Exists(elsewhere));
    }

    [Fact]
    public void Start_fresh_leaves_the_users_blend_alone_and_marks_the_project_fresh_once()
    {
        using var c = Setup();
        var first = c.Write(new BlenderOpenRequest("Carcharodontosaurus", null, null, false, false));
        var blend = Path.Combine(first.Dir, "carch.blend");
        File.WriteAllText(blend, "user work, other scenes too");
        BlenderProjectFile.Write(first.ProjectFile, BlenderProjectFile.Read(first.ProjectFile) with { Blend = blend });

        var fresh = c.Write(new BlenderOpenRequest("Carcharodontosaurus", null, null, true, false));

        Assert.Equal("user work, other scenes too", File.ReadAllText(blend)); // the .blend is the user's file now
        var project = BlenderProjectFile.Read(fresh.ProjectFile);
        Assert.True(project.Fresh); // the add-on keeps the old scene as "(old)" and imports anew
        Assert.Equal(blend, project.Blend); // a fresh Blender still opens the user's file
        Assert.False(BlenderProjectFile.Read(c.Write(new BlenderOpenRequest("Carcharodontosaurus", null, null, false, false)).ProjectFile).Fresh);
    }

    [Fact]
    public void A_texture_painted_in_blender_and_other_files_survive_a_normal_open()
    {
        using var c = Setup();
        var first = c.Write(new BlenderOpenRequest("Carcharodontosaurus", "Alt 1", null, false, false));
        var diffuse = Path.Combine(first.Dir, "textures", "diffuse.png");
        File.WriteAllText(diffuse, "painted in Blender");
        File.WriteAllText(Path.Combine(first.Dir, "textures", "notes.txt"), "mine");

        c.Write(new BlenderOpenRequest("Carcharodontosaurus", "Alt 1", null, false, false));

        Assert.Equal("painted in Blender", File.ReadAllText(diffuse));
        Assert.True(File.Exists(Path.Combine(first.Dir, "textures", "notes.txt")));
    }

    [Fact]
    public void A_changed_skin_picture_replaces_the_projects_copy_and_keeps_an_edited_one_as_old_png()
    {
        using var c = Setup();
        var mod = ModProject.Create(c.Ws, "reds", null, null);
        var skin = mod.AddSkin(c.Ws, c.Install, c.Index, c.Reader, c.Species, "Carcharodontosaurus", "Red", "1", new SkinTemplateOptions(true, false, false));
        var own = Path.Combine(mod.Dir, "skins", "red-d.png");
        Directory.CreateDirectory(Path.GetDirectoryName(own)!);
        File.WriteAllText(own, "version 1");
        skin.Male = new() { ["diffuse"] = "skins/red-d.png" };
        mod.Save();
        var first = c.Write(new BlenderOpenRequest("Carcharodontosaurus", skin.Id, "reds", false, false));
        var copy = Path.Combine(first.Dir, "textures", "diffuse.png");
        Assert.Equal("version 1", File.ReadAllText(copy));
        File.WriteAllText(copy, "painted in Blender");
        File.WriteAllText(own, "version 2 (longer)");

        c.Write(new BlenderOpenRequest("Carcharodontosaurus", skin.Id, "reds", false, false));

        Assert.Equal("version 2 (longer)", File.ReadAllText(copy));
        Assert.Equal("painted in Blender", File.ReadAllText(Path.Combine(first.Dir, "textures", "diffuse.old.png")));
    }

    [Fact]
    public void A_project_from_another_game_build_says_so_until_started_fresh()
    {
        using var c = Setup();
        var first = c.Write(new BlenderOpenRequest("Carcharodontosaurus", null, null, false, false));
        File.WriteAllText(Path.Combine(first.Dir, "carch.blend"), "user work");
        BlenderProjectFile.Write(first.ProjectFile, BlenderProjectFile.Read(first.ProjectFile) with { GameBuild = "older" });

        Assert.True(c.Write(new BlenderOpenRequest("Carcharodontosaurus", null, null, false, false)).GameChanged);
        var again = c.Write(new BlenderOpenRequest("Carcharodontosaurus", null, null, false, false));
        Assert.True(again.GameChanged); // the kept .blend still holds the old model
        Assert.True(BlenderProjectFile.Read(again.ProjectFile).GameChanged); // the add-on's panel says so too

        var fresh = c.Write(new BlenderOpenRequest("Carcharodontosaurus", null, null, true, false));
        Assert.False(fresh.GameChanged);
        Assert.False(BlenderProjectFile.Read(fresh.ProjectFile).GameChanged);
    }

    [Fact]
    public void A_mod_model_page_needs_the_mod_to_replace_that_species()
    {
        using var c = Setup();
        ModProject.Create(c.Ws, "empty", null, null);
        var ex = Assert.Throws<Tyrant.Core.Errors.TyrantException>(() => c.Write(new BlenderOpenRequest("Carcharodontosaurus", null, "empty", false, false)));
        Assert.Contains("does not replace", ex.Message);
    }

    [Fact]
    public void An_unreadable_project_file_is_a_clear_error()
    {
        var path = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), BlenderProjectFile.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ nope");
        var ex = Assert.Throws<Tyrant.Core.Errors.TyrantException>(() => BlenderProjectFile.Read(path));
        Assert.Contains("open it again from Tyrant", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
