using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class SoundReplacerTests
{
    /// <summary>A sound engine the test drives: instance state is set by the test, every call is recorded.</summary>
    private sealed class FakeSoundEngine : ISoundEngine
    {
        public readonly List<string> Calls = [];
        public readonly Dictionary<IntPtr, IntPtr> Buses = [];
        public readonly HashSet<IntPtr> Stopped = [];
        public readonly HashSet<IntPtr> Invalid = [];
        public readonly HashSet<IntPtr> Looping = [];
        public readonly HashSet<IntPtr> TwoD = [];
        public readonly Dictionary<IntPtr, Vec3> Positions = [];
        public readonly HashSet<IntPtr> Muted = [];
        public readonly HashSet<string> Unreadable = [];
        public readonly HashSet<IntPtr> Finished = [];
        public readonly Dictionary<IntPtr, IntPtr> RoutedTo = [];
        /// <summary>Where a channel really is now (FMOD moves a channel to master when its group is released).</summary>
        public readonly Dictionary<IntPtr, IntPtr> ChannelIn = [];
        public readonly HashSet<IntPtr> Unpaused = [];
        public readonly Dictionary<IntPtr, Vec3> ChannelPositions = [];
        public readonly Dictionary<IntPtr, bool> PlayedLooping = [];
        private int _next = 1000;

        /// <summary>False: Follow fails (the 0.4.0 behaviour: play on start). True: runs and sounds come from Run/Sound.</summary>
        public bool CanFollow;
        public readonly Dictionary<IntPtr, FollowedRun> Runs = [];
        public readonly HashSet<IntPtr> Unfollowed = [];

        public bool Follow(IntPtr instance) => CanFollow;
        public FollowedRun Followed(IntPtr instance) => Runs.TryGetValue(instance, out var run) ? run : default;
        public void Unfollow(IntPtr instance) => Unfollowed.Add(instance);

        /// <summary>The game starts (or restarts) the instance: a new run with no sound yet.</summary>
        public void Run(IntPtr instance)
        {
            Stopped.Remove(instance);
            var run = Followed(instance);
            Runs[instance] = new FollowedRun { Starts = run.Starts + 1, Sounds = 0 };
        }

        /// <summary>The game's event plays a sound in the current run.</summary>
        public void Sound(IntPtr instance)
        {
            var run = Followed(instance);
            Runs[instance] = new FollowedRun { Starts = run.Starts, Sounds = run.Sounds + 1 };
        }

        public bool InstanceValid(IntPtr instance) => !Invalid.Contains(instance);
        public bool InstanceStopped(IntPtr instance) => Stopped.Contains(instance);
        public bool InstanceLooping(IntPtr instance) => Looping.Contains(instance);
        public bool InstanceIs3D(IntPtr instance) => !TwoD.Contains(instance);
        public bool InstancePosition(IntPtr instance, out Vec3 position, out Vec3 velocity)
        {
            velocity = default;
            return Positions.TryGetValue(instance, out position);
        }
        public void MuteInstance(IntPtr instance) => Muted.Add(instance);
        public IntPtr InstanceBus(IntPtr instance) => Buses.TryGetValue(instance, out var bus) ? bus : IntPtr.Zero;
        public IntPtr FallbackBus(bool music) => music ? new IntPtr(2) : new IntPtr(1);
        public IntPtr LoadSound(string file, bool threeD)
        {
            Calls.Add($"load {Path.GetFileName(file)} {(threeD ? "3d" : "2d")}");
            return Unreadable.Contains(Path.GetFileName(file)) ? IntPtr.Zero : new IntPtr(_next++);
        }
        public IntPtr Play(IntPtr sound, bool loop)
        {
            var channel = new IntPtr(_next++);
            PlayedLooping[channel] = loop;
            return channel;
        }
        public void Route(IntPtr channel, IntPtr bus) => RoutedTo[channel] = ChannelIn[channel] = bus;
        public IntPtr ChannelBus(IntPtr channel) => ChannelIn.TryGetValue(channel, out var bus) ? bus : IntPtr.Zero;
        public void SetPosition(IntPtr channel, Vec3 position, Vec3 velocity) => ChannelPositions[channel] = position;
        public void SetVolume(IntPtr channel, float volume) => Calls.Add($"volume {volume:0.##}");
        public void SetPitch(IntPtr channel, float pitch) => Calls.Add($"pitch {pitch:0.##}");
        public void Unpause(IntPtr channel)
        {
            if (!RoutedTo.ContainsKey(channel)) throw new InvalidOperationException("unpaused before routing");
            Unpaused.Add(channel);
        }
        public void Stop(IntPtr channel) => Finished.Add(channel);
        public bool Playing(IntPtr channel) => !Finished.Contains(channel);
    }

    private static readonly IntPtr I1 = new(11), I2 = new(12);
    private static readonly List<string> Log = [];

    private static SoundChoice Choice(params string[] files) => new("carch-voice", "event:/X", files, 0.8, 1.0, unique: true);

    private static SoundChoice Chance(double chance) => new("carch-voice", "event:/X", ["a.ogg"], 1.0, 1.0, unique: true, chance);

    private static (FakeSoundEngine Engine, SoundReplacer Replacer, IntPtr Channel) Following(SoundChoice? choice = null)
    {
        var (engine, replacer) = Setup();
        engine.CanFollow = true;
        engine.Stopped.Add(I1); // created; the game starts it with Run
        engine.Buses[I1] = new IntPtr(5);
        Assert.True(replacer.Start(I1, "event:/Growl", choice ?? Choice("a.ogg"), 1f, music: false, new Random(1)));
        return (engine, replacer, OnlyChannel(engine));
    }

    private static (FakeSoundEngine Engine, SoundReplacer Replacer) Setup()
    {
        var engine = new FakeSoundEngine();
        return (engine, new SoundReplacer(engine, Log.Add));
    }

    private static IntPtr OnlyChannel(FakeSoundEngine engine) => Assert.Single(engine.PlayedLooping).Key;

    [Fact]
    public void It_stays_paused_until_it_is_in_the_instances_bus()
    {
        var (engine, replacer) = Setup();
        Assert.True(replacer.Start(I1, "event:/X", Choice("a.ogg"), 1f, music: false, new Random(1)));
        var channel = OnlyChannel(engine);

        replacer.Tick();
        replacer.Tick();
        Assert.Empty(engine.Unpaused);
        engine.Buses[I1] = new IntPtr(77);
        replacer.Tick();

        Assert.Equal(new IntPtr(77), engine.RoutedTo[channel]);
        Assert.Contains(channel, engine.Unpaused);
        Assert.Contains(I1, engine.Muted);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void Without_a_bus_after_five_frames_it_joins_the_sounds_or_music_bus(bool music, int bus)
    {
        var (engine, replacer) = Setup();
        replacer.Start(I1, "event:/X", Choice("a.ogg"), 1f, music, new Random(1));
        var channel = OnlyChannel(engine);

        for (var i = 0; i < 4; i++) replacer.Tick();
        Assert.Empty(engine.Unpaused);
        replacer.Tick();

        Assert.Equal(new IntPtr(bus), engine.RoutedTo[channel]);
        Assert.Contains(channel, engine.Unpaused);
    }

    [Fact]
    public void A_3D_event_follows_the_instance_and_a_2D_event_plays_without_a_position()
    {
        var (engine, replacer) = Setup();
        engine.TwoD.Add(I2); // e.g. the level ambience: no 3D attributes, (0,0,0) if asked
        replacer.Start(I1, "event:/X", Choice("a.ogg"), 1f, music: false, new Random(1));
        replacer.Start(I2, "event:/Ambience/Level", Choice("b.ogg"), 1f, music: false, new Random(1));
        engine.Buses[I1] = engine.Buses[I2] = new IntPtr(5);

        engine.Positions[I1] = new Vec3 { X = 1, Y = 2, Z = 3 };
        engine.Positions[I2] = new Vec3 { X = 9, Y = 9, Z = 9 };
        replacer.Tick();
        engine.Positions[I1] = new Vec3 { X = 4, Y = 5, Z = 6 };
        replacer.Tick();

        var channels = engine.PlayedLooping.Keys.ToList();
        Assert.Equal(4f, engine.ChannelPositions[channels[0]].X);
        Assert.False(engine.ChannelPositions.ContainsKey(channels[1]));
        Assert.Contains("load a.ogg 3d", engine.Calls);
        Assert.Contains("load b.ogg 2d", engine.Calls);
    }

    [Fact]
    public void One_file_used_by_a_3D_and_a_2D_event_is_loaded_for_each()
    {
        var (engine, replacer) = Setup();
        engine.TwoD.Add(I2);

        replacer.Start(I1, "event:/X", Choice("a.ogg"), 1f, music: false, new Random(1));
        replacer.Start(I2, "event:/Y", Choice("a.ogg"), 1f, music: false, new Random(1));

        Assert.Contains("load a.ogg 3d", engine.Calls);
        Assert.Contains("load a.ogg 2d", engine.Calls);
    }

    [Fact]
    public void A_looping_original_loops_the_replacement_until_the_game_stops_it()
    {
        var (engine, replacer) = Setup();
        engine.Looping.Add(I1);
        engine.Buses[I1] = new IntPtr(5);
        replacer.Start(I1, "event:/Breathing", Choice("breath.ogg"), 1f, music: false, new Random(1));
        var channel = OnlyChannel(engine);
        replacer.Tick();

        Assert.True(engine.PlayedLooping[channel]);
        engine.Stopped.Add(I1);
        replacer.Tick();
        Assert.Contains(channel, engine.Finished);

        engine.Invalid.Add(I1); // released
        replacer.Tick();
        Assert.Equal(0, replacer.Active);
    }

    [Fact]
    public void A_looping_replacement_outlives_the_frame_cap_while_its_instance_plays()
    {
        var (engine, replacer) = Setup();
        engine.Looping.Add(I1);
        engine.Buses[I1] = new IntPtr(5);
        replacer.Start(I1, "event:/Music/Theme", Choice("theme.ogg"), 1f, music: true, new Random(1));
        var channel = OnlyChannel(engine);

        for (var i = 0; i < SoundReplacer.MaxFrames + 10; i++) replacer.Tick();

        Assert.DoesNotContain(channel, engine.Finished);
        Assert.Equal(1, replacer.Active);
    }

    [Fact]
    public void It_waits_for_the_game_to_start_an_instance_it_created_earlier()
    {
        var (engine, replacer) = Setup();
        engine.Looping.Add(I1);
        engine.Stopped.Add(I1); // created at level load, started on the first dive
        engine.Buses[I1] = new IntPtr(5);
        replacer.Start(I1, "event:/Ambience/Underwater", Choice("under.ogg"), 1f, music: false, new Random(1));
        var channel = OnlyChannel(engine);

        for (var i = 0; i < 20; i++) replacer.Tick();
        Assert.Empty(engine.Unpaused);
        Assert.Equal(1, replacer.Active);

        engine.Stopped.Remove(I1);
        replacer.Tick();
        Assert.Contains(channel, engine.Unpaused);
    }

    [Fact]
    public void An_instance_the_game_starts_again_plays_the_replacement_again()
    {
        var (engine, replacer) = Setup();
        engine.Buses[I1] = new IntPtr(5);
        replacer.Start(I1, "event:/Water/Enter", Choice("splash.ogg"), 1f, music: false, new Random(1));
        var first = OnlyChannel(engine);
        replacer.Tick();
        Assert.Contains(first, engine.Unpaused);

        engine.Stopped.Add(I1);     // the game's splash ended
        engine.Finished.Add(first); // and so did ours
        replacer.Tick();
        Assert.Equal(1, replacer.Active); // the instance is kept for the next dive

        engine.Stopped.Remove(I1);  // the next dive
        replacer.Tick();
        replacer.Tick();

        var second = Assert.Single(engine.PlayedLooping.Keys, c => c != first);
        Assert.Contains(second, engine.Unpaused);
        Assert.Equal(new IntPtr(5), engine.RoutedTo[second]);
    }

    [Fact]
    public void A_one_shot_plays_to_its_end_after_the_game_releases_its_instance()
    {
        var (engine, replacer) = Setup();
        engine.Buses[I1] = new IntPtr(5);
        replacer.Start(I1, "event:/Roar", Choice("roar.ogg"), 1f, music: false, new Random(1));
        var channel = OnlyChannel(engine);
        replacer.Tick();

        engine.Invalid.Add(I1);
        replacer.Tick();
        Assert.DoesNotContain(channel, engine.Finished);
        Assert.Equal(1, replacer.Active);

        engine.Finished.Add(channel); // the file reached its end
        replacer.Tick();
        Assert.Equal(0, replacer.Active);
    }

    [Fact]
    public void A_one_shot_whose_bus_goes_away_moves_to_the_sounds_bus_not_master()
    {
        var (engine, replacer) = Setup();
        engine.Buses[I1] = new IntPtr(5);
        replacer.Start(I1, "event:/Roar", Choice("long-roar.ogg"), 1f, music: false, new Random(1));
        var channel = OnlyChannel(engine);
        replacer.Tick();

        engine.Invalid.Add(I1);
        engine.ChannelIn[channel] = new IntPtr(99); // FMOD released the bus group: the channel fell back to master
        replacer.Tick();

        Assert.Equal(new IntPtr(1), engine.ChannelIn[channel]);
    }

    [Fact]
    public void A_file_fmod_cannot_open_keeps_the_original_and_is_logged_once()
    {
        var (engine, replacer) = Setup();
        engine.Unreadable.Add("broken.ogg");
        Log.Clear();

        Assert.False(replacer.Start(I1, "event:/X", Choice("broken.ogg"), 1f, false, new Random(1)));
        Assert.False(replacer.Start(I2, "event:/X", Choice("broken.ogg"), 1f, false, new Random(1)));

        Assert.Empty(engine.Muted);
        Assert.Single(Log, l => l.Contains("broken.ogg"));
        Assert.Equal(0, replacer.Active);
    }

    [Fact]
    public void Two_instances_started_together_keep_their_own_channel_and_place()
    {
        var (engine, replacer) = Setup();
        replacer.Start(I1, "event:/X", Choice("a.ogg"), 1f, false, new Random(1));
        replacer.Start(I2, "event:/X", Choice("a.ogg"), 1f, false, new Random(1));
        engine.Buses[I1] = engine.Buses[I2] = new IntPtr(5);
        engine.Positions[I1] = new Vec3 { X = 10 };
        engine.Positions[I2] = new Vec3 { X = -10 };

        replacer.Tick();

        var channels = engine.PlayedLooping.Keys.ToList();
        Assert.Equal(2, channels.Distinct().Count());
        Assert.Equal((10f, -10f), (engine.ChannelPositions[channels[0]].X, engine.ChannelPositions[channels[1]].X));
        Assert.Equal(1, engine.Calls.Count(c => c == "load a.ogg 3d")); // the file is loaded once and shared
    }

    [Fact]
    public void Volume_and_pitch_are_applied()
    {
        var (engine, replacer) = Setup();
        replacer.Start(I1, "event:/X", Choice("a.ogg"), 1.5f, false, new Random(1));
        Assert.Contains("volume 0.8", engine.Calls);
        Assert.Contains("pitch 1.5", engine.Calls);
    }

    [Fact]
    public void A_one_shot_waits_for_the_games_first_sound()
    {
        var (engine, replacer, channel) = Following();

        engine.Run(I1);
        replacer.Tick();
        Assert.Empty(engine.Unpaused);

        engine.Sound(I1);
        replacer.Tick();
        Assert.Contains(channel, engine.Unpaused);
    }

    [Fact]
    public void A_run_where_the_game_plays_nothing_is_never_heard()
    {
        var (engine, replacer, first) = Following();
        engine.Run(I1);
        replacer.Tick();

        engine.Stopped.Add(I1); // the silence roll: the run ends without a sound
        replacer.Tick();
        Assert.Contains(first, engine.Finished);
        Assert.DoesNotContain(first, engine.Unpaused);
        Assert.Equal(1, replacer.Active); // waiting for the next run

        engine.Run(I1);
        engine.Sound(I1);
        replacer.Tick();
        var second = Assert.Single(engine.PlayedLooping.Keys, c => c != first);
        Assert.Contains(second, engine.Unpaused);
    }

    [Fact]
    public void A_sound_reported_before_the_tick_still_counts()
    {
        var (engine, replacer, channel) = Following();

        engine.Run(I1);
        engine.Sound(I1); // both callbacks came in the Studio update before this frame's tick
        replacer.Tick();

        Assert.Contains(channel, engine.Unpaused);
    }

    [Fact]
    public void Two_sounds_in_one_run_play_one_file()
    {
        var (engine, replacer, _) = Following();

        engine.Run(I1);
        engine.Sound(I1);
        replacer.Tick();
        engine.Sound(I1);
        replacer.Tick();

        Assert.Single(engine.PlayedLooping);
    }

    [Fact]
    public void When_following_fails_it_plays_on_start_and_says_so_once()
    {
        var (engine, replacer) = Setup();
        engine.Buses[I1] = engine.Buses[I2] = new IntPtr(5);
        Log.Clear();

        replacer.Start(I1, "event:/Growl", Choice("a.ogg"), 1f, music: false, new Random(1));
        replacer.Start(I2, "event:/Growl", Choice("a.ogg"), 1f, music: false, new Random(1));
        replacer.Tick();

        Assert.Equal(2, engine.Unpaused.Count);
        Assert.Single(Log, l => l.Contains("Could not follow event:/Growl"));
    }

    [Fact]
    public void Its_own_chance_of_one_plays_at_the_start_without_waiting_for_a_sound()
    {
        var (engine, replacer, channel) = Following(Chance(1.0));

        engine.Run(I1);
        replacer.Tick();

        Assert.Contains(channel, engine.Unpaused);
    }

    [Fact]
    public void Its_own_chance_of_zero_never_plays_even_when_the_game_does()
    {
        var (engine, replacer, channel) = Following(Chance(0.0));

        engine.Run(I1);
        engine.Sound(I1);
        replacer.Tick();
        replacer.Tick();

        Assert.DoesNotContain(channel, engine.Unpaused);
    }

    [Fact]
    public void A_loop_ignores_chance_and_does_not_wait_for_a_sound()
    {
        var (engine, replacer) = Setup();
        engine.CanFollow = true;
        engine.Looping.Add(I1);
        engine.Buses[I1] = new IntPtr(5);
        replacer.Start(I1, "event:/Breathing", Chance(0.0), 1f, music: false, new Random(1));

        replacer.Tick();

        Assert.Contains(OnlyChannel(engine), engine.Unpaused);
    }

    [Fact]
    public void A_dropped_entry_stops_following()
    {
        var (engine, replacer, channel) = Following();
        engine.Run(I1);
        engine.Sound(I1);
        replacer.Tick();

        engine.Invalid.Add(I1);
        engine.Finished.Add(channel);
        replacer.Tick();

        Assert.Equal(0, replacer.Active);
        Assert.Contains(I1, engine.Unfollowed);
    }

    [Fact]
    public void A_followed_sound_that_never_plays_anything_is_reported_once()
    {
        var (engine, replacer, _) = Following();
        Log.Clear();

        for (var i = 0; i < SoundReplacer.SilentRunsReported + 5; i++)
        {
            engine.Run(I1);
            replacer.Tick();
            engine.Stopped.Add(I1); // every run ends without the game's event playing a sound
            replacer.Tick();
        }

        Assert.Single(Log, l => l.Contains("event:/X") && l.Contains("no sound"));
    }

    [Fact]
    public void A_sound_that_plays_now_and_then_is_not_reported()
    {
        var (engine, replacer, _) = Following();
        Log.Clear();

        for (var i = 0; i < SoundReplacer.SilentRunsReported * 2; i++)
        {
            engine.Run(I1);
            if (i % 3 == 0) engine.Sound(I1); // a third of the runs play (like the game's chances)
            replacer.Tick();
            engine.Stopped.Add(I1);
            replacer.Tick();
        }

        Assert.DoesNotContain(Log, l => l.Contains("no sound"));
    }

    [Fact]
    public void A_restart_while_playing_is_a_new_run()
    {
        var (engine, replacer, first) = Following();
        engine.Run(I1);
        engine.Sound(I1);
        replacer.Tick();
        Assert.Contains(first, engine.Unpaused);

        engine.Run(I1); // start() on the playing instance (FMOD's RESTARTED)
        replacer.Tick();
        Assert.Contains(first, engine.Finished);
        var second = Assert.Single(engine.PlayedLooping.Keys, c => c != first);
        Assert.DoesNotContain(second, engine.Unpaused);

        engine.Sound(I1);
        replacer.Tick();
        Assert.Contains(second, engine.Unpaused);
    }
}
