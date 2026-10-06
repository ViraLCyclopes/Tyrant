using System.ComponentModel;
using System.Text.Json;
using Spectre.Console.Cli;
using Tyrant.Core.Blender;
using Tyrant.Core.Errors;

namespace Tyrant.Cli.Commands;

// Blender: the same operations as the Workspace tab's Blender card and the Open in Blender buttons; send and destinations
// answer in JSON for Tyrant's Blender add-on.

public sealed class BlenderStatusCommand : Command<WorkspaceSettings>
{
    public override int Execute(CommandContext context, WorkspaceSettings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        BlenderCli.Print(new BlenderService(CliServices.Blender).Status(ws));
        return ExitCodes.Ok;
    }
}

public sealed class BlenderSetPathCommand : Command<BlenderSetPathCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandArgument(0, "[EXE]")]
        [Description("The blender.exe to use.")]
        public string? Exe { get; set; }

        [CommandOption("--clear")]
        [Description("Forget the chosen blender.exe and find Blender again.")]
        public bool Clear { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        if (settings.Exe is null && !settings.Clear)
        {
            Console.Error.WriteLine("error: give the path of blender.exe, or --clear.");
            return ExitCodes.Usage;
        }
        var (ws, _) = CliServices.OpenWorkspace(settings);
        BlenderCli.Print(new BlenderService(CliServices.Blender).SetPath(ws, settings.Clear ? null : Path.GetFullPath(settings.Exe!)));
        return ExitCodes.Ok;
    }
}

public sealed class BlenderInstallAddonCommand : Command<WorkspaceSettings>
{
    public override int Execute(CommandContext context, WorkspaceSettings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var status = new BlenderService(CliServices.Blender).InstallAddon(ws);
        Console.WriteLine($"Installed Tyrant's add-on {status.AddonInstalled} into Blender {status.Version}. Restart Blender if it is open.");
        return ExitCodes.Ok;
    }
}

public sealed class BlenderOpenCommand : Command<BlenderOpenCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandOption("--species <ID>")]
        [Description("The species id, e.g. Carcharodontosaurus.")]
        public string Species { get; set; } = "";

        [CommandOption("--skin <SKIN>")]
        [Description("With --mod: the mod skin's id. Without: a game skin's name or number.")]
        public string? Skin { get; set; }

        [CommandOption("--mod <ID>")]
        [Description("Open the model or skin in this mod.")]
        public string? Mod { get; set; }

        [CommandOption("--fresh")]
        [Description("Rebuild the project (keeps your old .blend as .old.blend).")]
        public bool Fresh { get; set; }

        [CommandOption("--lods")]
        [Description("Also bring the far levels of detail.")]
        public bool Lods { get; set; }

        [CommandOption("--prefab <REF>")]
        [Description("Open any GameObject (a fence, a building) by its reference from 'tyrant assets list', instead of --species.")]
        public string? Prefab { get; set; }

        [CommandOption("--sex <SEX>")]
        [Description("male (default) or female: the sex Blender shows first (its maps and growth; the Tyrant panel switches it).")]
        public string Sex { get; set; } = "male";

        [CommandOption("--animations <ID>")]
        [Description("Open with these animations as Actions (ids from 'tyrant species animations --json'); repeatable.")]
        public string[] Animations { get; set; } = [];

        [CommandOption("--no-ik")]
        [Description("Build no IK controls (the add-on's Add IK controls can still add them later).")]
        public bool NoIk { get; set; }

        [CommandOption("--no-launch")]
        [Description("Only write the project and print its path.")]
        public bool NoLaunch { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var service = new BlenderService(CliServices.Blender);
        var request = new BlenderOpenRequest(settings.Species, settings.Skin, settings.Mod, settings.Fresh, settings.Lods,
            string.Equals(settings.Sex, "female", StringComparison.OrdinalIgnoreCase) ? "female" : "male") { PrefabRef = settings.Prefab, Ik = !settings.NoIk, Animations = settings.Animations };
        if (!settings.NoLaunch) service.CheckReady(ws); // before the index and data, so a missing Blender is said first
        var index = CliServices.LoadIndex(ws, install);
        var species = ModCli.RequireSpecies(ws);
        if (settings.NoLaunch)
        {
            var written = BlenderProjectWriter.Write(request, ws, install, index, species, CliServices.AssetReader, CliServices.Blender.TyrantExe);
            Console.WriteLine($"Project: {written.ProjectFile}");
            return ExitCodes.Ok;
        }
        var result = service.Open(ws, install, index, species, CliServices.AssetReader, request);
        Console.WriteLine($"Project: {result.ProjectFile}");
        if (result.GameChanged) Console.WriteLine("  NOTE   The game was updated since this project was made: use --fresh to start from the new model.");
        Console.WriteLine(result.How == "running" ? "Opened in the running Blender." : "Started Blender.");
        return ExitCodes.Ok;
    }
}

