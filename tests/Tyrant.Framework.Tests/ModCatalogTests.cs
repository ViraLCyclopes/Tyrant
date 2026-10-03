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

    [Fact]
    public void Discover_turns_an_unreadable_mod_into_a_skipped_mod_and_finds_the_rest()
    {
        var root = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "Mods");
        Directory.CreateDirectory(Path.Combine(root, "fine"));
        File.WriteAllText(Path.Combine(root, "fine", "mod.json"), """{ "format": 1, "id": "fine" }""");
        Directory.CreateDirectory(Path.Combine(root, "locked"));
        var lockedPath = Path.Combine(root, "locked", "mod.json");
        File.WriteAllText(lockedPath, """{ "format": 1, "id": "locked" }""");

        List<ModSource> sources;
        using (File.Open(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None)) // held open elsewhere: reading it fails
            sources = ModCatalog.Discover(root);
        var plan = ModCatalog.Plan(sources, null, "0.1.0");

        Assert.Equal(new[] { "fine" }, Ids(plan));
        Assert.Contains(plan.Skipped, s => s.Id == "locked" && s.Reason.Contains("could not be read"));
        Assert.Empty(ModCatalog.Discover(Path.Combine(root, "nowhere")));
    }

    [Fact]
    public void A_path_that_cannot_be_resolved_is_never_inside_a_mod()
    {
        Assert.False(ModPaths.IsInside(@"C:\m\bad" + "\0" + "name.dll", @"C:\m")); // a NUL makes the path unresolvable
    }

    [Fact]
    public void Tyrants_own_working_folders_are_not_mods()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));
        foreach (var folder in new[] { "red-spot", "red-spot.previous", "red-spot.installing" })
        {
            Directory.CreateDirectory(Path.Combine(dir, folder));
            File.WriteAllText(Path.Combine(dir, folder, "mod.json"), """{ "format": 1, "id": "red-spot" }""");
        }

        var sources = ModCatalog.Discover(dir);

        Assert.Equal(["red-spot"], sources.Select(s => s.Folder));
    }

    [Fact]
    public void A_mod_with_one_bad_skin_still_loads_and_the_skip_is_a_warning()
    {
        var plan = ModCatalog.Plan([new ModSource("mod-id", "D:/mods/mod-id", """
            { "format": 1, "id": "mod-id", "skins": [
              { "id": "good", "species": "S", "male": { "diffuse": "a.png" } },
              { "id": "bad", "species": "S" } ] }
            """)], null, FrameworkInfo.Version);

        Assert.Equal(["good"], Assert.Single(plan.Mods).Manifest.Skins.Select(s => s.Id));
        Assert.Contains(plan.Warnings, w => w.Contains("mod-id") && w.Contains("bad"));
    }
}
