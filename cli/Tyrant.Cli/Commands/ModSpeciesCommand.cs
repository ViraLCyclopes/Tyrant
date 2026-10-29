using System.ComponentModel;
using Spectre.Console.Cli;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;

namespace Tyrant.Cli.Commands;

public sealed class ModAddSpeciesCommand : Command<ModAddSpeciesCommand.Settings>
{
    public sealed class Settings : ModSettings, IStartingSkinOptions
    {
        [CommandArgument(1, "<SPECIES-ID>")]
        public string SpeciesId { get; set; } = "";
        [CommandOption("--start-from <SPECIES>")]
        [Description("Its starting skin copies this base-game species' skin, as SPECIES or SPECIES/SKIN (number or name); default: the template's starting skin.")]
        public string? StartFrom { get; set; }
        [CommandOption("--start-png <PNG>")]
        [Description("Your own diffuse PNG for the starting skin instead (the other maps come from the template skin).")]
        public string? StartPng { get; set; }
        [CommandOption("--start-png-female <PNG>")]
        [Description("The female's own diffuse PNG (default: the --start-png one).")]
        public string? StartPngFemale { get; set; }
        [CommandOption("--start-name <NAME>")]
        [Description("The starting skin's name in game (default: the copied skin's name, or the species name).")]
        public string? StartName { get; set; }
        [CommandArgument(2, "<TEMPLATE>")]
        public string Template { get; set; } = "";
        [CommandOption("--name <NAME>")]
        [Description("The new animal's display name.")]
        public string Name { get; set; } = "";
        [CommandOption("--genus <GENUS>")]
        public string Genus { get; set; } = "";
        [CommandOption("--epithet <EPITHET>")]
        public string Epithet { get; set; } = "";
        [CommandOption("--cost <COST>")]
        public int? Cost { get; set; }
        [CommandOption("--starting-skin <SKIN>")]
        [Description("The template skin the species starts with, by name or number (default: the first).")]
        public string? StartingSkin { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        var species = SpeciesSkinsReader.Load(ws);
        var template = species.FirstOrDefault(s => string.Equals(s.SpeciesId, settings.Template, StringComparison.OrdinalIgnoreCase));
        var startingSkin = settings.StartingSkin is null ? 0 : StartingSkinOption.Resolve(species, settings.Template, settings.StartingSkin);
        var source = StartingSkinOptions.Source(species, settings, template?.SpeciesId ?? settings.Template, startingSkin);
        var entry = mod.CreateSpecies(install, CliServices.LoadIndex(ws, install), CliServices.AssetReader, species, settings.SpeciesId, settings.Template,
            settings.Name, settings.Genus, settings.Epithet, settings.Cost, null, SpeciesDatabase.Template(ws, template?.SpeciesId ?? settings.Template),
            startingSkin, source, settings.StartName);
        var skin = mod.StartingSkinOf(entry.Id)!;
        Console.WriteLine($"Added {entry.Name}: {entry.Key(mod.Id)} (template {entry.Base}).");
        Console.WriteLine($"Starting skin: {skin.Id} ({skin.Name}).");
        Console.WriteLine("Experimental species support: test spawning and save/reload in a separate park before using it in your main park.");
        return ExitCodes.Ok;
    }
}

internal static class StartingSkinOption
{
    /// <summary>A template skin by number or name (case-insensitive), as --starting-skin takes it.</summary>
    public static int Resolve(IReadOnlyList<SpeciesSkins> vanilla, string template, string skin)
    {
        var row = vanilla.FirstOrDefault(s => string.Equals(s.SpeciesId, template, StringComparison.OrdinalIgnoreCase))
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, $"'{template}' is not in the game data dump.");
        if (int.TryParse(skin, out var number)) return number; // AddSpecies / SetSpeciesStartingSkin report a missing number
        return row.Skins.FirstOrDefault(s => string.Equals(s.Name, skin, StringComparison.OrdinalIgnoreCase))?.Index
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, $"{row.SpeciesId} has no skin '{skin}'. Its skins: {string.Join(", ", row.Skins.Select(s => $"{s.Index} {s.Name}"))}.");
    }
}

