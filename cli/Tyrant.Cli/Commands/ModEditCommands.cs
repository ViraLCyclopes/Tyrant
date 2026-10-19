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
        Console.WriteLine($"Species ({m.Species.Count}):");
        var dump = m.Species.Count > 0 ? ModCli.TrySpecies(ws) : null; // read once, for the template skins' names
        foreach (var entry in m.Species)
        {
            var template = dump?.FirstOrDefault(s => s.SpeciesId == entry.Base)?.Skins.FirstOrDefault(s => s.Index == entry.StartingSkin)?.Name;
            Console.WriteLine($"  {entry.Key(m.Id)}  {entry.Name}  template {entry.Base}  starting skin {entry.StartingSkin} {entry.StartingSkinName ?? template ?? "?"}{(entry.Swatch is null ? "" : $"  swatch {entry.Swatch}")}");
        }
        Console.WriteLine($"Skins ({m.Skins.Count}):");
        foreach (var s in m.Skins)
        {
            var files = (s.Male?.Count ?? 0) + (s.Female?.Count ?? 0);
            Console.WriteLine($"  {s.Id}  \"{s.Name}\"  {s.Species}  base {s.Base}  {files} file(s)  colours: {(s.Colors is null ? "no" : "yes")}  materials: {(s.Materials is null ? "no" : "yes")}{(s.Model is null ? "" : $"  model: {s.Model}")}{(s.Group is null ? "" : $"  group: {s.Group}")}");
        }
        foreach (var use in SpeciesUse.Of(m)) Console.WriteLine($"Uses species from: {use.ModId} ({use.Skins} skin(s))");
        Console.WriteLine($"Models ({m.Models.Count}):");
        foreach (var model in m.Models) Console.WriteLine($"  {model.Target} <- {model.File}");
        Console.WriteLine($"Sounds ({m.Sounds.Count}):");
        foreach (var sound in m.Sounds) Console.WriteLine(SoundCli.Line(sound));
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
        [Description("Also send its PNGs, its own model and its Blender project to the Recycle Bin (never ones another skin, model or replacement uses).")]
        public bool DeleteFiles { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        var fallback = StartingFallback(ws, mod, mod.Skin(settings.Skin));
        var deleted = mod.RemoveSkin(settings.Skin, settings.DeleteFiles);
        Console.WriteLine(fallback is null ? $"Removed skin '{settings.Skin}'. Its number in the game stays reserved." : $"Removed skin '{settings.Skin}'. {fallback}");
        foreach (var file in deleted.Recycled) Console.WriteLine($"  to the Recycle Bin  {file}");
        foreach (var file in deleted.Kept) Console.WriteLine($"  stayed  {file}");
        return ExitCodes.Ok;
    }

    /// <summary>For a species' starting skin: what the species wears once it is gone; null for any other skin.</summary>
    private static string? StartingFallback(Tyrant.Core.Workspaces.Workspace ws, ModProject mod, Tyrant.Framework.Core.SkinEntry skin)
    {
        if (!ReferenceEquals(Tyrant.Framework.Core.StartingSkins.Of(mod.Manifest, skin.Species), skin)) return null;
        var species = mod.Manifest.Species.First(s => s.Key(mod.Id) == skin.Species);
        string? name = null;
        try { name = SkinMaps.Vanilla(SpeciesSkinsReader.Load(ws), species.Base, species.StartingSkin.ToString(System.Globalization.CultureInfo.InvariantCulture))?.Name; }
        catch (Tyrant.Core.Errors.TyrantException) { } // no data dump: the template skin's name is unknown
        return name is null ? $"{skin.Species} will start with its template's game skin instead." : $"{skin.Species} will start with its template's game skin ({name}) instead.";
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

public sealed class ModMaterialsCommand : Command<ModMaterialsCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandArgument(1, "[SKIN]")]
        [Description("The skin's id (see 'tyrant mod show'); or use --species.")]
        public string? Skin { get; set; }

        [CommandOption("--species <ID>")]
        [Description("Work on a species' material settings instead of a skin's.")]
        public string? Species { get; set; }

        [CommandOption("--show")]
        [Description("Print the material settings as JSON.")]
        public bool Show { get; set; }

        [CommandOption("--set <FILE>")]
        [Description("Set them from a JSON file (the \"materials\" section of mod.json).")]
        public string? Set { get; set; }

        [CommandOption("--clear")]
        [Description("Remove them (the game's settings are used).")]
        public bool Clear { get; set; }

        [CommandOption("--ramp <PNG>")]
        [Description("Use your PNG as the iridescence ramp of a material.")]
        public string? Ramp { get; set; }

        [CommandOption("--mask <PNG>")]
        [Description("Use your PNG as the iridescence mask of a material.")]
        public string? Mask { get; set; }

        [CommandOption("--material <NAME>")]
        [Description("The material for --ramp or --mask (default: the only one in the settings, else the animal's material).")]
        public string? Material { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        if ((settings.Skin is null) == (settings.Species is null))
        {
            Console.Error.WriteLine("Give a skin or --species (not both).");
            return ExitCodes.Error;
        }
        if ((settings.Show ? 1 : 0) + (settings.Set is null ? 0 : 1) + (settings.Clear ? 1 : 0) + (settings.Ramp is null ? 0 : 1) + (settings.Mask is null ? 0 : 1) != 1)
        {
            Console.Error.WriteLine("Use exactly one of --show, --set <file.json>, --clear, --ramp <png> or --mask <png>.");
            return ExitCodes.Error;
        }
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        var who = settings.Skin ?? settings.Species!;
        // The index and the data dump are read once; without them a species is found by its name, in any case, among the mod's own entries.
        var index = ModCli.TryIndex(ws);
        var dump = ModCli.TrySpecies(ws);
        var species = index is not null && dump is not null ? mod.SpeciesFor(ws, install, dump) : null;
        var speciesId = settings.Species is null ? null : ResolveSpeciesId(index, species, mod, settings.Species);
        var blocks = settings.Skin is not null ? mod.Skin(settings.Skin).Materials : mod.SpeciesMaterials(speciesId!);

        if (settings.Show)
        {
            Console.WriteLine(blocks is null ? "No material settings of its own." : Json.Write(MaterialOverrides.ToJson(blocks)));
            return ExitCodes.Ok;
        }
        if (settings.Set is not null || settings.Clear)
        {
            var json = settings.Clear ? null : File.ReadAllText(settings.Set!);
            if (settings.Skin is not null) mod.SetSkinMaterials(settings.Skin, json);
            else if (species is not null) mod.SetSpeciesMaterials(index!, species, speciesId!, json);
            else if (settings.Clear) mod.ClearSpeciesMaterials(speciesId!);
            else
            {
                Console.Error.WriteLine("Setting a species' material settings needs the game's data: run 'tyrant dump run' and 'tyrant assets index' first.");
                return ExitCodes.Error;
            }
            Console.WriteLine(settings.Clear ? $"'{who}' uses the game's material settings." : $"Saved the material settings of '{who}'.");
            return ExitCodes.Ok;
        }

        if (speciesId is not null && blocks is null && species is null)
        {
            Console.Error.WriteLine($"'{who}' has no material settings in this mod, and the game's data is missing to look the species up; set them first with --set.");
            return ExitCodes.Error;
        }
        var material = settings.Material;
        if (material is null && blocks is { Count: 1 }) material = blocks.Keys.First();
        if (material is null)
        {
            if (index is null || species is null)
            {
                Console.Error.WriteLine("Name the material with --material (the animal's own cannot be found without the data dump and the asset index).");
                return ExitCodes.Error;
            }
            var prefab = ModProject.ResolveModelTarget(index, species, speciesId ?? mod.Skin(settings.Skin!).Species, null).Prefab;
            material = AnimalMaterial.Of(install, CliServices.AssetReader, index, prefab)?.Name;
            if (material is null)
            {
                Console.Error.WriteLine("This animal has no animal-shader material; name one with --material.");
                return ExitCodes.Error;
            }
        }
        var file = mod.SetMaterialFile(settings.Skin, speciesId, material, settings.Ramp is not null ? "ramp" : "mask", settings.Ramp ?? settings.Mask);
        Console.WriteLine($"Imported {file}.");
        return ExitCodes.Ok;
    }

    /// <summary>The species' id as the mod stores it: the game's canonical one when the data is there, else the mod's own entry's (any case), else as given.</summary>
    private static string ResolveSpeciesId(Tyrant.Core.Assets.AssetIndex? index, IReadOnlyList<SpeciesSkins>? species, ModProject mod, string given)
    {
        if (index is not null && species is not null) return ModProject.ResolveModelTarget(index, species, given, null).Species;
        return mod.Manifest.Models.FirstOrDefault(m => string.Equals(m.Target, given, StringComparison.OrdinalIgnoreCase))?.Target ?? given;
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

        [CommandOption("--copy-base")]
        [Description("Copy the base skin's texture into the skin's folder, to edit (needs the data dump and the asset index).")]
        public bool CopyBase { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        if ((settings.Base ? 1 : 0) + (settings.CopyBase ? 1 : 0) + (settings.Png is not null ? 1 : 0) != 1)
        {
            Console.Error.WriteLine("Give one of: a PNG, --base or --copy-base.");
            return ExitCodes.Error;
        }
        if (settings.CopyBase)
        {
            var (workspace, install) = CliServices.OpenWorkspace(settings);
            var mod = ModProject.Open(workspace, settings.Id);
            var copied = mod.CopyBaseFile(install, CliServices.LoadIndex(workspace, install), CliServices.AssetReader,
                ModCli.RequireSpeciesFor(workspace, install, mod), settings.Skin, settings.Sex, settings.Slot);
            Console.WriteLine($"  {settings.Sex} {settings.Slot} <- {copied} (a copy of the base skin's texture, to edit)");
            return ExitCodes.Ok;
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

public sealed class ModSkinPortraitCommand : Command<ModSkinPortraitCommand.Settings>
{
    public sealed class Settings : ModSkinSettings
    {
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
        var portraits = ModProject.Open(ws, settings.Id).SetPortraits(settings.Skin,
            new Dictionary<string, PortraitSource?> { [settings.Slot] = settings.Clear ? null : new PortraitSource(Png: settings.Png) });
        Console.WriteLine(portraits.TryGetValue(settings.Slot, out var file) ? $"  {settings.Slot} <- {file}" : $"'{settings.Skin}' {settings.Slot} uses the game's portrait.");
        return ExitCodes.Ok;
    }
}

public sealed class ModSetCompressionCommand : Command<ModSetCompressionCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandArgument(1, "<MODE>")]
        [Description("auto (match the game: DXT1/DXT5), bc7 (best quality, twice DXT1's memory), or default (with --texture: use the mod's).")]
        public string Mode { get; set; } = "";

        [CommandOption("--texture <FILE>")]
        [Description("One texture of the mod (as in mod.json, e.g. skins/blue/male_D.png); without it the mod's own setting changes.")]
        public string? Texture { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        TextureCompression? mode = settings.Mode == "default" ? null : TextureCompressions.Parse(settings.Mode);
        if (settings.Mode != "default" && mode is null)
        {
            Console.Error.WriteLine($"'{settings.Mode}' is not a compression: use auto, bc7 or default.");
            return ExitCodes.Error;
        }
        if (mode is null && settings.Texture is null)
        {
            Console.Error.WriteLine("'default' needs --texture: the mod itself is auto or bc7.");
            return ExitCodes.Error;
        }
        var (ws, _) = CliServices.OpenWorkspace(settings);
        ModProject.Open(ws, settings.Id).SetCompression(settings.Texture, mode);
        Console.WriteLine(settings.Texture is null ? $"'{settings.Id}' textures: {settings.Mode}." : $"{settings.Texture}: {settings.Mode}.");
        return ExitCodes.Ok;
    }
}
