using Tyrant.Core.Tests;
using static Tyrant.Rpc.Tests.AssetFixtures;

namespace Tyrant.Rpc.Tests;

public class EnvironmentMethodsTests
{
    [Fact]
    public async Task Environments_list_grounds_and_skies()
    {
        using var game = new FakeGame();
        var (h, _, _) = await Opened(game);

        var list = await h.Call("assets.environments");

        Assert.Contains(list.GetProperty("grounds").EnumerateArray(), g => g.GetProperty("id").GetString() == "lush-grass");
        Assert.Contains(list.GetProperty("skies").EnumerateArray(), s => s.GetProperty("label").GetString() == "Noon");
    }

    [Fact]
    public async Task A_sky_is_written_once_as_six_faces_in_the_preview_cache()
    {
        using var game = new FakeGame();
        var (h, ws, reader) = await Opened(game);

        var first = await h.Call("assets.environment", new { id = "noon" });
        await h.Call("assets.environment", new { id = "noon" });

        Assert.Equal("sky", first.GetProperty("kind").GetString());
        Assert.Equal(6, first.GetProperty("files").GetArrayLength());
        Assert.StartsWith(Path.Combine(Path.GetFullPath(ws), "cache", "previews") + Path.DirectorySeparatorChar, first.GetProperty("files")[0].GetString());
        Assert.Equal(1, reader.Environments);
    }

    [Fact]
    public async Task An_unknown_environment_is_invalid()
    {
        using var game = new FakeGame();
        var (h, _, _) = await Opened(game);

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("assets.environment", new { id = "moon" }));

        Assert.Equal(-32602, ex.Code);
    }
}