public sealed class ModSetSpeciesSkinCommand : Command<ModSetSpeciesSkinCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandArgument(1, "<SPECIES-ID>")]
        public string SpeciesId { get; set; } = "";
        [CommandOption("--starting-skin <SKIN>")]
        [Description("The template skin to start with, by name or number.")]
        public string? StartingSkin { get; set; }
        [CommandOption("--name <NAME>")]
        [Description("The starting skin's name in game (\"\" = the template skin's name).")]
        public string? Name { get; set; }
        [CommandOption("--swatch <PNG>")]
        [Description("A PNG for the starting skin's swatch in the skin picker.")]
        public string? Swatch { get; set; }
        [CommandOption("--no-swatch")]
        [Description("Go back to the template skin's swatch.")]
        public bool NoSwatch { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        var entry = mod.Manifest.Species.FirstOrDefault(s => s.Id == settings.SpeciesId)
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, $"No species '{settings.SpeciesId}' in mod '{mod.Id}'.");
        if (settings.Swatch is not null && settings.NoSwatch) throw new TyrantException(TyrantErrorCode.ModInvalid, "Use --swatch or --no-swatch, not both.");
        if ((settings.Name is not null || settings.Swatch is not null || settings.NoSwatch) && mod.StartingSkinOf(entry.Id) is { } own)
            throw new TyrantException(TyrantErrorCode.ModInvalid,
                $"{entry.Id} has its own starting skin '{own.Id}': rename it with 'mod rename-skin' and set its swatch with 'mod thumbnail'.");
        if (settings.StartingSkin is not null || settings.Name is not null)
        {
            var vanilla = SpeciesSkinsReader.Load(ws);
            var skin = settings.StartingSkin is null ? entry.StartingSkin : StartingSkinOption.Resolve(vanilla, entry.Base, settings.StartingSkin);
            mod.SetSpeciesStartingSkin(vanilla, entry.Id, skin, settings.Name ?? entry.StartingSkinName);
        }
        if (settings.Swatch is not null) mod.SetSpeciesSwatch(entry.Id, settings.Swatch);
        if (settings.NoSwatch) mod.SetSpeciesSwatch(entry.Id, null);
        entry = mod.Manifest.Species.First(s => s.Id == settings.SpeciesId);
        Console.WriteLine($"{entry.Key(mod.Id)}: starting skin {entry.StartingSkin}{(entry.StartingSkinName is null ? "" : $" \"{entry.StartingSkinName}\"")}{(entry.Swatch is null ? "" : $", swatch {entry.Swatch}")}.");
        return ExitCodes.Ok;
    }
}

public sealed class ModSpeciesPortraitCommand : Command<ModSpeciesPortraitCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandArgument(1, "<SPECIES-ID>")]
        public string SpeciesId { get; set; } = "";

        [CommandOption("--slot <SLOT>")]
        [Description("adultMale, adultMaleAlbino, adultMaleMelanistic, adultMaleLeucistic, the same for adultFemale, infant, infantAlbino, infantMelanistic, infantLeucistic.")]
        public string Slot { get; set; } = "";

        [CommandOption("--png <FILE>")]
        [Description("The portrait PNG (the game's are 128x64).")]
        public string? Png { get; set; }

        [CommandOption("--clear")]
        [Description("Show the base skin's portrait again.")]
        public bool Clear { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        if (settings.Clear == (settings.Png is not null))
        {
            Console.Error.WriteLine("Give --png or --clear (not both).");
            return ExitCodes.Error;
        }
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var portraits = ModProject.Open(ws, settings.Id).SetSpeciesPortraits(settings.SpeciesId,
            new Dictionary<string, PortraitSource?> { [settings.Slot] = settings.Clear ? null : new PortraitSource(Png: settings.Png) });
        Console.WriteLine(portraits.TryGetValue(settings.Slot, out var file) ? $"  {settings.Slot} <- {file}" : $"'{settings.SpeciesId}' {settings.Slot} uses the game's portrait.");
        return ExitCodes.Ok;
    }
}

