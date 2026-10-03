using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MelonLoader;
using Tyrant.Framework.Core;
using UnityEngine;

namespace Tyrant.Framework
{
    /// <summary>
    /// Entry point of a code mod: a mod lists its DLL as "assembly" in mod.json, and the framework creates one instance of
    /// every non-abstract TyrantMod subclass in it. Exceptions thrown from these methods are logged, never fatal.
    /// </summary>
    public abstract class TyrantMod
    {
        public string ModId { get; private set; } = "";
        public string ModDirectory { get; private set; } = "";
        protected MelonLogger.Instance Log { get; private set; } = null!;

        internal void Initialize(string id, string directory)
        {
            ModId = id;
            ModDirectory = directory;
            Log = new MelonLogger.Instance("Tyrant:" + id);
        }

        /// <summary>Once, after every mod has loaded.</summary>
        public virtual void OnGameLoaded() { }

        /// <summary>Whenever an animal's skin textures have been (re)applied: when it spawns, grows up or changes skin.</summary>
        public virtual void OnAnimalSpawned(Component animal) { }
    }

    internal static class CodeModLoader
    {
        public static void Load(LoadedMod mod, List<TyrantMod> into)
        {
            var id = mod.Manifest.Id;
            try
            {
                // Inside the try: a path with characters Mono rejects ("bin|Mod.dll") must only skip this mod's code.
                var path = Path.Combine(mod.Directory, mod.Manifest.Assembly!);
                if (!ModPaths.IsInside(path, mod.Directory))
                {
                    FrameworkMod.Log.Warning($"{id}: its assembly \"{mod.Manifest.Assembly}\" points outside the mod folder; its code is not loaded.");
                    return;
                }
                if (!File.Exists(path))
                {
                    FrameworkMod.Log.Warning($"{id}: its assembly \"{mod.Manifest.Assembly}\" is missing — reinstall the mod from Tyrant.");
                    return;
                }
                var types = Assembly.LoadFrom(path).GetTypes().Where(t => typeof(TyrantMod).IsAssignableFrom(t) && !t.IsAbstract).ToList();
                if (types.Count == 0) FrameworkMod.Log.Warning($"{id}: {mod.Manifest.Assembly} has no TyrantMod class; nothing to run.");
                foreach (var type in types)
                {
                    var instance = (TyrantMod)Activator.CreateInstance(type);
                    instance.Initialize(id, mod.Directory);
                    into.Add(instance);
                }
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Error($"{id}: its assembly could not be loaded ({ex.Message}); the mod's code does not run.");
            }
        }
    }
}
