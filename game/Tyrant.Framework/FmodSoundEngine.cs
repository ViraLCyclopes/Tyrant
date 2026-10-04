using System;
using System.Collections.Generic;
using HarmonyLib;
using Tyrant.Framework.Core;

namespace Tyrant.Framework
{
    /// <summary>The replacer's sound engine in the game: FMOD's C functions on the game's running FMOD system.</summary>
    internal sealed class FmodSoundEngine : ISoundEngine
    {
        private IntPtr _studio;
        private IntPtr _core;
        private IntPtr _master;
        private readonly Dictionary<bool, IntPtr> _fallback = new Dictionary<bool, IntPtr>();

        /// <summary>FMODUnity.RuntimeManager.StudioSystem's handle, read by reflection (the repo has no game assemblies).</summary>
        internal static IntPtr StudioHandle()
        {
            var manager = AccessTools.TypeByName("FMODUnity.RuntimeManager");
            var system = manager == null ? null : AccessTools.Property(manager, "StudioSystem")?.GetValue(null, null);
            return system == null ? IntPtr.Zero : (IntPtr)(AccessTools.Field(system.GetType(), "handle")?.GetValue(system) ?? IntPtr.Zero);
        }

        private bool Ready()
        {
            if (_core != IntPtr.Zero) return true;
            _studio = StudioHandle();
            if (_studio == IntPtr.Zero || FmodNative.FMOD_Studio_System_GetCoreSystem(_studio, out _core) != FmodNative.Ok) return false;
            FmodNative.FMOD5_System_GetMasterChannelGroup(_core, out _master);
            return _core != IntPtr.Zero;
        }

        public bool InstanceValid(IntPtr instance) => FmodNative.FMOD_Studio_EventInstance_IsValid(instance);

        public bool InstanceStopped(IntPtr instance) =>
            FmodNative.FMOD_Studio_EventInstance_GetPlaybackState(instance, out var state) != FmodNative.Ok || state == FmodNative.PlaybackStopped;

        public bool InstanceLooping(IntPtr instance) =>
            FmodNative.FMOD_Studio_EventInstance_GetDescription(instance, out var description) == FmodNative.Ok
            && FmodNative.FMOD_Studio_EventDescription_IsOneshot(description, out var oneshot) == FmodNative.Ok && !oneshot;

        /// <summary>From the event itself: interface sounds, music and ambiences are 2D and have no position.</summary>
        public bool InstanceIs3D(IntPtr instance) =>
            FmodNative.FMOD_Studio_EventInstance_GetDescription(instance, out var description) == FmodNative.Ok
            && FmodNative.FMOD_Studio_EventDescription_Is3D(description, out var is3D) == FmodNative.Ok && is3D;

        public bool InstancePosition(IntPtr instance, out Vec3 position, out Vec3 velocity)
        {
            position = velocity = default;
            if (FmodNative.FMOD_Studio_EventInstance_Get3DAttributes(instance, out var a) != FmodNative.Ok) return false;
            position = new Vec3 { X = a.Position.X, Y = a.Position.Y, Z = a.Position.Z };
            velocity = new Vec3 { X = a.Velocity.X, Y = a.Velocity.Y, Z = a.Velocity.Z };
            return true;
        }

        public void MuteInstance(IntPtr instance) => FmodNative.FMOD_Studio_EventInstance_SetVolume(instance, 0f);

        public IntPtr InstanceBus(IntPtr instance) =>
            FmodNative.FMOD_Studio_EventInstance_GetChannelGroup(instance, out var group) == FmodNative.Ok
            && FmodNative.FMOD5_ChannelGroup_GetParentGroup(group, out var parent) == FmodNative.Ok ? parent : IntPtr.Zero;

        public IntPtr FallbackBus(bool music)
        {
            if (_fallback.TryGetValue(music, out var cached) && cached != IntPtr.Zero) return cached;
            if (!Ready()) return IntPtr.Zero;
            var path = music ? "bus:/MasterMix/Soundtrack" : "bus:/MasterMix/SFX";
            var group = IntPtr.Zero;
            if (FmodNative.FMOD_Studio_System_GetBus(_studio, FmodNative.Utf8(path), out var bus) == FmodNative.Ok)
            {
                FmodNative.FMOD_Studio_Bus_LockChannelGroup(bus);
                FmodNative.FMOD_Studio_Bus_GetChannelGroup(bus, out group);
            }
            _fallback[music] = group; // zero until the bus has a group: the replacer waits (never plays outside the mixer)
            return group;
        }

        public IntPtr LoadSound(string file, bool threeD)
        {
            if (!Ready()) return IntPtr.Zero;
            var mode = FmodNative.ModeCreateCompressedSample | (threeD ? FmodNative.Mode3D | FmodNative.Mode3DLinearRolloff : FmodNative.Mode2D);
            return FmodNative.FMOD5_System_CreateSound(_core, FmodNative.Utf8(file), mode, IntPtr.Zero, out var sound) == FmodNative.Ok ? sound : IntPtr.Zero;
        }

        public IntPtr Play(IntPtr sound, bool loop)
        {
            if (!Ready() || FmodNative.FMOD5_System_PlaySound(_core, sound, _master, true, out var channel) != FmodNative.Ok) return IntPtr.Zero;
            FmodNative.FMOD5_Channel_SetMode(channel, loop ? FmodNative.ModeLoopNormal : FmodNative.ModeLoopOff);
            FmodNative.FMOD5_Channel_Set3DMinMaxDistance(channel, 2f, 120f);
            return channel;
        }

        public void Route(IntPtr channel, IntPtr bus) => FmodNative.FMOD5_Channel_SetChannelGroup(channel, bus);

        public IntPtr ChannelBus(IntPtr channel) =>
            FmodNative.FMOD5_Channel_GetChannelGroup(channel, out var group) == FmodNative.Ok ? group : IntPtr.Zero;

        public void SetPosition(IntPtr channel, Vec3 position, Vec3 velocity)
        {
            var p = new FmodNative.Vector { X = position.X, Y = position.Y, Z = position.Z };
            var v = new FmodNative.Vector { X = velocity.X, Y = velocity.Y, Z = velocity.Z };
            FmodNative.FMOD5_Channel_Set3DAttributes(channel, ref p, ref v);
        }

        public void SetVolume(IntPtr channel, float volume) => FmodNative.FMOD5_Channel_SetVolume(channel, volume);

        public void SetPitch(IntPtr channel, float pitch) => FmodNative.FMOD5_Channel_SetPitch(channel, pitch);

        public void Unpause(IntPtr channel) => FmodNative.FMOD5_Channel_SetPaused(channel, false);

        public void Stop(IntPtr channel) => FmodNative.FMOD5_Channel_Stop(channel);

        public bool Playing(IntPtr channel) => FmodNative.FMOD5_Channel_IsPlaying(channel, out var playing) == FmodNative.Ok && playing;
    }
}
