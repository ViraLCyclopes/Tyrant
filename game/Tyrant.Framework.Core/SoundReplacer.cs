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

        /// <summary>True before the game starts the instance and after it ends (or when it is gone).</summary>
        bool InstanceStopped(IntPtr instance);

        bool InstanceLooping(IntPtr instance);

        /// <summary>The event is positional (an animal, a building); false for interface sounds, music and ambiences.</summary>
        bool InstanceIs3D(IntPtr instance);

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

        /// <summary>The group the channel plays in now (FMOD moves it to master when its bus group is released).</summary>
        IntPtr ChannelBus(IntPtr channel);

        void SetPosition(IntPtr channel, Vec3 position, Vec3 velocity);
        void SetVolume(IntPtr channel, float volume);
        void SetPitch(IntPtr channel, float pitch);
        void Unpause(IntPtr channel);
        void Stop(IntPtr channel);
        bool Playing(IntPtr channel);
    }

    /// <summary>
    /// Plays replacements for the game's sound instances. The instance is muted; each time the game starts it, a file plays
    /// paused, goes into the instance's bus (so the game's volume sliders apply) and is then heard; it follows a positional
    /// instance, loops with a looping original and lets a one-shot finish. An instance the game keeps and starts again (water
    /// splashes, menu sounds) plays the replacement again.
    /// </summary>
    public sealed class SoundReplacer
    {
        /// <summary>Frames to wait for the instance's bus, once it started, before falling back to the Sounds/Soundtrack bus.</summary>
        public const int RouteFrames = 5;

        /// <summary>A safety cap for one-shots only: a non-looping channel still "playing" after this many frames is stopped.</summary>
        public const int MaxFrames = 18000;

        private sealed class Entry
        {
            public IntPtr Instance;
            public SoundChoice Choice = null!;
            public Random Random = null!;
            public float Pitch;
            public bool ThreeD;
            public bool Music;
            public bool Loop;

            /// <summary>Zero between plays (the replacement ended; waiting for the game to start the instance again).</summary>
            public IntPtr Channel;
            public bool SeenStart;
            public bool Started;
            public IntPtr Bus;
            public bool OnFallback;
            public int Frames;
            public int PlayFrames;
            public bool WasRunning;
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
        public bool Start(IntPtr instance, string eventPath, SoundChoice choice, float pitch, bool music, Random random)
        {
            var entry = new Entry
            {
                Instance = instance, Choice = choice, Random = random, Pitch = pitch, Music = music,
                ThreeD = _engine.InstanceIs3D(instance), Loop = _engine.InstanceLooping(instance),
            };
            if (!Begin(entry, eventPath)) return false;
            entry.SeenStart = false; // the game starts it now (PlayOneShot) or later (an instance it keeps)
            _engine.MuteInstance(instance);
            _entries.Add(entry);
            return true;
        }

        /// <summary>Once per frame.</summary>
        public void Tick()
        {
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                var valid = _engine.InstanceValid(e.Instance);
                var running = valid && !_engine.InstanceStopped(e.Instance);
                if (!Advance(e, valid, running)) _entries.RemoveAt(i);
                e.WasRunning = running;
            }
        }

        /// <summary>One frame of one entry; false drops it.</summary>
        private bool Advance(Entry e, bool valid, bool running)
        {
            if (e.Channel == IntPtr.Zero)
            {
                if (!valid) return false;
                if (running && !e.WasRunning) return Begin(e, e.Choice.Event); // the game started it again
                return true;
            }

            if (!e.SeenStart)
            {
                if (running) e.SeenStart = true;
                else if (!valid)
                {
                    _engine.Stop(e.Channel); // released without ever playing
                    return false;
                }
                else return true; // created, not started yet: stay paused
            }

            if (!e.Started)
            {
                e.Frames++;
                var bus = _engine.InstanceBus(e.Instance);
                var fallback = false;
                if (bus == IntPtr.Zero && e.Frames >= RouteFrames)
                {
                    bus = _engine.FallbackBus(e.Music);
                    fallback = true;
                }
                if (bus != IntPtr.Zero)
                {
                    _engine.Route(e.Channel, bus);
                    e.Bus = bus;
                    e.OnFallback = fallback;
                }
            }

            if (e.ThreeD && running && _engine.InstancePosition(e.Instance, out var position, out var velocity))
                _engine.SetPosition(e.Channel, position, velocity);

            if (e.Bus != IntPtr.Zero && !e.Started)
            {
                _engine.Unpause(e.Channel);
                e.Started = true;
            }

            // The original ended: its bus group may be released, and FMOD would move the channel to master (outside the sliders).
            if (e.Started && !running && !e.OnFallback && _engine.ChannelBus(e.Channel) != e.Bus)
            {
                var fallbackBus = _engine.FallbackBus(e.Music);
                if (fallbackBus != IntPtr.Zero)
                {
                    _engine.Route(e.Channel, fallbackBus);
                    e.Bus = fallbackBus;
                    e.OnFallback = true;
                }
            }

            if (e.Loop && !running && e.SeenStart)
                return Ended(e, valid, stop: true);
            if (e.Started && !_engine.Playing(e.Channel))
                return Ended(e, valid, stop: false);
            if (!e.Loop && e.Started && ++e.PlayFrames > MaxFrames)
                return Ended(e, valid, stop: true);
            return true;
        }

        /// <summary>The replacement ended; the entry waits for the next start while the game keeps the instance.</summary>
        private bool Ended(Entry e, bool valid, bool stop)
        {
            if (stop) _engine.Stop(e.Channel);
            e.Channel = IntPtr.Zero;
            return valid;
        }

        /// <summary>A new paused channel with one of the choice's files; false when the file cannot be played.</summary>
        private bool Begin(Entry e, string eventPath)
        {
            var file = e.Choice.NextFile(e.Random);
            var key = (e.ThreeD ? "3d|" : "2d|") + file;
            if (!_sounds.TryGetValue(key, out var sound))
            {
                sound = _engine.LoadSound(file, e.ThreeD);
                _sounds[key] = sound;
                if (sound == IntPtr.Zero) _log($"Could not open {file} for {eventPath} ({e.Choice.ModId}); the game's own sound plays instead.");
            }
            if (sound == IntPtr.Zero) return false;
            var channel = _engine.Play(sound, e.Loop);
            if (channel == IntPtr.Zero) return false;
            _engine.SetVolume(channel, (float)e.Choice.Volume);
            _engine.SetPitch(channel, e.Pitch);
            e.Channel = channel;
            e.SeenStart = true;
            e.Started = false;
            e.Bus = IntPtr.Zero;
            e.OnFallback = false;
            e.Frames = 0;
            e.PlayFrames = 0;
            return true;
        }
    }
}
