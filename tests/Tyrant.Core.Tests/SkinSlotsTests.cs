using Tyrant.Core.Dumping;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class SkinSlotsTests
{
    private static (FakeGame Game, GameInstall Install) Setup()
    {
        var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var numbers = new SkinNumbers();
        numbers.Assign("Carcharodontosaurus", 3, ["kept-mod/red", "gone-mod/blue", "kept-mod/old"], []);
        Directory.CreateDirectory(ModLoaderInstaller.RecordDir(install));
        File.WriteAllText(SkinSlots.PathOf(install), numbers.ToJson());
        var kept = Path.Combine(ModLoaderInstaller.ModsDir(install), "kept-mod");
        Directory.CreateDirectory(kept);
        File.WriteAllText(Path.Combine(kept, "mod.json"),
            """{ "format": 1, "id": "kept-mod", "skins": [ { "id": "red", "species": "Carcharodontosaurus", "male": { "diffuse": "a.png" } } ] }""");
        return (game, install);
    }

    [Fact]
    public void Orphans_are_skins_whose_mod_is_gone_or_no_longer_has_them()
    {
        var (game, install) = Setup();
        using var _ = game;

        var orphans = new SkinSlots().Orphans(install);

        Assert.Equal(new[] { ("Carcharodontosaurus", "gone-mod/blue", 16), ("Carcharodontosaurus", "kept-mod/old", 17) },
            orphans.Select(o => (o.Species, o.Key, o.Number)));
    }

    [Fact]
    public void Forget_frees_the_numbers_and_refuses_while_the_game_runs()
    {
        var (game, install) = Setup();
        using var _ = game;

        Assert.Equal(TyrantErrorCode.GameRunning, Assert.Throws<TyrantException>(() => new SkinSlots(_ => true).Forget(install, ["gone-mod/blue"])).Code);
        var forgotten = new SkinSlots().Forget(install, ["gone-mod/blue", "not/there"]);

        Assert.Equal(1, forgotten);
        var numbers = SkinNumbers.Parse(File.ReadAllText(SkinSlots.PathOf(install)));
        Assert.False(numbers.Of("Carcharodontosaurus").ContainsKey("gone-mod/blue"));
        Assert.Equal(15, numbers.Of("Carcharodontosaurus")["kept-mod/red"]);
    }

    [Fact]
    public void Without_a_file_there_is_nothing_to_clean_up()
    {
        using var game = new FakeGame();

        Assert.Empty(new SkinSlots().Orphans(new GameInstall(game.Root, null)));
    }
}
