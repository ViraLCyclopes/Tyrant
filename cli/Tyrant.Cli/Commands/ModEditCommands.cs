using System.ComponentModel;
using Spectre.Console.Cli;
using Tyrant.Core.Mods;
using Tyrant.Framework.Core;

namespace Tyrant.Cli.Commands;

// Editing a mod in place: the same operations as the app's mod editor.

public sealed class ModShowCommand : Command<ModSettings>
{
    public override int Execute(CommandContext context, ModSettings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var m = ModProject.Open(ws, settings.Id).Manifest;
        Console.WriteLine($"{m.Name} ({m.Id}) {m.Version}{(m.Author is null ? "" : $" by {m.Author}")}");
        if (m.Description is not null) Console.WriteLine($"  {m.Description}");
        Console.WriteLine($"Replacements ({m.Replace.Count}):");
        foreach (var r in m.Replace) Console.WriteLine($"  {r.Texture} <- {r.File}");
        Console.WriteLine($"Skins ({m.Skins.Count}):");
        foreach (var s in m.Skins)
        {
            var files = (s.Male?.Count ?? 0) + (s.Female?.Count ?? 0);
            Console.WriteLine($"  {s.Id}  \"{s.Name}\"  {s.Species}  base {s.Base}  {files} file(s)  colours: {(s.Colors is null ? "no" : "yes")}{(s.Model is null ? "" : $"  model: {s.Model}")}");
        }
        Console.WriteLine($"Models ({m.Models.Count}):");
        foreach (var model in m.Models) Console.WriteLine($"  {model.Target} <- {model.File}");
        return ExitCodes.Ok;
    }
}

public sealed class ModSetCommand : Command<ModSetCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandOption("--name <NAME>")]
        [Description("The mod's shown name.")]
        public string? Name { get; set; }

        [CommandOption("--version <VERSION>")]
        [Description("The mod's version, e.g. 1.1.0.")]
        public string? Version { get; set; }

        [CommandOption("--author <AUTHOR>")]
        [Description("Who made it (\"\" clears it).")]
        public string? Author { get; set; }

        [CommandOption("--description <TEXT>")]
        [Description("One or two sentences about it (\"\" clears it).")]
        public string? Description { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        var m = mod.Manifest;
        mod.SetDetails(settings.Name ?? m.Name, settings.Version ?? m.Version, settings.Author ?? m.Author, settings.Description ?? m.Description);
        Console.WriteLine($"Saved the details of '{m.Id}'.");
        return ExitCodes.Ok;
    }
}

public class ModSkinSettings : ModSettings
{
    [CommandArgument(1, "<SKIN>")]
    [Description("The skin's id (see 'tyrant mod show').")]
    public string Skin { get; set; } = "";
}

public sealed class ModRenameSkinCommand : Command<ModRenameSkinCommand.Settings>
{
    public sealed class Settings : ModSkinSettings
    {
        [CommandArgument(2, "<NAME>")]
        [Description("The new shown name (the skin's id stays).")]
        public string Name { get; set; } = "";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        ModProject.Open(ws, settings.Id).RenameSkin(settings.Skin, settings.Name);
        Console.WriteLine($"'{settings.Skin}' is now called '{settings.Name.Trim()}'.");
        return ExitCodes.Ok;
    }
}

public sealed class ModRemoveSkinCommand : Command<ModRemoveSkinCommand.Settings>
{
    public sealed class Settings : ModSkinSettings
    {
        [CommandOption("--delete-files")]
        [Description("Also delete its PNGs (never ones another skin or replacement uses).")]
        public bool DeleteFiles { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var deleted = ModProject.Open(ws, settings.Id).RemoveSkin(settings.Skin, settings.DeleteFiles);
        Console.WriteLine($"Removed skin '{settings.Skin}'. Its number in the game stays reserved.");
        foreach (var file in deleted) Console.WriteLine($"  deleted  {file}");
        return ExitCodes.Ok;
    }
}

public sealed class ModColorsCommand : Command<ModColorsCommand.Settings>
{
    public sealed class Settings : ModSkinSettings
    {
        [CommandOption("--show")]
        [Description("Print the skin's colours as JSON.")]
        public bool Show { get; set; }

