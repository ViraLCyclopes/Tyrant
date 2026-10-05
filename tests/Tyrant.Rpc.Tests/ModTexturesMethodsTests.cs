using Tyrant.Core.Tests;

namespace Tyrant.Rpc.Tests;

public class ModTexturesMethodsTests
{
    [Fact]
    public async Task A_species_lists_its_skin_textures_to_replace()
    {
        using var game = new FakeGame();
        var h = new RpcHarness(TestStudio.Options(dumperDir: TestStudio.FakeGameModsDir()));
        var ws = TestStudio.TempDir();
        await h.Call("workspace.create", new { dir = ws, gamePath = game.Root });
        SkinDumps.Write(Path.Combine(ws, "data"));
        AssetFixtures.WriteIndex(ws, SkinDumps.Textures, Tyrant.Core.Install.GameFingerprint.Compute(new Tyrant.Core.Install.GameInstall(game.Root, null)));

        var result = await h.Call("mods.speciesTextures", new { species = "carcharodontosaurus" });

        Assert.Equal("Carcharodontosaurus", result.GetProperty("speciesId").GetString());
        var diffuse = result.GetProperty("textures").EnumerateArray().Single(t => t.GetProperty("texture").GetString() == "T_carch_alt1_male_D");
        Assert.Equal("adult colour", diffuse.GetProperty("slot").GetString());
        Assert.Equal(["Alt 1"], diffuse.GetProperty("skins").EnumerateArray().Select(s => s.GetString()));
    }

    [Fact]
    public async Task Without_the_data_dump_it_says_what_to_run()
    {
        using var game = new FakeGame();
        var h = new RpcHarness(TestStudio.Options(dumperDir: TestStudio.FakeGameModsDir()));
        await h.Call("workspace.create", new { dir = TestStudio.TempDir(), gamePath = game.Root });

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.speciesTextures", new { species = "Carcharodontosaurus" }));

        Assert.Contains("Run data dump", ex.Message);
    }
}
