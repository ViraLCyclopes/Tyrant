using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;
using static Tyrant.Core.Tests.SkinDumps;

namespace Tyrant.Core.Tests;

public class SkinOrderTests
{
    private const string Key = "tyrant.mod.rex";

    private sealed class Setup : IDisposable
    {
        public FakeGame Game { get; } = new();
        public GameInstall Install { get; }
        public Workspace Ws { get; }
        public ModProject Mod { get; private set; }

        public Setup()
        {
            Install = new GameInstall(Game.Root, null);
            Ws = Workspace.Create(Path.Combine(Game.LibraryRoot, "workspace"), Install);
            Mod = ModProject.Create(Ws, "mod", null, null);
            Mod.CreateSpecies(Install, Index(), new FakeAssetReader(), StartingSkinTests.Species(), "rex", "Allosaurus Anax", "Rex", "Rex", "rex",
                null, null, null, 0, new CopySkin("Allosaurus Anax", 0), null);
            foreach (var id in new[] { "aaa", "bbb" })
            {
                var file = $"skins/{id}/male_D.png";
                var path = Path.Combine(Mod.Dir, file.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, id);
                Mod.Manifest.Skins.Add(new SkinEntry { Id = id, Species = Key, Name = id.ToUpperInvariant(), Base = "0", Male = new() { ["diffuse"] = file } });
            }
            Mod.Save();
        }

        public string Start => Mod.Manifest.Skins.Single(k => k.Starting).Id;
        public void Reopen() => Mod = ModProject.Open(Ws, "mod");
        public List<string> Shown(IReadOnlyDictionary<string, int>? numbers = null) => Mod.ShownSkinOrder(Key, numbers)!;
        public void Dispose() => Game.Dispose();
    }

    private static Dictionary<string, int> Numbers(int a, int b) => new() { ["mod/aaa"] = a, ["mod/bbb"] = b };

    [Fact]
    public void Shown_without_saved_order_uses_numbers()
    {
        using var s = new Setup();
        Assert.Equal([s.Start, "bbb", "aaa"], s.Shown(Numbers(2, 1)));
    }

    [Fact]
    public void Set_order_saves_and_round_trips()
    {
        using var s = new Setup();
        var fileOrder = s.Mod.Manifest.Skins.Select(k => k.Id).ToList();
        s.Mod.SetSkinOrder(Key, ["aaa", s.Start, "bbb"]);
        s.Reopen();
        Assert.Equal(["aaa", s.Start, "bbb"], s.Shown());
        Assert.Equal(fileOrder, s.Mod.Manifest.Skins.Select(k => k.Id));
    }

    [Fact]
    public void Set_order_refuses_bad_lists()
    {
        using var s = new Setup();
        s.Mod.AddSpecies(StartingSkinTests.Species(), "old", "Allosaurus Anax", "Old", "Old", "old", null, null, null, 0);
        void Refused(string key, params string[] ids) =>
            Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() => s.Mod.SetSkinOrder(key, ids)).Code);
        Refused(Key, s.Start, "aaa");
        Refused(Key, s.Start, "aaa", "bbb", "zz");
        Refused(Key, s.Start, "aaa", "aaa");
        Refused("Allosaurus Anax", s.Start, "aaa", "bbb");
        Refused("tyrant.mod.old");
        Assert.Empty(s.Mod.Manifest.SkinOrders);
    }

    [Fact]
    public void Make_default_swaps_places_in_the_saved_order()
    {
        using var s = new Setup();
        var old = s.Start;
        s.Mod.SetSkinOrder(Key, ["aaa", old, "bbb"]);
        s.Mod.MakeStartingSkin("bbb");
        Assert.Equal(["aaa", "bbb", old], s.Shown());
    }

    [Fact]
    public void Make_default_without_a_saved_order_swaps_in_the_shown_order()
    {
        using var s = new Setup();
        var old = s.Start;
        var numbers = Numbers(1, 2);
        s.Mod.MakeStartingSkin("bbb", _ => numbers);
        Assert.Equal(["bbb", "aaa", old], s.Mod.Manifest.SkinOrders[Key]);
    }

    [Fact]
    public void Make_default_without_numbers_swaps_in_file_order()
    {
        using var s = new Setup();
        var old = s.Start;
        s.Mod.MakeStartingSkin("bbb");
        Assert.Equal(["bbb", "aaa", old], s.Mod.Manifest.SkinOrders[Key]);
    }

    [Fact]
    public void A_failed_make_default_restores_the_order()
    {
        using var s = new Setup();
        var json = Path.Combine(s.Mod.Dir, "mod.json");
        File.SetAttributes(json, FileAttributes.ReadOnly);
        try
        {
            Assert.ThrowsAny<Exception>(() => s.Mod.MakeStartingSkin("bbb"));
            Assert.Empty(s.Mod.Manifest.SkinOrders);
            Assert.Equal(s.Start, s.Mod.Manifest.Skins.Single(k => k.Starting).Id);
        }
        finally { File.SetAttributes(json, FileAttributes.Normal); }
    }

    [Fact]
    public void Removing_a_skin_drops_it_from_the_order()
    {
        using var s = new Setup();
        s.Mod.SetSkinOrder(Key, ["aaa", s.Start, "bbb"]);
        s.Mod.RemoveSkin("aaa", false);
        Assert.Equal([s.Start, "bbb"], s.Mod.Manifest.SkinOrders[Key]);
        s.Reopen();
        Assert.Equal([s.Start, "bbb"], s.Shown());
    }

    [Fact]
    public void Removing_the_species_drops_its_order()
    {
        using var s = new Setup();
        s.Mod.SetSkinOrder(Key, ["aaa", s.Start, "bbb"]);
        s.Mod.RemoveSpecies("rex", false);
        Assert.Empty(s.Mod.Manifest.SkinOrders);
    }

    [Fact]
    public void NumbersOf_is_empty_for_a_missing_or_bad_file()
    {
        using var s = new Setup();
        Assert.Empty(SkinSlots.NumbersOf(s.Install, Key));
        var path = SkinSlots.PathOf(s.Install);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not json");
        Assert.Empty(SkinSlots.NumbersOf(s.Install, Key));
        File.WriteAllText(path, "{\"format\":1,\"species\":{\"" + Key + "\":{\"mod/aaa\":3}}}");
        Assert.Equal(3, SkinSlots.NumbersOf(s.Install, Key)["mod/aaa"]);
    }
}
