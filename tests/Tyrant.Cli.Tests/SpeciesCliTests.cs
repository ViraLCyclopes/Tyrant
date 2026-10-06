using Tyrant.Cli;
using Tyrant.Core.Tests;

namespace Tyrant.Cli.Tests;

public class SpeciesCliTests
{
    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var (oldOut, oldErr) = (Console.Out, Console.Error);
        var (o, e) = (new StringWriter(), new StringWriter());
        Console.SetOut(o);
        Console.SetError(e);
        try { return (CliApp.Run(args), o.ToString(), e.ToString()); }
        finally { Console.SetOut(oldOut); Console.SetError(oldErr); }
    }

    private static string IndexedWorkspace(FakeGame game)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws");
        Assert.Equal(ExitCodes.Ok, Run("workspace", "init", dir, "--game", game.Root).Code);
        Assert.Equal(ExitCodes.Ok, Run("assets", "index", "-w", dir).Code);
        return dir;
    }

    [Fact]
    public void Species_list_on_install_without_animals_says_so()
    {
        using var game = new FakeGame();
        var (code, output, _) = Run("species", "list", "-w", IndexedWorkspace(game));
        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("No species", output);
    }

    [Fact]
    public void Species_pack_unknown_name_reports_not_found()
    {
        using var game = new FakeGame();
        var (code, _, err) = Run("species", "pack", "stegosaurus", "-w", IndexedWorkspace(game));
        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("ASSET_NOT_FOUND", err);
    }

    [Fact]
    public void Export_model_unknown_key_reports_not_found()
    {
        using var game = new FakeGame();
        var (code, _, err) = Run("assets", "export-model", "nope", "-w", IndexedWorkspace(game));
        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("ASSET_NOT_FOUND", err);
    }

    [Theory]
    [InlineData("species", "pack", "stegosaurus")]
    [InlineData("assets", "export-model", "nope")]
    public void Fbx_without_blender_says_fbx_needs_blender_before_anything_else(string group, string command, string key)
    {
        using var game = new FakeGame();
        var (code, _, err) = Run(group, command, key, "-w", IndexedWorkspace(game), "--format", "fbx");
        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("BLENDER_MISSING", err);
        Assert.Contains("FBX needs Blender", err);
    }

    [Fact]
    public void An_unknown_format_is_refused()
    {
        using var game = new FakeGame();
        var (code, _, err) = Run("assets", "export-model", "nope", "-w", IndexedWorkspace(game), "--format", "obj");
        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("glb, fbx or both", err);
    }

    [Fact]
    public void Species_ik_is_listed_and_an_unknown_species_reports_not_found()
    {
        Assert.Contains("ik", Run("species", "--help").Out);
        using var game = new FakeGame();
        var (code, _, err) = Run("species", "ik", "stegosaurus", "-w", IndexedWorkspace(game));
        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("ASSET_NOT_FOUND", err);
    }

    [Fact]
    public void Species_ik_lines_are_plain_ascii_for_any_console()
    {
        var chain = new Tyrant.Core.Blender.BlenderIkChain("Leg L", "limb", "L", false,
            [new("Femur.L", 0.5f), new("Calve.L", 0.4f), new("Heel.L", 0f)], 1f, [0f, 0f, 0f],
            [new("Calve.L", "Pelvis", [0f, 0f, 1f], 0.4f)], false, new("ctrl_foot.L", "ctrl_knee.L", null));

        var line = Tyrant.Cli.Commands.SpeciesIkCommand.Describe(chain);

        Assert.Contains("Femur.L -> Calve.L -> Heel.L, pole from Pelvis", line);
        Assert.All(line, c => Assert.True(c < 128, $"non-ASCII '{c}' in: {line}"));
    }

    [Fact]
    public void Species_rig_info_lists_moved_and_growth_bones_in_ascii()
    {
        var info = new Tyrant.Core.Rigging.RigInfo(["MainBone", "Pelvis", "Neck.002"], ["Pelvis"], ["Neck.002"], [], ["The game's animations for this species were not found"]);

        var lines = Tyrant.Cli.Commands.SpeciesRigInfoCommand.Lines(info).ToList();

        Assert.Contains("Moved by the game's animations: Pelvis", lines);
        Assert.Contains("Positioned by growth: Neck.002", lines);
        Assert.Contains("Scaled by growth: (none)", lines);
        Assert.Contains(lines, l => l.StartsWith("  WARN", StringComparison.Ordinal));
        Assert.All(string.Join("", lines), c => Assert.True(c < 128, $"non-ASCII '{c}'"));
    }

    [Fact]
    public void Species_animations_lines_show_name_length_rate_and_flags_in_ascii()
    {
        var lines = Tyrant.Cli.Commands.SpeciesAnimationsCommand.Lines([
            new Tyrant.Core.Animation.AnimationInfo("Carch|LocWalk", "LocWalk", 2.5f, 24, true, true),
            new Tyrant.Core.Animation.AnimationInfo("Carch|Roar", "Roar", 3f, 30, false, false),
        ]).ToList();

        Assert.Contains(lines, l => l.Contains("LocWalk") && l.Contains("2.50 s") && l.Contains("24 fps") && l.Contains("loops") && l.Contains("travels"));
        Assert.Contains(lines, l => l.Contains("Roar") && !l.Contains("loops"));
        Assert.Contains(lines, l => l.Contains("2 animation(s)"));
        Assert.All(string.Join("", lines), c => Assert.True(c < 128, $"non-ASCII '{c}'"));
    }
}
