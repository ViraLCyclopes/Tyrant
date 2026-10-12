using StbImageSharp;
using Tyrant.Core.Mods;
using Tyrant.Core.Textures;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class SlimCopyTests
{
    private static string CacheDir() => Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "cooked");

    private static (FakeGame Game, ModProject Mod) Setup()
    {
        var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), new Tyrant.Core.Install.GameInstall(game.Root, null));
        return (game, ModEditTests.WithSkins(ws, ModProject.Create(ws, "red-spot", "Red spot", null)));
    }

    /// <summary>The mod's pack, as install writes it.</summary>
    private static TexturePack PackOf(ModProject mod)
    {
        var path = new ModTextureCooker(CacheDir()).CookInPlace(mod)!;
        using var stream = File.OpenRead(path);
        return TexturePack.Read(stream);
    }

    [Fact]
    public void Every_cooked_file_nothing_else_names_is_droppable()
    {
        var (game, mod) = Setup();
        using var _ = game;
        Assert.Equal(["skins/blue/female_D.png", "skins/blue/male_D.png", "skins/blue/male_pattern.png"], SlimCopy.Droppable(mod.Manifest).Order().ToArray());
    }

    [Fact]
    public void A_cooked_file_also_used_as_a_thumbnail_or_portrait_is_kept()
    {
        var (game, mod) = Setup();
        using var _ = game;
        mod.Manifest.Skins[0].Thumbnail = "skins/blue/male_D.png";
        mod.Manifest.Skins[0].Portraits = new() { ["adultFemale"] = "skins/blue/female_D.png" };

        Assert.Equal(["skins/blue/male_pattern.png"], SlimCopy.Droppable(mod.Manifest).ToArray());
    }

    [Fact]
    public void Only_files_the_pack_holds_are_dropped_and_skins_without_a_thumbnail_get_one()
    {
        var (game, mod) = Setup();
        using var _ = game;
        var pack = PackOf(mod);
        File.WriteAllText(Path.Combine(mod.Dir, "skins", "blue", "male_pattern.png"), "edited after cooking"); // still in the pack: dropped

        var plan = SlimCopy.Plan(mod.Manifest, mod.Dir, pack);

        Assert.Equal(3, plan.Dropped.Count);
        Assert.Equal("skins/blue/thumbnail.auto.png", plan.Manifest.Skins.Single(s => s.Id == "blue").Thumbnail);
        Assert.Equal("skins/green/thumbnail.auto.png", plan.Manifest.Skins.Single(s => s.Id == "green").Thumbnail);
        Assert.Null(mod.Manifest.Skins[0].Thumbnail); // the workspace manifest is untouched
        var thumb = ImageResult.FromMemory(plan.Thumbnails["skins/blue/thumbnail.auto.png"], ColorComponents.RedGreenBlueAlpha);
        Assert.Equal((128, 128), (thumb.Width, thumb.Height));
    }

    [Fact]
    public void The_auto_thumbnail_is_the_games_own_crop_of_the_diffuse()
    {
        var (game, mod) = Setup();
        using var _ = game;
        var diffuse = ImageResult.FromMemory(File.ReadAllBytes(Path.Combine(mod.Dir, "skins", "blue", "male_D.png")), ColorComponents.RedGreenBlueAlpha);

        var plan = SlimCopy.Plan(mod.Manifest, mod.Dir, PackOf(mod));

        var thumb = ImageResult.FromMemory(plan.Thumbnails["skins/blue/thumbnail.auto.png"], ColorComponents.RedGreenBlueAlpha);
        Assert.Equal(Pixels.Thumbnail(diffuse.Data, diffuse.Width, diffuse.Height, 128), thumb.Data);
    }

    [Fact]
    public void A_taken_thumbnail_name_gets_a_number()
    {
        var (game, mod) = Setup();
        using var _ = game;
        File.WriteAllText(Path.Combine(mod.Dir, "skins", "blue", "thumbnail.auto.png"), "the author's own file");

        var plan = SlimCopy.Plan(mod.Manifest, mod.Dir, PackOf(mod));

        Assert.Equal("skins/blue/thumbnail.auto-2.png", plan.Manifest.Skins.Single(s => s.Id == "blue").Thumbnail);
    }

    [Fact]
    public void Apply_leaves_out_the_dropped_files_and_writes_the_thumbnails_and_mod_json()
    {
        var (game, mod) = Setup();
        using var _ = game;
        var plan = SlimCopy.Plan(mod.Manifest, mod.Dir, PackOf(mod));
        var copy = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));
        foreach (var file in Directory.GetFiles(mod.Dir, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(copy, Path.GetRelativePath(mod.Dir, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        SlimCopy.Apply(plan, copy);

        Assert.False(File.Exists(Path.Combine(copy, "skins", "blue", "male_D.png")));
        Assert.True(File.Exists(Path.Combine(copy, "skins", "blue", "thumbnail.auto.png")));
        Assert.Equal("skins/blue/thumbnail.auto.png", ModManifest.Parse(File.ReadAllText(Path.Combine(copy, "mod.json"))).Skins[0].Thumbnail);
        Assert.True(File.Exists(Path.Combine(mod.Dir, "skins", "blue", "male_D.png"))); // the workspace keeps its PNGs
    }

    [Theory]
    [InlineData("skins/blue/thumbnail.auto.png", true)]
    [InlineData("skins/blue/Thumbnail.Auto-3.PNG", true)]
    [InlineData("skins/blue/thumbnail.png", false)]
    [InlineData(null, false)]
    public void Auto_thumbnails_are_known_by_name(string? path, bool auto) => Assert.Equal(auto, SlimCopy.IsAutoThumbnail(path));

    [Fact]
    public void Auto_thumbnails_can_be_taken_off_a_manifest()
    {
        var manifest = new ModManifest { Id = "x" };
        manifest.Skins.Add(new SkinEntry { Id = "a", Species = "S", Thumbnail = "skins/a/thumbnail.auto.png", Male = new() { ["diffuse"] = "a.png" } });
        manifest.Skins.Add(new SkinEntry { Id = "b", Species = "S", Thumbnail = "skins/b/mine.png", Male = new() { ["diffuse"] = "b.png" } });

        Assert.True(SlimCopy.WithoutAutoThumbnails(manifest));
        Assert.Null(manifest.Skins[0].Thumbnail);
        Assert.Equal("skins/b/mine.png", manifest.Skins[1].Thumbnail);
        Assert.False(SlimCopy.WithoutAutoThumbnails(manifest));
    }
}
