using System;
using System.Collections.Generic;

namespace Tyrant.Framework.Core
{
    public struct Vec3
    {
        public float X, Y, Z;
    }

    /// <summary>
    /// What the replacer needs from the sound engine (FMOD in the game, a fake in tests). Handles are opaque: the game's event
    /// instances, and the replacement's sounds and channels.
    /// </summary>
    public interface ISoundEngine
    {
        bool InstanceValid(IntPtr instance);
        bool InstanceStopped(IntPtr instance);
        bool InstanceLooping(IntPtr instance);
        bool InstancePosition(IntPtr instance, out Vec3 position, out Vec3 velocity);
        void MuteInstance(IntPtr instance);

        /// <summary>The bus the instance plays into (the parent of its channel group); zero until the instance has one.</summary>
        IntPtr InstanceBus(IntPtr instance);

        /// <summary>The game's Sounds bus (or Soundtrack for music): where a replacement goes when its own bus cannot be found.</summary>
        IntPtr FallbackBus(bool music);

        /// <summary>Zero when the file cannot be opened.</summary>
        IntPtr LoadSound(string file, bool threeD);

        /// <summary>Starts the sound paused; zero on failure.</summary>
        IntPtr Play(IntPtr sound, bool loop);

        void Route(IntPtr channel, IntPtr bus);
        void SetPosition(IntPtr channel, Vec3 position, Vec3 velocity);
        void SetVolume(IntPtr channel, float volume);
        void SetPitch(IntPtr channel, float pitch);
        void Unpause(IntPtr channel);
        void Stop(IntPtr channel);
        bool Playing(IntPtr channel);
    }

    /// <summary>
    /// Plays replacements for the game's sound instances: mutes the instance, plays the file paused, puts it in the instance's
    /// bus (so the game's volume sliders apply) before it is heard, follows the instance's position, loops with a looping
    /// original and lets a one-shot finish.
    /// </summary>
    public sealed class SoundReplacer
    {
        /// <summary>Frames to wait for the instance's bus before falling back to the Sounds/Soundtrack bus.</summary>
        public const int RouteFrames = 5;

        /// <summary>A safety cap: entries are dropped after this many frames (about five minutes).</summary>
        public const int MaxFrames = 18000;

        private sealed class Entry
        {
            public IntPtr Instance;
            public IntPtr Channel;
            public bool ThreeD;
            public bool Music;
            public bool Loop;
            public bool Routed;
            public bool Started;
            public int Frames;
        }

        private readonly ISoundEngine _engine;
        private readonly Action<string> _log;
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly Dictionary<string, IntPtr> _sounds = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);

        public SoundReplacer(ISoundEngine engine, Action<string> log)
        {
            _engine = engine;
            _log = log;
        }

        public int Active => _entries.Count;

        /// <summary>
        /// Replaces a game instance the table chose a replacement for; false keeps the original (the file could not be played),
        /// and then the instance is left audible.
        /// </summary>
        public bool Start(IntPtr instance, string eventPath, SoundChoice choice, float pitch, bool ui, bool music, Random random)
        {
            var threeD = !ui && !music;
            var file = choice.NextFile(random);
            if (!_sounds.TryGetValue(file, out var sound))
            {
                sound = _engine.LoadSound(file, threeD);
                _sounds[file] = sound;
                if (sound == IntPtr.Zero) _log($"Could not open {file} for {eventPath} ({choice.ModId}); the game's own sound plays instead.");
            }
            if (sound == IntPtr.Zero) return false;
            var loop = _engine.InstanceLooping(instance);
            var channel = _engine.Play(sound, loop);
            if (channel == IntPtr.Zero) return false;
            _engine.SetVolume(channel, (float)choice.Volume);
            _engine.SetPitch(channel, pitch);
            _engine.MuteInstance(instance);
            _entries.Add(new Entry { Instance = instance, Channel = channel, ThreeD = threeD, Music = music, Loop = loop });
            return true;
        }

        /// <summary>Once per frame.</summary>
        public void Tick()
        {
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                e.Frames++;
                if (!e.Routed)
                {
                    var bus = _engine.InstanceBus(e.Instance);
                    if (bus == IntPtr.Zero && e.Frames >= RouteFrames) bus = _engine.FallbackBus(e.Music);
                    if (bus != IntPtr.Zero)
                    {
                        _engine.Route(e.Channel, bus);
                        e.Routed = true;
                    }
                }
                var alive = _engine.InstanceValid(e.Instance) && !_engine.InstanceStopped(e.Instance);
                if (e.ThreeD && alive && _engine.InstancePosition(e.Instance, out var position, out var velocity))
                    _engine.SetPosition(e.Channel, position, velocity);
                if (e.Routed && !e.Started)
                {
                    _engine.Unpause(e.Channel);
                    e.Started = true;
                }
                if (!alive && e.Loop)
                {
                    _engine.Stop(e.Channel);
                    _entries.RemoveAt(i);
                }
                else if (e.Started && !_engine.Playing(e.Channel))
                    _entries.RemoveAt(i);
                else if (e.Frames > MaxFrames)
                {
                    _engine.Stop(e.Channel);
                    _entries.RemoveAt(i);
                }
            }
        }
    }
}
