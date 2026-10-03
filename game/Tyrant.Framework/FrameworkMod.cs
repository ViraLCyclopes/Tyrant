using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MelonLoader;
using MelonLoader.Utils;
using Tyrant.Framework;
using Tyrant.Framework.Core;

[assembly: MelonInfo(typeof(FrameworkMod), "Tyrant Framework", FrameworkInfo.Version, "Tyrant")]
[assembly: MelonGame("Blue Meridian", "Prehistoric Kingdom")]

namespace Tyrant.Framework
{
    /// <summary>Loads the mods in UserData/Tyrant/Mods and applies them in memory; it never changes a game file.</summary>
    public sealed class FrameworkMod : MelonMod
    {
        internal static MelonLogger.Instance Log = null!;
        internal static ReplacementTable Replacements = ReplacementTable.Empty;
        internal static readonly List<TyrantMod> CodeMods = new List<TyrantMod>();

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            try
            {
                Load();
            }
            catch (Exception ex)
            {
                Log.Error("The Tyrant framework could not start; no mods are applied: " + ex);
            }
        }

        public override void OnLateInitializeMelon()
        {
            foreach (var mod in CodeMods) Safe(mod, m => m.OnGameLoaded(), nameof(TyrantMod.OnGameLoaded));
        }

        internal static void Safe(TyrantMod mod, Action<TyrantMod> call, string what)
        {
            try
            {
                call(mod);
            }
            catch (Exception ex)
            {
                Log.Error($"{mod.ModId}: {what} failed: {ex}");
            }
        }

        private void Load()
        {
            var root = Path.Combine(MelonEnvironment.UserDataDirectory, "Tyrant");
            var modsDir = Path.Combine(root, "Mods");
            var sources = Directory.Exists(modsDir)
                ? Directory.GetDirectories(modsDir).Select(SourceOf).ToList()
                : new List<ModSource>();
            var listPath = Path.Combine(root, ModList.FileName);
            var plan = ModCatalog.Plan(sources, File.Exists(listPath) ? File.ReadAllText(listPath) : null, FrameworkInfo.Version);

            foreach (var warning in plan.Warnings) Log.Warning(warning);
            foreach (var skipped in plan.Skipped) Log.Warning($"{skipped.Id}: skipped — {skipped.Reason}");
            Replacements = ReplacementTable.Build(plan.Mods, out var messages);
            foreach (var message in messages) Log.Warning(message);
            foreach (var mod in plan.Mods)
            {
                Log.Msg($"{mod.Manifest.Id} {mod.Manifest.Version}: {mod.Manifest.Replace.Count} texture replacement(s){(mod.Manifest.Assembly == null ? "" : ", code")}");
                if (mod.Manifest.Assembly != null) CodeModLoader.Load(mod, CodeMods);
            }
            if (Replacements.Count > 0 || CodeMods.Count > 0) AnimalTexturePatch.Apply(HarmonyInstance);
            Log.Msg($"Tyrant framework {FrameworkInfo.Version}: {plan.Mods.Count} mod(s) loaded, {plan.Skipped.Count} skipped.");
        }

        private static ModSource SourceOf(string directory)
        {
            var manifest = Path.Combine(directory, ModManifest.FileName);
            return new ModSource(Path.GetFileName(directory), directory, File.Exists(manifest) ? File.ReadAllText(manifest) : null);
        }
    }
}