public sealed class BlenderDestinationsCommand : Command<BlenderDestinationsCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandArgument(0, "<PROJECT>")]
        [Description("The project's tyrant-blender.json.")]
        public string Project { get; set; } = "";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var project = BlenderProjectFile.Read(settings.Project);
        Console.WriteLine(JsonSerializer.Serialize(new { species = project.Source.Species, mods = BlenderService.Destinations(ws, project.Source.Species) }, BlenderCli.Json));
        return ExitCodes.Ok;
    }
}

/// <summary>For the add-on's Add IK controls on a project written before Tyrant read the game's IK chains: writes them into it.</summary>
public sealed class BlenderIkDataCommand : Command<BlenderIkDataCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandArgument(0, "<PROJECT>")]
        [Description("The project's tyrant-blender.json.")]
        public string Project { get; set; } = "";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        try
        {
            var (ws, install) = CliServices.OpenWorkspace(settings);
            var chains = BlenderService.RefreshIk(install, CliServices.LoadIndex(ws, install), ModCli.RequireSpecies(ws), CliServices.AssetReader, settings.Project);
            Console.WriteLine(JsonSerializer.Serialize(new { ok = true, chains }, BlenderCli.Json));
            return ExitCodes.Ok;
        }
        catch (TyrantException ex)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { ok = false, errors = new[] { ex.Message } }, BlenderCli.Json));
            return ExitCodes.Error;
        }
    }
}

/// <summary>Writes animations (clip ids) next to a Blender project for the add-on's Add animations; one JSON line (files, errors).</summary>
public sealed class BlenderAnimationsCommand : Command<BlenderAnimationsCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandArgument(0, "<PROJECT>")]
        [Description("The project's tyrant-blender.json.")]
        public string Project { get; set; } = "";

        [CommandArgument(1, "<ID>")]
        [Description("Animation ids, e.g. Carch|LocWalk (see 'tyrant species animations --json').")]
        public string[] Ids { get; set; } = [];
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        try
        {
            var (ws, install) = CliServices.OpenWorkspace(settings);
            var (files, errors) = BlenderService.Animations(ws, install, CliServices.LoadIndex(ws, install), ModCli.RequireSpecies(ws), CliServices.AssetReader,
                settings.Project, settings.Ids);
            Console.WriteLine(JsonSerializer.Serialize(new { ok = files.Count > 0 || errors.Count == 0, files, errors }, BlenderCli.Json));
            return files.Count > 0 || errors.Count == 0 ? ExitCodes.Ok : ExitCodes.Error;
        }
        catch (TyrantException ex)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { ok = false, files = Array.Empty<string>(), errors = new[] { ex.Message } }, BlenderCli.Json));
            return ExitCodes.Error;
        }
    }
}

/// <summary>Writes the species' animation list into an older Blender project (one JSON line), as ik-data does for IK chains.</summary>
public sealed class BlenderAnimationListCommand : Command<BlenderAnimationListCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandArgument(0, "<PROJECT>")]
        [Description("The project's tyrant-blender.json.")]
        public string Project { get; set; } = "";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        try
        {
            var (ws, install) = CliServices.OpenWorkspace(settings);
            var count = BlenderService.RefreshAnimations(ws, install, CliServices.LoadIndex(ws, install), ModCli.RequireSpecies(ws), CliServices.AssetReader, settings.Project);
            Console.WriteLine(JsonSerializer.Serialize(new { ok = true, animations = count }, BlenderCli.Json));
            return ExitCodes.Ok;
        }
        catch (TyrantException ex)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { ok = false, errors = new[] { ex.Message } }, BlenderCli.Json));
            return ExitCodes.Error;
        }
    }
}