/// <summary>The Nursery picture (the game's heroRender). Its command keeps its old name, set-species-photo.</summary>
public sealed class ModSpeciesPhotoCommand : Command<ModSpeciesPhotoCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandArgument(1, "<SPECIES-ID>")]
        [Description("The species' id in the mod (see 'tyrant mod show').")]
        public string SpeciesId { get; set; } = "";

        [CommandOption("--png <FILE>")]
        [Description("The picture PNG (the game's are 512x256), shown on the species' Nursery card and in the Nursery's species panel.")]
        public string? Png { get; set; }

        [CommandOption("--clear")]
        [Description("Show the template's Nursery picture again.")]
        public bool Clear { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        if (settings.Clear == (settings.Png is not null))
        {
            Console.Error.WriteLine("Give --png or --clear (not both).");
            return ExitCodes.Error;
        }
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        mod.SetSpeciesIcon(settings.SpeciesId, settings.Png);
        var picture = mod.Manifest.Species.First(s => s.Id == settings.SpeciesId).Icon;
        Console.WriteLine(picture is null ? $"{settings.SpeciesId} uses its template's Nursery picture." : $"{settings.SpeciesId}: Nursery picture <- {picture}");
        return ExitCodes.Ok;
    }
}

public sealed class ModSpeciesPaleopediaPictureCommand : Command<ModSpeciesPaleopediaPictureCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandArgument(1, "<SPECIES-ID>")]
        [Description("The species' id in the mod (see 'tyrant mod show').")]
        public string SpeciesId { get; set; } = "";

        [CommandOption("--png <FILE>")]
        [Description("The picture PNG for the species' Paleopedia page (the game's are 2048x1152, 16:9).")]
        public string? Png { get; set; }

        [CommandOption("--clear")]
        [Description("Show the template's Paleopedia picture again.")]
        public bool Clear { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        if (settings.Clear == (settings.Png is not null))
        {
            Console.Error.WriteLine("Give --png or --clear (not both).");
            return ExitCodes.Error;
        }
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        mod.SetSpeciesPaleopediaPicture(settings.SpeciesId, settings.Png);
        var picture = mod.Manifest.Species.First(s => s.Id == settings.SpeciesId).PaleopediaPicture;
        Console.WriteLine(picture is null ? $"{settings.SpeciesId} uses its template's Paleopedia picture." : $"{settings.SpeciesId}: Paleopedia picture <- {picture}");
        return ExitCodes.Ok;
    }
}

