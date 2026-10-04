using System;
using System.Collections.Generic;
using System.Reflection;
using Tyrant.Dumper.Serialization;
using Tyrant.Framework;

namespace Tyrant.Dumper
{
    /// <summary>
    /// Every FMOD event in the banks the game has loaded, read through FMOD's C functions on the game's Studio system
    /// (FMODUnity.RuntimeManager.StudioSystem, found by reflection: the repo has no game assemblies).
    /// </summary>
    internal static class AudioEventDump
    {
        public static List<AudioEvent> Collect()
        {
            var events = new List<AudioEvent>();
            var studio = StudioHandle();
            if (studio == IntPtr.Zero) throw new InvalidOperationException("FMOD's Studio system is not running.");
            Check(FmodNative.FMOD_Studio_System_GetBankCount(studio, out var bankCount), "GetBankCount");
            var banks = new IntPtr[bankCount];
            Check(FmodNative.FMOD_Studio_System_GetBankList(studio, banks, bankCount, out bankCount), "GetBankList");
            for (var b = 0; b < bankCount; b++)
            {
                var bankPath = FmodNative.ReadPath(banks[b], FmodNative.FMOD_Studio_Bank_GetPath) ?? "";
                if (FmodNative.FMOD_Studio_Bank_GetEventCount(banks[b], out var count) != FmodNative.Ok || count <= 0) continue;
                var descriptions = new IntPtr[count];
                if (FmodNative.FMOD_Studio_Bank_GetEventList(banks[b], descriptions, count, out count) != FmodNative.Ok) continue;
                for (var i = 0; i < count; i++)
                {
                    var path = FmodNative.ReadPath(descriptions[i], FmodNative.FMOD_Studio_EventDescription_GetPath);
                    if (string.IsNullOrEmpty(path)) continue;
                    FmodNative.FMOD_Studio_EventDescription_GetID(descriptions[i], out var id);
                    FmodNative.FMOD_Studio_EventDescription_GetLength(descriptions[i], out var length);
                    FmodNative.FMOD_Studio_EventDescription_IsOneshot(descriptions[i], out var oneShot);
                    events.Add(new AudioEvent(path!, id.ToString("B"), length, oneShot, bankPath));
                }
            }
            return events;
        }

        private static void Check(int result, string call)
        {
            if (result != FmodNative.Ok) throw new InvalidOperationException($"FMOD {call} failed ({result}).");
        }

        private static IntPtr StudioHandle()
        {
            Type? manager = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if ((manager = assembly.GetType("FMODUnity.RuntimeManager", false)) != null) break;
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            var system = manager?.GetProperty("StudioSystem", all)?.GetValue(null, null);
            return system?.GetType().GetField("handle", all)?.GetValue(system) is IntPtr handle ? handle : IntPtr.Zero;
        }
    }
}