public sealed class BlenderSendCommand : Command<BlenderSendCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandArgument(0, "<PROJECT>")]
        [Description("The project's tyrant-blender.json.")]
        public string Project { get; set; } = "";

        [CommandArgument(1, "<GLB>")]
        [Description("The model exported from that project.")]
        public string Glb { get; set; } = "";

        [CommandOption("--mod <ID>")]
        [Description("Send it to this mod (saved in the project for next time).")]
        public string? Mod { get; set; }

        [CommandOption("--skin <ID>")]
        [Description("With --mod: give this skin the model instead of replacing the species' model.")]
        public string? Skin { get; set; }

        [CommandOption("--new-mod-name <NAME>")]
        [Description("With --mod: create the mod with this name when it does not exist.")]
        public string? NewModName { get; set; }

        [CommandOption("--image <SLOT=PNG>")]
        [Description("A changed image for a skin slot (diffuse, normal, extra, pattern, fur, infantDiffuse, …); repeatable.")]
        public string[] Image { get; set; } = [];

        [CommandOption("--sex <SEX>")]
        [Description("The sex shown in Blender (male or female): whose maps the images go to.")]
        public string Sex { get; set; } = "male";

        [CommandOption("--rig <FILE>")]
        [Description("The armature's rig edit (a JSON object of bones, as mod.json's \"rig\"); {} clears the destination's. Without it the destination keeps its rig edit.")]
        public string? Rig { get; set; }

        [CommandOption("--rig-only")]
        [Description("With --rig: send only the rig edit; the destination wears the game's mesh.")]
        public bool RigOnly { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        // Always one JSON line on stdout: the add-on shows it in Blender's Tyrant panel.
        BlenderSendResult result;
        try
        {
            var (ws, install) = CliServices.OpenWorkspace(settings);
            var project = BlenderProjectFile.Read(settings.Project);
            var choose = settings.Mod is null ? null : new BlenderDestination(settings.Mod, project.Source.Species, settings.Skin);
            if (settings.Sex is not ("male" or "female"))
                throw new TyrantException(TyrantErrorCode.ModInvalid, $"--sex must be male or female (got '{settings.Sex}').");
            var images = settings.Image.Select(i =>
            {
                var at = i.IndexOf('=');
                return at > 0 && at < i.Length - 1 ? new BlenderImage(i[..at], Path.GetFullPath(i[(at + 1)..]))
                    : throw new TyrantException(TyrantErrorCode.ModInvalid, $"--image takes <slot>=<png> (got '{i}').");
            }).ToList();
            IReadOnlyDictionary<string, Tyrant.Framework.Core.RigOffset>? rig = null;
            if (settings.Rig is not null)
            {
                try
                {
                    rig = Tyrant.Framework.Core.RigEdit.Parse(Tyrant.Framework.Core.Json.Parse(File.ReadAllText(settings.Rig)), "The rig edit");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or Tyrant.Framework.Core.ManifestException)
                {
                    throw new TyrantException(TyrantErrorCode.ModInvalid, $"The rig edit could not be read ({ex.Message}).");
                }
            }
            result = BlenderService.Send(ws, install, CliServices.LoadIndex(ws, install), ModCli.RequireSpecies(ws), CliServices.AssetReader,
                settings.Project, Path.GetFullPath(settings.Glb), choose, settings.NewModName, images, settings.Sex, rig, settings.RigOnly);
        }
        catch (TyrantException ex)
        {
            result = new BlenderSendResult(false, [ex.Message], [], [], null);
        }
        Console.WriteLine(JsonSerializer.Serialize(result, BlenderCli.Json));
        return result.Ok ? ExitCodes.Ok : ExitCodes.Error;
    }
}

internal static class BlenderCli
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void Print(BlenderStatus s)
    {
        if (!s.Found)
            Console.WriteLine($"Blender: not found{(s.MissingConfigured is { } gone ? $" (not at {gone} any more)" : "")} — choose it on the Workspace tab (Blender card) or run 'tyrant blender set-path <blender.exe>'.");
        else
            Console.WriteLine($"Blender: {s.Exe} ({s.Version}){(s.Supported ? "" : " — " + s.Problem)}");
        Console.WriteLine(s.Addon switch
        {
            "missing" => $"Add-on: not installed · {s.AddonBundled} in this Tyrant — Install add-on on the Workspace tab, or 'tyrant blender install-addon'.",
            "older" => $"Add-on: {s.AddonInstalled} in Blender · {s.AddonBundled} in this Tyrant — Update add-on on the Workspace tab, or 'tyrant blender install-addon'.",
            "changed" => $"Add-on: an older build of {s.AddonInstalled} in Blender — Update add-on on the Workspace tab, or 'tyrant blender install-addon'.",
            "unknown" => "Add-on: unknown (no Blender)",
            _ => $"Add-on: {s.AddonInstalled} in Blender · {s.AddonBundled} in this Tyrant",
        });
    }
}
