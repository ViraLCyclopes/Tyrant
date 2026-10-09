using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Tyrant.Framework.Core;
using UnityEngine;

namespace Tyrant.Framework
{
    /// <summary>
    /// Re-aims the game's head look for species-wide rig edits: the species' HeadNeckControlRig.Config (its "boom" from the
    /// neck base to the head, measured by the game on the original skeleton) is turned and scaled by the edit's measured
    /// correction (mod.json "headRig", see <see cref="HeadRig"/>), once per species, before the game copies species data into
    /// its native tables (prefix on AnimalsGlobal.InitializeNativeData, after new species are registered there). The config
    /// is per species in the game's Burst code, so a skin's rig edit cannot have one of its own.
    /// </summary>
    internal static class HeadRigModule
    {
        private static readonly Dictionary<string, (HeadRigCorrection Correction, string ModId)> Corrections = new Dictionary<string, (HeadRigCorrection, string)>(StringComparer.Ordinal);
        private static readonly HashSet<object> Applied = new HashSet<object>(ReferenceComparer.Instance);

        public static void Start(IReadOnlyList<LoadedMod> mods, HarmonyLib.Harmony harmony)
        {
            foreach (var set in ModelTable.Build(mods).SpeciesSets)
                if (set.HeadRig != null) Corrections[set.Target] = (set.HeadRig, set.ModId);
            if (Corrections.Count == 0) return;
            var native = AccessTools.Method(AccessTools.TypeByName("PrehistoricKingdom.AnimalsV2.AI.AnimalsGlobal"), "InitializeNativeData");
            if (native == null)
            {
                FrameworkMod.Log.Warning("AnimalsGlobal.InitializeNativeData not found (game updated?); species rig edits keep the game's head look.");
                return;
            }
            // Low priority: after SpeciesModule's prefix there, so new species are in the database.
            harmony.Patch(native, prefix: new HarmonyMethod(typeof(HeadRigModule), nameof(BeforeNativeData)) { priority = Priority.Low });
        }

        private static void BeforeNativeData()
        {
            try
            {
                var persistent = AccessTools.TypeByName("PrehistoricKingdom.PKPersistentData");
                var database = AccessTools.Property(persistent, "Animals")?.GetValue(null, null);
                if (database == null || !(AccessTools.Field(database.GetType(), "AnimalDataAssets")?.GetValue(database) is IList list)) return;
                foreach (var data in list.Cast<object>())
                {
                    if (data == null || Applied.Contains(data)) continue;
                    if (!(AccessTools.Field(data.GetType(), "speciesID")?.GetValue(data) is string id) || !Corrections.TryGetValue(id, out var found)) continue;
                    Applied.Add(data); // the AnimalData outlives a park: correct it once
                    try
                    {
                        Correct(data, found.Correction);
                        FrameworkMod.Log.Msg($"{id}: head look re-aimed for its rig edit ({found.ModId}).");
                    }
                    catch (Exception ex)
                    {
                        FrameworkMod.Log.Warning($"{id}: the head look could not be re-aimed for its rig edit ({ex.GetBaseException().Message}); it keeps the game's.");
                    }
                }
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("Species rig edits could not re-aim the head look: " + ex.GetBaseException().Message);
            }
        }

        private static void Correct(object data, HeadRigCorrection correction)
        {
            var property = AccessTools.Property(data.GetType(), "HeadNeckRigConfigAdult") ?? throw new MissingMemberException("AnimalData.HeadNeckRigConfigAdult");
            var config = property.GetValue(data, null); // a boxed copy of the struct: changed in place, then written back
            var type = config.GetType();
            FieldInfo Backing(string name) => AccessTools.Field(type, "<" + name + ">k__BackingField") ?? throw new MissingFieldException("HeadNeckControlRig.Config." + name);
            var lengthField = Backing("boomArmLengthRaw");
            var boomField = Backing("boomArmOrientationFromRoot");
            var headField = Backing("headOrientationFromBoomArm");
            var length = (float)lengthField.GetValue(config);
            var boom = Rig((Quaternion)boomField.GetValue(config));
            var head = Rig((Quaternion)headField.GetValue(config));
            var pitch = (float)Backing("BoomArmOrientationFromRootPitchOffset").GetValue(config);
            var upsideDown = (bool)Backing("rigUpsideDown").GetValue(config);
            HeadRig.Apply(correction, upsideDown, pitch, ref length, ref boom, ref head);
            lengthField.SetValue(config, length);
            boomField.SetValue(config, new Quaternion(boom.X, boom.Y, boom.Z, boom.W));
            headField.SetValue(config, new Quaternion(head.X, head.Y, head.Z, head.W));
            property.SetValue(data, config, null);
        }

        private static RigQuaternion Rig(Quaternion q) => new RigQuaternion(q.x, q.y, q.z, q.w);
    }
}
