using System;
using HarmonyLib;
using UnityEngine;

namespace Tyrant.Framework
{
    /// <summary>
    /// AddrAnimals.InstantiateInternal for every species Tyrant changes (a custom species, or one with prefab settings): the
    /// clone is made inactive, gets its identity and prefab settings, then wakes as the game asked. Other species spawn as usual.
    /// </summary>
    internal static class SpawnHook
    {
        private static bool _started;
        private static bool _warnedMissing;
        private static System.Reflection.MethodInfo? _instantiateToInstance;

        public static void Start(HarmonyLib.Harmony harmony)
        {
            if (_started || (!SpeciesModule.HasSpecies && !PrefabModule.HasOverrides)) return;
            try
            {
                var spawn = AccessTools.Method(AccessTools.TypeByName("PrehistoricKingdom.AddrAnimals"), "InstantiateInternal")
                    ?? throw new MissingMethodException("AddrAnimals.InstantiateInternal");
                var parameters = spawn.GetParameters();
                if (parameters.Length != 3 || parameters[2].ParameterType != typeof(bool))
                    throw new MissingMethodException("signature changed");
                _instantiateToInstance = AccessTools.Method(parameters[1].ParameterType, "InstantiateToInstance", new[] { typeof(Func<GameObject, GameObject>) })
                    ?? throw new MissingMethodException("InstantiateToInstance");
                harmony.Patch(spawn, prefix: new HarmonyMethod(typeof(SpawnHook), nameof(Spawn)));
                _started = true;
            }
            catch (Exception ex)
            {
                _instantiateToInstance = null;
                FrameworkMod.Log.Error($"AddrAnimals.InstantiateInternal changed ({ex.Message}): custom species and prefab settings are off.");
            }
        }

        private static bool Spawn(object __0, object __1, bool __2)
        {
            var custom = SpeciesModule.TryCustom(__0, out var id, out var authored);
            var target = custom ? id : (PrefabModule.HasOverrides ? SpeciesOf(__0) : null);
            var fields = PrefabModule.For(target);
            if (fields != null && fields.Count == 0) fields = null;
            if (!custom && fields == null) return true;
            var instantiate = _instantiateToInstance;
            if (instantiate == null || !instantiate.DeclaringType!.IsInstanceOfType(__1))
                instantiate = AccessTools.Method(__1.GetType(), "InstantiateToInstance", new[] { typeof(Func<GameObject, GameObject>) });
            if (instantiate == null)
            {
                if (!_warnedMissing)
                {
                    _warnedMissing = true;
                    FrameworkMod.Log.Error("InstantiateToInstance is missing: Tyrant spawning is skipped and the game's own spawn runs.");
                }
                return true;
            }
            Func<GameObject, GameObject> create = template =>
            {
                var prefab = template;
                if (authored != null)
                {
                    SpeciesModule.PrepareAuthored(authored, template, __1);
                    prefab = authored;
                }
                GameObject? instance = null;
                var active = prefab.activeSelf;
                try
                {
                    prefab.SetActive(false);
                    instance = UnityEngine.Object.Instantiate(prefab);
                    if (custom) SpeciesModule.Identify(instance, __0, id);
                    if (fields != null) PrefabModule.Apply(instance, target!, fields);
                    instance.SetActive(__2);
                    if (custom) SpeciesModule.Activated(instance, __0, id);
                    return instance;
                }
                catch
                {
                    if (instance != null) UnityEngine.Object.Destroy(instance);
                    throw;
                }
                finally { prefab.SetActive(active); }
            };
            instantiate.Invoke(__1, new object[] { create });
            return false;
        }

        private static string? SpeciesOf(object data) => Traverse.Create(data).Field("speciesID").GetValue() as string;
    }
}
