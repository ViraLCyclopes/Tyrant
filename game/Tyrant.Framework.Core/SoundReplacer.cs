using System;
using System.Collections.Generic;

namespace Tyrant.Framework.Core
{
    public struct Vec3
    {
        public float X, Y, Z;
    }

    /// <summary>What FMOD reported for a followed instance: runs started so far, and sounds played in the latest run.</summary>
    public struct FollowedRun
    {
        public int Starts;
        public int Sounds;
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

        /// <summary>Starts counting the instance's runs and sounds (an FMOD event callback); false when it cannot.</summary>
        bool Follow(IntPtr instance);

        FollowedRun Followed(IntPtr instance);
        void Unfollow(IntPtr instance);

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
    /// Plays replacements for the game's sound instances. The instance is muted and followed: each time the game starts it (a
    /// run), a file is made ready, paused, in the instance's bus (so the game's volume sliders apply). A one-off sound is heard
    /// when the game's own event plays a sound in that run, which keeps its chances (silence rolls), or, with the mod's own
    /// chance, when that roll succeeds. A loop plays while its run lasts. It follows a positional instance and lets a one-off
    /// finish. When the instance cannot be followed, a run is heard as soon as it starts.
    /// </summary>
    public sealed class SoundReplacer
    {
        /// <summary>Frames to wait for the instance's bus, once its run started, before falling back to the Sounds/Soundtrack bus.</summary>
        public const int RouteFrames = 5;

        /// <summary>A safety cap for one-shots only: a non-looping channel still "playing" after this many frames is stopped.</summary>
        public const int MaxFrames = 18000;

        /// <summary>Followed runs of one event with no sound at all before the log says so (silence rolls never get this far).</summary>
        public const int SilentRunsReported = 30;

        private sealed class Entry
        {
            public IntPtr Instance;
            public SoundChoice Choice = null!;
            public Random Random = null!;
            public float Pitch;
            public bool ThreeD;
            public bool Music;
            public bool Loop;
            public bool Following;

            /// <summary>Zero between runs.</summary>
            public IntPtr Channel;
            public bool RunActive;
            public int SeenStarts;
            public bool Heard;
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
        private readonly HashSet<string> _notFollowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _silentRuns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _played = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
            if (!entry.Loop) // a loop plays while its run lasts; only one-off sounds have chances
            {
                entry.Following = _engine.Follow(instance);
                if (!entry.Following && _notFollowed.Add(eventPath))
                    _log($"Could not follow {eventPath}; it plays every time the game starts it.");
            }
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
                if (!Advance(e, valid, running))
                {
                    if (e.Following) _engine.Unfollow(e.Instance);
                    _entries.RemoveAt(i);
                }
                e.WasRunning = running;
            }
        }

        /// <summary>One frame of one entry; false drops it.</summary>
        private bool Advance(Entry e, bool valid, bool running)
        {
            var run = e.Following ? _engine.Followed(e.Instance) : default;
            var newRun = e.Following ? run.Starts > e.SeenStarts : running && !e.WasRunning;
            if (newRun)
            {
                e.SeenStarts = run.Starts;
                if (e.Channel != IntPtr.Zero && e.Started && !e.Loop)
                {
                    _engine.Stop(e.Channel); // the game restarted it: a new run, a new file
                    e.Channel = IntPtr.Zero;
                }
                if (e.Channel == IntPtr.Zero && !Begin(e, e.Choice.Event)) return false;
                e.RunActive = true;
                e.Heard = e.Loop || (e.Choice.Chance is double chance ? e.Random.NextDouble() < chance : !e.Following);
            }

            if (e.Channel == IntPtr.Zero) return valid; // between runs
            if (!e.RunActive)
            {
                if (valid) return true; // created, not started yet: stay paused
                _engine.Stop(e.Channel); // released without ever playing
                return false;
            }

            if (!e.Heard && e.Following && e.Choice.Chance == null && run.Starts == e.SeenStarts && run.Sounds > 0)
            {
                e.Heard = true; // the game's own event played a sound in this run
                _played.Add(e.Choice.Event);
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

            if (e.Bus != IntPtr.Zero && e.Heard && !e.Started)
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

            if (!running && (e.Loop || !e.Heard))
            {
                if (!e.Loop && e.Following && e.Choice.Chance == null) CountSilentRun(e.Choice.Event);
                return Ended(e, valid, stop: true); // a loop ends with its run; an unheard run (a silence roll) is never heard
            }
            if (e.Started && !_engine.Playing(e.Channel))
                return Ended(e, valid, stop: false);
            if (!e.Loop && e.Started && ++e.PlayFrames > MaxFrames)
                return Ended(e, valid, stop: true);
            return true;
        }

        /// <summary>
        /// An event whose followed runs never play a sound (one FMOD does not report, e.g. built from nested events) would leave the
        /// replacement silent for good: the log says so once, and the mod's own chance gets it heard.
        /// </summary>
        private void CountSilentRun(string eventPath)
        {
            if (_played.Contains(eventPath)) return;
            _silentRuns.TryGetValue(eventPath, out var silent);
            _silentRuns[eventPath] = ++silent;
            if (silent == SilentRunsReported)
                _log($"{eventPath} has played no sound in {silent} turns, so its replacement was not heard. If it never is, give it its own chance on its page (or 'tyrant mod set-sound … --chance').");
        }

        /// <summary>The run's replacement ended; the entry waits for the next run while the game keeps the instance.</summary>
        private bool Ended(Entry e, bool valid, bool stop)
        {
            if (stop) _engine.Stop(e.Channel);
            e.Channel = IntPtr.Zero;
            e.RunActive = false;
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
            e.Started = false;
            e.Bus = IntPtr.Zero;
            e.OnFallback = false;
            e.Frames = 0;
            e.PlayFrames = 0;
            return true;
        }
    }
}
