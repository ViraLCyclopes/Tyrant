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

        public override void OnUpdate()
        {
            SkinsModule.Tick(); // adds skins once the animal database exists, before a park loads
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
            var sources = ModCatalog.Discover(Path.Combine(root, "Mods")); // an unreadable mod is skipped, not fatal
            var plan = ModCatalog.Plan(sources, ReadList(Path.Combine(root, ModList.FileName)), FrameworkInfo.Version);

            foreach (var warning in plan.Warnings) Log.Warning(warning);
            foreach (var skipped in plan.Skipped) Log.Warning($"{skipped.Id}: skipped — {skipped.Reason}");
            Replacements = ReplacementTable.Build(plan.Mods, out var messages);
            foreach (var message in messages) Log.Warning(message);
            foreach (var mod in plan.Mods)
            {
                Log.Msg($"{mod.Manifest.Id} {mod.Manifest.Version}: {mod.Manifest.Replace.Count} texture replacement(s), {mod.Manifest.Skins.Count} skin(s){(mod.Manifest.Assembly == null ? "" : ", code")}");
                if (mod.Manifest.Assembly != null) CodeModLoader.Load(mod, CodeMods); // logs and skips a broken code mod
            }
            SkinsModule.Prepare(plan.Mods);
            if (Replacements.Count > 0 || CodeMods.Count > 0 || SkinsModule.HasSkins) AnimalTexturePatch.Apply(HarmonyInstance);
            if (SkinsModule.HasSkins) SkinPatches.Apply(HarmonyInstance);
            Log.Msg($"Tyrant framework {FrameworkInfo.Version}: {plan.Mods.Count} mod(s) loaded, {plan.Skipped.Count} skipped.");
        }

        /// <summary>mods.json text, or null when it is missing or unreadable (then every mod loads, sorted by id).</summary>
        private static string? ReadList(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Warning($"UserData/Tyrant/{ModList.FileName} could not be read ({ex.Message}); every installed mod is loaded, sorted by id.");
                return null;
            }
        }
    }
}
