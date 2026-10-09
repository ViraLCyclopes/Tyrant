using System.ComponentModel;
using System.Text.Json;
using Spectre.Console.Cli;
using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Mods;

namespace Tyrant.Cli.Commands;

public sealed class ModPrefabCommand : Command<ModPrefabCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandArgument(1, "<TARGET>")]
        [Description("The species: a game species id, or a custom species of this mod (tyrant.<mod>.<species>).")]
        public string Target { get; set; } = "";

        [CommandOption("--set <FIELD=VALUE>")]
        [Description("Set a field: \"<object>|<component>|<field>=<value>\" (object empty for the root; value as JSON, else text); repeatable.")]
        public string[] Set { get; set; } = [];

        [CommandOption("--reset <FIELD>")]
        [Description("Back to the game's value: \"<object>|<component>|<field>\"; repeatable.")]
        public string[] Reset { get; set; } = [];

        [CommandOption("--clear")]
        [Description("Remove all of this species' prefab settings.")]
        public bool Clear { get; set; }

        [CommandOption("--overridden")]
        [Description("List only the fields this mod sets.")]
        public bool Overridden { get; set; }

        [CommandOption("--find <TEXT>")]
        [Description("List only fields whose object, component or field contains this text.")]
        public string? Find { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        if (settings.Clear && settings.Set.Length == 0 && settings.Reset.Length == 0)
        {
            if (Overrides(mod, settings.Target) is null) Console.WriteLine($"{settings.Target} has no prefab settings in '{settings.Id}'.");
            else
            {
                mod.RemovePrefabTarget(settings.Target);
                Console.WriteLine($"Cleared the prefab settings of {settings.Target} in '{settings.Id}'.");
            }
            return ExitCodes.Ok;
        }
        var species = ModProject.WithSpecies(SpeciesSkinsReader.Load(ws), mod.Manifest);
        var tree = PrefabFields.Read(ws, install, CliServices.LoadIndex(ws, install), species, CliServices.AssetReader, settings.Target);
        foreach (var entry in settings.Set)
        {
            var eq = entry.IndexOf('=');
            if (eq < 0) throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{entry}' needs =<value>.");
            var (obj, component, field) = Address(entry[..eq]);
            var text = entry[(eq + 1)..];
            JsonElement value;
            try { value = JsonDocument.Parse(text).RootElement.Clone(); }
            catch (JsonException) { value = JsonDocument.Parse(JsonSerializer.Serialize(text)).RootElement.Clone(); }
            mod.SetPrefabField(settings.Target, obj, component, field, value, tree);
        }
        foreach (var entry in settings.Reset)
        {
            var (obj, component, field) = Address(entry);
            mod.ResetPrefabField(settings.Target, obj, component, field);
        }
        if (settings.Clear)
        {
            if (Overrides(mod, settings.Target) is not null) mod.RemovePrefabTarget(settings.Target);
        }
        var overrides = Overrides(mod, settings.Target);
        foreach (var f in tree)
        {
            var mine = overrides?.Fields.FirstOrDefault(o => o.Object == f.Object && o.Component == f.Component && o.Field == f.Field);
            if (settings.Overridden && mine is null) continue;
            if (settings.Find is { } find && !$"{f.Object}|{f.Component}|{f.Field}".Contains(find, StringComparison.OrdinalIgnoreCase)) continue;
            var value = mine is null ? "" : " -> " + JsonSerializer.Serialize(JsonDocument.Parse(Tyrant.Framework.Core.Json.Write(mine.Value)).RootElement);
            Console.WriteLine($"{(f.Object.Length == 0 ? "(root)" : f.Object)} | {f.Component} | {f.Field} = {f.Value}{value}{(f.Editable ? "" : f.Kind == "duplicate" ? " (duplicate path — the game can't address it)" : " (read-only)")}");
        }
        return ExitCodes.Ok;
    }

    private static Tyrant.Framework.Core.PrefabOverrides? Overrides(ModProject mod, string target) =>
        mod.PrefabTargets().FirstOrDefault(t => string.Equals(t.Overrides.Target, target, StringComparison.OrdinalIgnoreCase)).Overrides;

    private static (string, string, string) Address(string text)
    {
        var parts = text.Split('|');
        if (parts.Length != 3) throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{text}' is not <object>|<component>|<field> (leave <object> empty for the root).");
        return (parts[0], parts[1], parts[2]);
    }
}
