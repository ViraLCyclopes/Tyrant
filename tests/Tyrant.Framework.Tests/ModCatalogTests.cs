using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class ModCatalogTests
{
    private static ModSource Mod(string id, string extra = "", string? folder = null) =>
        new(folder ?? id, @"C:\game\UserData\Tyrant\Mods\" + (folder ?? id), $$"""{ "format": 1, "id": "{{id}}"{{extra}} }""");

    private static string[] Ids(LoadPlan plan) => plan.Mods.Select(m => m.Manifest.Id).ToArray();

    [Fact]
    public void Listed_mods_load_in_list_order_then_unlisted_mods_by_id()
    {
        var list = """{ "format": 1, "mods": [ { "id": "zeta", "enabled": true }, { "id": "alpha", "enabled": true } ] }""";

        var plan = ModCatalog.Plan([Mod("alpha"), Mod("mid"), Mod("zeta"), Mod("beta")], list, "0.1.0");

        Assert.Equal(new[] { "zeta", "alpha", "beta", "mid" }, Ids(plan));
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Disabled_mods_are_skipped_as_disabled()
    {
        var list = """{ "format": 1, "mods": [ { "id": "alpha", "enabled": false } ] }""";

        var plan = ModCatalog.Plan([Mod("alpha"), Mod("beta")], list, "0.1.0");

        Assert.Equal(new[] { "beta" }, Ids(plan));
        Assert.Equal(("alpha", "turned off"), (plan.Skipped[0].Id, plan.Skipped[0].Reason));
    }

    [Fact]
    public void A_broken_mod_is_skipped_with_its_reason_and_the_rest_load()
    {
        var broken = new ModSource("broken", @"C:\m\broken", "{ \"format\": 1, ");
        var missing = new ModSource("missing", @"C:\m\missing", null);

        var plan = ModCatalog.Plan([broken, missing, Mod("fine")], null, "0.1.0");

        Assert.Equal(new[] { "fine" }, Ids(plan));
        Assert.Contains(plan.Skipped, s => s.Id == "broken" && s.Reason.Contains("not valid JSON"));
        Assert.Contains(plan.Skipped, s => s.Id == "missing" && s.Reason.Contains("mod.json is missing"));
    }

    [Fact]
    public void A_mod_whose_id_does_not_match_its_folder_is_skipped()
    {
        var plan = ModCatalog.Plan([Mod("alpha", folder: "alpha-copy")], null, "0.1.0");

        Assert.Empty(plan.Mods);
        Assert.Contains("does not match its folder", Assert.Single(plan.Skipped).Reason);
    }

    [Fact]
    public void A_mod_needing_a_newer_framework_is_skipped()
    {
        var plan = ModCatalog.Plan([Mod("future", """, "requires": { "tyrant": ">=0.3" }"""), Mod("now", """, "requires": { "tyrant": "0.1" }""")], null, "0.1.0");

        Assert.Equal(new[] { "now" }, Ids(plan));
        Assert.Contains("0.3", Assert.Single(plan.Skipped).Reason);
    }

    [Fact]
    public void Missing_or_turned_off_dependencies_skip_the_mod_and_mods_that_need_it()
    {
        var list = """{ "format": 1, "mods": [ { "id": "base", "enabled": false } ] }""";

        var plan = ModCatalog.Plan([Mod("base"), Mod("child", """, "dependencies": ["base"]"""), Mod("grandchild", """, "dependencies": ["child"]"""),
            Mod("lonely", """, "dependencies": ["nowhere"]""")], list, "0.1.0");

        Assert.Empty(plan.Mods);
        Assert.Contains(plan.Skipped, s => s.Id == "child" && s.Reason.Contains("\"base\""));
        Assert.Contains(plan.Skipped, s => s.Id == "grandchild" && s.Reason.Contains("\"child\""));
        Assert.Contains(plan.Skipped, s => s.Id == "lonely" && s.Reason.Contains("\"nowhere\""));
    }

    [Fact]
    public void An_unreadable_mods_list_is_a_warning_and_every_mod_loads()
    {
        var plan = ModCatalog.Plan([Mod("alpha")], "not json", "0.1.0");

        Assert.Equal(new[] { "alpha" }, Ids(plan));
        Assert.Contains("mods.json", Assert.Single(plan.Warnings));
    }

    [Fact]
    public void Mods_list_round_trips()
    {
        var entries = ModList.Parse(ModList.ToJson([new ModListEntry("alpha", true), new ModListEntry("beta", false)]));

        Assert.Equal(new[] { ("alpha", true), ("beta", false) }, entries.Select(e => (e.Id, e.Enabled)));
        Assert.Empty(ModList.Parse(null));
    }

    [Theory]
    [InlineData("0.1.0", "0.1", 0)]
    [InlineData("0.1.0", "0.2", -1)]
    [InlineData("1.0", "0.9.9", 1)]
    [InlineData("0.10", "0.9", 1)]
    public void Versions_compare_by_number(string a, string b, int sign)
    {
        Assert.Equal(sign, Math.Sign(VersionText.Compare(a, b)));
    }
}