        [CommandOption("--set <FILE>")]
        [Description("Set them from a JSON file (the \"colors\" section of mod.json).")]
        public string? Set { get; set; }

        [CommandOption("--clear")]
        [Description("Remove them (the base skin's colours are used).")]
        public bool Clear { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        if ((settings.Show ? 1 : 0) + (settings.Set is null ? 0 : 1) + (settings.Clear ? 1 : 0) != 1)
        {
            Console.Error.WriteLine("Use exactly one of --show, --set <file.json> or --clear.");
            return ExitCodes.Error;
        }
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        if (settings.Show)
        {
            var colors = mod.Skin(settings.Skin).Colors;
            Console.WriteLine(colors is null ? "No colours of its own (the base skin's are used)." : Json.Write(colors.ToJson()));
            return ExitCodes.Ok;
        }
        mod.SetColors(settings.Skin, settings.Clear ? null : File.ReadAllText(settings.Set!));
        Console.WriteLine(settings.Clear ? $"'{settings.Skin}' uses the base skin's colours." : $"Saved the colours of '{settings.Skin}'.");
        return ExitCodes.Ok;
    }
}

public sealed class ModSkinFileCommand : Command<ModSkinFileCommand.Settings>
{
    public sealed class Settings : ModSkinSettings
    {
        [CommandArgument(2, "<SEX>")]
        [Description("male or female.")]
        public string Sex { get; set; } = "";

        [CommandArgument(3, "<SLOT>")]
        [Description("diffuse, normal, extra, pattern, fur, or infantDiffuse … infantFur.")]
        public string Slot { get; set; } = "";

        [CommandArgument(4, "[PNG]")]
        [Description("Your PNG; it is copied into the skin's folder.")]
        public string? Png { get; set; }

        [CommandOption("--base")]
        [Description("Use the base skin's texture for this slot instead.")]
        public bool Base { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        if (settings.Base == (settings.Png is not null))
        {
            Console.Error.WriteLine("Give a PNG or --base (not both).");
            return ExitCodes.Error;
        }
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var file = ModProject.Open(ws, settings.Id).SetSkinFile(settings.Skin, settings.Sex, settings.Slot, settings.Png);
        Console.WriteLine(file is null ? $"'{settings.Skin}' {settings.Sex} {settings.Slot} uses the base skin's texture." : $"  {settings.Sex} {settings.Slot} <- {file}");
        return ExitCodes.Ok;
    }
}

public sealed class ModThumbnailCommand : Command<ModThumbnailCommand.Settings>
{
    public sealed class Settings : ModSkinSettings
    {
        [CommandArgument(2, "[PNG]")]
        [Description("A small PNG shown as the skin's swatch in the game.")]
        public string? Png { get; set; }

        [CommandOption("--auto")]
        [Description("Let the game cut a swatch from the diffuse.")]
        public bool Auto { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        if (settings.Auto == (settings.Png is not null))
        {
            Console.Error.WriteLine("Give a PNG or --auto (not both).");
            return ExitCodes.Error;
        }
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var file = ModProject.Open(ws, settings.Id).SetThumbnail(settings.Skin, settings.Png);
        Console.WriteLine(file is null ? $"'{settings.Skin}' gets an automatic swatch." : $"  thumbnail <- {file}");
        return ExitCodes.Ok;
    }
}

public sealed class ModUnreplaceCommand : Command<ModUnreplaceCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandArgument(1, "<TEXTURE>")]
        [Description("The replaced game texture's name.")]
        public string Texture { get; set; } = "";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        ModProject.Open(ws, settings.Id).RemoveReplacement(settings.Texture);
        Console.WriteLine($"'{settings.Id}' no longer replaces {settings.Texture} (its PNG stays in textures/).");
        return ExitCodes.Ok;
    }
}