public sealed class ModSpeciesTextCommand : Command<ModSpeciesTextCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandArgument(1, "<SPECIES-ID>")]
        [Description("The species' id in the mod (see 'tyrant mod show').")]
        public string SpeciesId { get; set; } = "";

        [CommandArgument(2, "<FIELD>")]
        [Description("description, characterNotes, socialInfo or name (the English name is the species' display name).")]
        public string Field { get; set; } = "";

        [CommandOption("--lang <CODE>")]
        [Description("en-US (default), fr, es, pt, de, ru or zh-CN. Empty languages use the English text.")]
        public string Lang { get; set; } = "en-US";

        [CommandOption("--text <TEXT>")]
        [Description("The text.")]
        public string? Text { get; set; }

        [CommandOption("--file <FILE>")]
        [Description("A UTF-8 text file holding the text (paragraphs and <b>markup</b> are kept).")]
        public string? File { get; set; }

        [CommandOption("--english")]
        [Description("Empty this translation so the language uses the English text.")]
        public bool English { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        if (new[] { settings.Text is not null, settings.File is not null, settings.English }.Count(given => given) != 1)
        {
            Console.Error.WriteLine("Give one of --text, --file or --english.");
            return ExitCodes.Error;
        }
        if (settings.English && settings.Lang == "en-US")
        {
            Console.Error.WriteLine("--english empties a translation; give --lang with another language.");
            return ExitCodes.Error;
        }
        var text = settings.English ? "" : settings.Text ?? System.IO.File.ReadAllText(settings.File!);
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        mod.SetSpeciesTexts(settings.SpeciesId, new Dictionary<string, IReadOnlyDictionary<string, string>>
        { [settings.Lang] = new Dictionary<string, string> { [settings.Field] = text } });
        Console.WriteLine(settings.Lang == "en-US" ? $"{settings.SpeciesId}: en-US {settings.Field} set (every empty language uses it)."
            : text.Length == 0 ? $"{settings.SpeciesId}: {settings.Lang} {settings.Field} uses English." : $"{settings.SpeciesId}: {settings.Lang} {settings.Field} set.");
        return ExitCodes.Ok;
    }
}

public sealed class ModSetStartingSkinCommand : Command<ModSetStartingSkinCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandArgument(1, "<SKIN>")]
        [Description("The skin's id in the mod (see 'tyrant mod show').")]
        public string Skin { get; set; } = "";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var (made, previous) = ModProject.Open(ws, settings.Id).MakeStartingSkin(settings.Skin, key => SkinSlots.NumbersOf(install, key));
        Console.WriteLine($"{made.Name} is now the starting skin of {made.Species}"
            + (previous is null ? "." : $"; {previous.Name} ({previous.Id}) is an ordinary skin."));
        if (previous is not null) Console.WriteLine($"{previous.Name} took its place in the skin picker.");
        Console.WriteLine("Skins based on 0 now take what they have no PNG for from it, and animals with skin 0 in saved parks wear it.");
        return ExitCodes.Ok;
    }
}

public sealed class ModSkinOrderCommand : Command<ModSkinOrderCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandArgument(1, "<SPECIES-ID>")]
        [Description("The custom species' id in the mod.")]
        public string SpeciesId { get; set; } = "";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        ModSkinOrder.Print(ModProject.Open(ws, settings.Id), settings.SpeciesId, install);
        return ExitCodes.Ok;
    }
}

public sealed class ModSetSkinOrderCommand : Command<ModSetSkinOrderCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandArgument(1, "<SPECIES-ID>")]
        [Description("The custom species' id in the mod.")]
        public string SpeciesId { get; set; } = "";

        [CommandArgument(2, "<SKIN>")]
        [Description("The species' skin ids in the order to show them, the starting skin included.")]
        public string[] Skins { get; set; } = [];
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        mod.SetSkinOrder(ModSkinOrder.Key(mod, settings.SpeciesId), settings.Skins);
        ModSkinOrder.Print(mod, settings.SpeciesId, install);
        return ExitCodes.Ok;
    }
}

internal static class ModSkinOrder
{
    public static string Key(ModProject mod, string speciesId) => $"tyrant.{mod.Id}.{speciesId}";

    public static void Print(ModProject mod, string speciesId, GameInstall install)
    {
        var key = Key(mod, speciesId);
        if (!mod.Manifest.Species.Any(s => s.Key(mod.Id) == key))
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"{key} is not one of this mod's species: only their skin order can be shown.");
        var order = mod.ShownSkinOrder(key, SkinSlots.NumbersOf(install, key))
            ?? throw new TyrantException(TyrantErrorCode.ModInvalid, $"{key} needs an editable starting skin first (Make an editable starting skin on its page).");
        var n = 0;
        foreach (var id in order)
        {
            var skin = mod.Skin(id);
            Console.WriteLine($"{++n}. {skin.Name} ({skin.Id})" + (skin.Starting ? " · starting skin" : ""));
        }
    }
}
