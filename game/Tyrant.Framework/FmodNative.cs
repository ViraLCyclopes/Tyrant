using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Tyrant.Framework
{
    /// <summary>
    /// The FMOD C functions Tyrant needs, called in the game's own fmodstudio.dll (the DLL FMOD's C# wrapper uses; it exports the
    /// core FMOD5_* and the Studio FMOD_Studio_* functions). Signatures follow FMOD 2.3's wrapper, so the repo needs no game
    /// assemblies. Also linked into the dumper.
    /// </summary>
    internal static class FmodNative
    {
        private const string Dll = "fmodstudio";

        public const int Ok = 0;
        public const int PlaybackStopped = 2;

        public const uint ModeLoopOff = 0x00000001;
        public const uint ModeLoopNormal = 0x00000002;
        public const uint Mode2D = 0x00000008;
        public const uint Mode3D = 0x00000010;
        public const uint ModeCreateCompressedSample = 0x00000200;
        public const uint Mode3DLinearRolloff = 0x00200000;

        [StructLayout(LayoutKind.Sequential)]
        public struct Vector
        {
            public float X, Y, Z;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Attributes3D
        {
            public Vector Position, Velocity, Forward, Up;
        }

        // Studio: event instances and descriptions
        [DllImport(Dll)] public static extern bool FMOD_Studio_EventInstance_IsValid(IntPtr instance);
        [DllImport(Dll)] public static extern int FMOD_Studio_EventInstance_GetDescription(IntPtr instance, out IntPtr description);
        [DllImport(Dll)] public static extern int FMOD_Studio_EventInstance_GetPlaybackState(IntPtr instance, out int state);
        [DllImport(Dll)] public static extern int FMOD_Studio_EventInstance_Get3DAttributes(IntPtr instance, out Attributes3D attributes);
        [DllImport(Dll)] public static extern int FMOD_Studio_EventInstance_SetVolume(IntPtr instance, float volume);
        [DllImport(Dll)] public static extern int FMOD_Studio_EventInstance_GetChannelGroup(IntPtr instance, out IntPtr group);
        [DllImport(Dll)] public static extern int FMOD_Studio_EventDescription_GetPath(IntPtr description, IntPtr path, int size, out int retrieved);
        [DllImport(Dll)] public static extern int FMOD_Studio_EventDescription_IsOneshot(IntPtr description, out bool oneshot);
        [DllImport(Dll)] public static extern int FMOD_Studio_EventDescription_Is3D(IntPtr description, out bool is3D);
        [DllImport(Dll)] public static extern int FMOD_Studio_EventDescription_GetID(IntPtr description, out Guid id);
        [DllImport(Dll)] public static extern int FMOD_Studio_EventDescription_GetLength(IntPtr description, out int length);

        // Studio: system, buses, banks
        [DllImport(Dll)] public static extern int FMOD_Studio_System_GetCoreSystem(IntPtr system, out IntPtr core);
        [DllImport(Dll)] public static extern int FMOD_Studio_System_GetBus(IntPtr system, byte[] path, out IntPtr bus);
        [DllImport(Dll)] public static extern int FMOD_Studio_System_GetBankCount(IntPtr system, out int count);
        [DllImport(Dll)] public static extern int FMOD_Studio_System_GetBankList(IntPtr system, IntPtr[] array, int capacity, out int count);
        [DllImport(Dll)] public static extern int FMOD_Studio_Bus_LockChannelGroup(IntPtr bus);
        [DllImport(Dll)] public static extern int FMOD_Studio_Bus_GetChannelGroup(IntPtr bus, out IntPtr group);
        [DllImport(Dll)] public static extern int FMOD_Studio_Bank_GetPath(IntPtr bank, IntPtr path, int size, out int retrieved);
        [DllImport(Dll)] public static extern int FMOD_Studio_Bank_GetEventCount(IntPtr bank, out int count);
        [DllImport(Dll)] public static extern int FMOD_Studio_Bank_GetEventList(IntPtr bank, IntPtr[] array, int capacity, out int count);

        // Core: sounds and channels
        [DllImport(Dll)] public static extern int FMOD5_System_CreateSound(IntPtr system, byte[] name, uint mode, IntPtr exinfo, out IntPtr sound);
        [DllImport(Dll)] public static extern int FMOD5_System_PlaySound(IntPtr system, IntPtr sound, IntPtr group, bool paused, out IntPtr channel);
        [DllImport(Dll)] public static extern int FMOD5_System_GetMasterChannelGroup(IntPtr system, out IntPtr group);
        [DllImport(Dll)] public static extern int FMOD5_ChannelGroup_GetParentGroup(IntPtr group, out IntPtr parent);
        [DllImport(Dll)] public static extern int FMOD5_Channel_SetChannelGroup(IntPtr channel, IntPtr group);
        [DllImport(Dll)] public static extern int FMOD5_Channel_GetChannelGroup(IntPtr channel, out IntPtr group);
        [DllImport(Dll)] public static extern int FMOD5_Channel_Set3DAttributes(IntPtr channel, ref Vector position, ref Vector velocity);
        [DllImport(Dll)] public static extern int FMOD5_Channel_Set3DMinMaxDistance(IntPtr channel, float min, float max);
        [DllImport(Dll)] public static extern int FMOD5_Channel_SetPaused(IntPtr channel, bool paused);
        [DllImport(Dll)] public static extern int FMOD5_Channel_SetVolume(IntPtr channel, float volume);
        [DllImport(Dll)] public static extern int FMOD5_Channel_SetPitch(IntPtr channel, float pitch);
        [DllImport(Dll)] public static extern int FMOD5_Channel_SetMode(IntPtr channel, uint mode);
        [DllImport(Dll)] public static extern int FMOD5_Channel_Stop(IntPtr channel);
        [DllImport(Dll)] public static extern int FMOD5_Channel_IsPlaying(IntPtr channel, out bool playing);

        /// <summary>A UTF-8, zero-terminated string as FMOD takes paths.</summary>
        public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text + "\0");

        public delegate int PathGetter(IntPtr handle, IntPtr buffer, int size, out int retrieved);

        /// <summary>Reads a path an FMOD getter writes into a buffer; null on failure.</summary>
        public static string? ReadPath(IntPtr handle, PathGetter getter)
        {
            var buffer = Marshal.AllocHGlobal(512);
            try
            {
                if (getter(handle, buffer, 512, out var retrieved) != Ok || retrieved <= 0) return null;
                var bytes = new byte[Math.Min(retrieved, 512)];
                Marshal.Copy(buffer, bytes, 0, bytes.Length);
                var end = Array.IndexOf(bytes, (byte)0);
                return Encoding.UTF8.GetString(bytes, 0, end < 0 ? bytes.Length : end);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
