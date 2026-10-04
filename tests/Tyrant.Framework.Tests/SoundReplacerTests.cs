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
        public readonly Dictionary<IntPtr, Vec3> Positions = [];
        public readonly HashSet<IntPtr> Muted = [];
        public readonly HashSet<string> Unreadable = [];
        public readonly HashSet<IntPtr> Finished = [];
        public readonly Dictionary<IntPtr, IntPtr> RoutedTo = [];
        public readonly HashSet<IntPtr> Unpaused = [];
        public readonly Dictionary<IntPtr, Vec3> ChannelPositions = [];
        public readonly Dictionary<IntPtr, bool> PlayedLooping = [];
        private int _next = 1000;

        public bool InstanceValid(IntPtr instance) => !Invalid.Contains(instance);
        public bool InstanceStopped(IntPtr instance) => Stopped.Contains(instance);
        public bool InstanceLooping(IntPtr instance) => Looping.Contains(instance);
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
        public void Route(IntPtr channel, IntPtr bus) => RoutedTo[channel] = bus;
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
        Assert.True(replacer.Start(I1, "event:/X", Choice("a.ogg"), 1f, ui: false, music: false, new Random(1)));
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
        replacer.Start(I1, "event:/X", Choice("a.ogg"), 1f, ui: false, music, new Random(1));
        var channel = OnlyChannel(engine);

        for (var i = 0; i < 4; i++) replacer.Tick();
        Assert.Empty(engine.Unpaused);
        replacer.Tick();

        Assert.Equal(new IntPtr(bus), engine.RoutedTo[channel]);
        Assert.Contains(channel, engine.Unpaused);
    }

    [Fact]
    public void A_world_sound_follows_the_instance_and_an_interface_sound_does_not()
    {
        var (engine, replacer) = Setup();
        replacer.Start(I1, "event:/X", Choice("a.ogg"), 1f, ui: false, music: false, new Random(1));
        replacer.Start(I2, "event:/User Interface/Click", Choice("b.ogg"), 1f, ui: true, music: false, new Random(1));
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
    public void A_looping_original_loops_the_replacement_until_the_game_stops_it()
    {
        var (engine, replacer) = Setup();
        engine.Looping.Add(I1);
        engine.Buses[I1] = new IntPtr(5);
        replacer.Start(I1, "event:/Breathing", Choice("breath.ogg"), 1f, ui: false, music: false, new Random(1));
        var channel = OnlyChannel(engine);
        replacer.Tick();

        Assert.True(engine.PlayedLooping[channel]);
        engine.Stopped.Add(I1);
        replacer.Tick();

        Assert.Contains(channel, engine.Finished);
        Assert.Equal(0, replacer.Active);
    }

    [Fact]
    public void A_one_shot_plays_to_its_end_after_the_game_releases_its_instance()
    {
        var (engine, replacer) = Setup();
        engine.Buses[I1] = new IntPtr(5);
        replacer.Start(I1, "event:/Roar", Choice("roar.ogg"), 1f, ui: false, music: false, new Random(1));
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
    public void A_file_fmod_cannot_open_keeps_the_original_and_is_logged_once()
    {
        var (engine, replacer) = Setup();
        engine.Unreadable.Add("broken.ogg");
        Log.Clear();

        Assert.False(replacer.Start(I1, "event:/X", Choice("broken.ogg"), 1f, false, false, new Random(1)));
        Assert.False(replacer.Start(I2, "event:/X", Choice("broken.ogg"), 1f, false, false, new Random(1)));

        Assert.Empty(engine.Muted);
        Assert.Single(Log, l => l.Contains("broken.ogg"));
        Assert.Equal(0, replacer.Active);
    }

    [Fact]
    public void Two_instances_started_together_keep_their_own_channel_and_place()
    {
        var (engine, replacer) = Setup();
        replacer.Start(I1, "event:/X", Choice("a.ogg"), 1f, false, false, new Random(1));
        replacer.Start(I2, "event:/X", Choice("a.ogg"), 1f, false, false, new Random(1));
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
        replacer.Start(I1, "event:/X", Choice("a.ogg"), 1.5f, false, false, new Random(1));
        Assert.Contains("volume 0.8", engine.Calls);
        Assert.Contains("pitch 1.5", engine.Calls);
    }
}
