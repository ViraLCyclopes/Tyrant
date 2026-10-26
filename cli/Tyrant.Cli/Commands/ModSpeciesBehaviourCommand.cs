using System.ComponentModel;
using Spectre.Console.Cli;
using Tyrant.Core.Errors;
using Tyrant.Core.Mods;
using Tyrant.Framework.Core;

namespace Tyrant.Cli.Commands;

public sealed class ModSetSpeciesBehaviourCommand : Command<ModSetSpeciesBehaviourCommand.Settings>
{
    const string NeedsDump = "A game data dump is needed to know the template's behaviour.";

    public sealed class Settings : ModSettings
    {
        [CommandArgument(1, "<SPECIES-ID>")]
        [Description("The species' id in the mod (see 'tyrant mod show').")]
        public string SpeciesId { get; set; } = "";
        [CommandOption("--start-from <PROFILE>")]
        [Description("The combat profile to start from (asset, short or plain name; see --list). Replaces the rules with that profile's rules.")]
        public string? StartFrom { get; set; }
        [CommandOption("--add-rule <RULE>")]
        [Description("Add an aggression rule (asset, short or plain name). Can be given more than once.")]
        public string[] AddRule { get; set; } = [];
        [CommandOption("--remove-rule <RULE>")]
        [Description("Remove an aggression rule. Can be given more than once.")]
        public string[] RemoveRule { get; set; } = [];
        [CommandOption("--reset")]
        [Description("Go back to the template's behaviour (cannot be combined with the other options).")]
        public bool Reset { get; set; }
        [CommandOption("--list")]
        [Description("List the profiles and rules you can use.")]
        public bool List { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var catalog = BehaviourCatalog.Load(ws);
        if (settings.List) return List(catalog);
        var changes = settings.StartFrom is not null || settings.AddRule.Length > 0 || settings.RemoveRule.Length > 0;
        if (settings.Reset && changes) throw new TyrantException(TyrantErrorCode.ModInvalid, "--reset cannot be combined with --start-from, --add-rule or --remove-rule.");
        if (!settings.Reset && !changes)
        {
            Console.Error.WriteLine("Give --start-from, --add-rule, --remove-rule, --reset or --list.");
            return ExitCodes.Error;
        }
        var mod = ModProject.Open(ws, settings.Id);
        var entry = mod.Manifest.Species.FirstOrDefault(s => s.Id == settings.SpeciesId)
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, $"No species '{settings.SpeciesId}' in mod '{mod.Id}'.");
        var basis = BehaviourCatalog.Basis(catalog, SpeciesDatabase.Template(ws, entry.Base)?.Fields);

        SpeciesBehaviour behaviour;
        // Like the Behaviour card: a new start profile (or the template's, on reset) also brings its three choices into the record.
        BehaviourProfileInfo? valuesFrom = null;
        if (settings.Reset)
        {
            if (basis == null) throw new TyrantException(TyrantErrorCode.ModInvalid, NeedsDump);
            behaviour = new SpeciesBehaviour { StartFrom = basis.Profile, Rules = basis.Rules.ToList() };
            valuesFrom = catalog.Profiles.FirstOrDefault(p => p.Asset == basis.Profile);
        }
        else if (settings.StartFrom is not null)
        {
            var asset = SpeciesBehaviours.ResolveProfile(settings.StartFrom)
                ?? throw new TyrantException(TyrantErrorCode.ModInvalid, $"No behaviour profile '{settings.StartFrom}'. See --list.");
            var profile = catalog.Profiles.FirstOrDefault(p => p.Asset == asset)
                ?? throw new TyrantException(TyrantErrorCode.ModInvalid, catalog.Profiles.Count == 0 ? NeedsDump : $"The game data dump has no profile '{asset}'. See --list.");
            behaviour = new SpeciesBehaviour { StartFrom = asset, Rules = profile.Rules.ToList() };
            valuesFrom = profile;
        }
        else
        {
            var current = entry.Behaviour
                ?? (basis == null ? throw new TyrantException(TyrantErrorCode.ModInvalid, NeedsDump) : new SpeciesBehaviour { StartFrom = basis.Profile, Rules = basis.Rules.ToList() });
            behaviour = new SpeciesBehaviour { StartFrom = current.StartFrom, Rules = current.Rules.ToList() };
        }
        foreach (var text in settings.AddRule)
        {
            var rule = ResolveRule(text);
            if (!behaviour.Rules.Contains(rule)) behaviour.Rules.Add(rule);
        }
        foreach (var text in settings.RemoveRule) behaviour.Rules.Remove(ResolveRule(text));

        mod.SetSpeciesBehaviour(entry.Id, behaviour, basis, valuesFrom == null ? null : ProfileValues(valuesFrom));
        var stored = mod.Manifest.Species.First(s => s.Id == entry.Id).Behaviour;
        Console.WriteLine(stored is null ? $"{entry.Key(mod.Id)}: template behaviour"
            : $"{entry.Key(mod.Id)}: behaviour from {SpeciesBehaviours.Profiles.First(p => p.Asset == stored.StartFrom).Name}, {stored.Rules.Count} rules");
        return ExitCodes.Ok;
    }

    /// <summary>The profile's confidence, temperament and cannibalism by record path; a value the dump lacks is left as it is.</summary>
    static Dictionary<string, string> ProfileValues(BehaviourProfileInfo profile) =>
        SpeciesBehaviours.ProfileValuePaths.Zip([profile.Confidence, profile.Temperament, profile.Cannibalism])
            .Where(pair => pair.Second != "").ToDictionary(pair => pair.First, pair => pair.Second);

    static string ResolveRule(string text) => SpeciesBehaviours.ResolveRule(text)
        ?? throw new TyrantException(TyrantErrorCode.ModInvalid, $"No behaviour rule '{text}'. See --list.");

    static int List(BehaviourCatalogData catalog)
    {
        if (catalog.Profiles.Count == 0)
        {
            Console.WriteLine("A game data dump is needed to list the profiles and rules.");
            return ExitCodes.Ok;
        }
        Console.WriteLine("Profiles:");
        foreach (var p in catalog.Profiles)
        {
            var name = SpeciesBehaviours.Profiles.First(x => x.Asset == p.Asset).Name;
            Console.WriteLine($"  {name} ({p.Asset}){(p.UsedBy.Count == 0 ? "" : " used by " + string.Join(", ", p.UsedBy))}");
        }
        Console.WriteLine("Rules:");
        foreach (var r in catalog.Rules) Console.WriteLine($"  {r.Name} ({r.Asset})");
        return ExitCodes.Ok;
    }
}
