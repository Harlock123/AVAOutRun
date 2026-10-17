using Avalonia.Input;
using AVAOutRun.Engine;

namespace AVAOutRun.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avaoutrun-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>The first stage with no traffic.</summary>
    private static World Empty()
    {
        var w = new World();
        Sim.Start(w, 1);
        w.Cars.Clear();
        return w;
    }

    private static void Run(World w, int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c);
    }

    private static readonly Controls Gas = new(0, 0, true);

    // ───────────────────────────── driving ─────────────────────────────

    [Fact]
    public void Low_gear_pulls_hard_but_tops_out_and_high_gear_goes_on_to_top_speed()
    {
        var w = Empty();
        Run(w, 60 * 3, Gas);
        double low = w.Player.Kmh;
        Assert.InRange(low, 120, Sim.LowTopKmh + 2);
        Run(w, 60 * 4, Gas);
        Assert.InRange(w.Player.Kmh, Sim.LowTopKmh - 10, Sim.LowTopKmh + 2);
        Sim.Step(w, new Controls(0, 0, true, true));
        Assert.True(w.Player.High);
        for (int i = 0; i < 60 * 12; i++) { Sim.Step(w, Gas); w.Player.X = 0; }
        Assert.True(w.Player.Kmh > 270);
    }

    [Fact]
    public void From_a_standstill_high_gear_is_sluggish()
    {
        var low = Empty();
        var high = Empty();
        Sim.Step(high, new Controls(0, 0, false, true));
        Run(low, 60, Gas);
        Run(high, 60, Gas);
        Assert.True(low.Player.Kmh > high.Player.Kmh * 3);
    }

    [Fact]
    public void Braking_slows_harder_than_coasting()
    {
        var a = Empty();
        a.Player.Speed = Field.MaxSpeed * 0.8;
        var b = Empty();
        b.Player.Speed = Field.MaxSpeed * 0.8;
        Run(a, 30);
        Run(b, 30, new Controls(0, 1));
        Assert.True(b.Player.Speed < a.Player.Speed - Field.MaxSpeed * 0.2);
    }

    [Fact]
    public void A_bend_pulls_the_car_to_the_outside_unless_it_is_steered()
    {
        var w = Empty();
        var bend = w.Stage.Segments.First(s => Math.Abs(s.Curve) > 2 && s.Split == 0);
        w.Player.Z = bend.Index * Field.SegmentLength - Field.PlayerZ;
        w.Player.Speed = Field.MaxSpeed * 0.9;
        w.Player.High = true;
        Run(w, 12, Gas);
        Assert.True(w.Player.X * bend.Curve < -0.05, "pulled wide");
    }

    [Fact]
    public void Off_the_road_the_car_slows_and_a_tree_at_speed_is_a_crash()
    {
        var w = Empty();
        var seg = w.Stage.Segments.First(s => s.Index > 100 && s.Split == 0 && s.Props.Any(p => Sim.Width(p.Kind) > 0 && p.Offset > 1.3));
        var prop = seg.Props.First(p => Sim.Width(p.Kind) > 0 && p.Offset > 1.3);
        w.Player.Z = (seg.Index - 6) * Field.SegmentLength - Field.PlayerZ;
        w.Player.X = prop.Offset;
        w.Player.Speed = Field.MaxSpeed * 0.7;
        w.Player.High = true;
        double v = w.Player.Speed;
        Run(w, 12, Gas);
        Assert.True(w.Player.Speed < v, "the grass slows it");
        for (int i = 0; i < 60 && w.Player.Crash == Crash.None; i++) Sim.Step(w, Gas);
        Assert.NotEqual(Crash.None, w.Player.Crash);
        Run(w, 60 * 4);
        Assert.Equal(Crash.None, w.Player.Crash);
        Assert.True(Sim.OnRoad(w.Player.X, 0));
        Assert.False(w.Player.High);
    }

    [Fact]
    public void Running_into_the_back_of_a_car_loses_the_speed()
    {
        var w = Empty();
        w.Player.Z = 20 * Field.SegmentLength;
        w.Player.Speed = Field.MaxSpeed * 0.7;
        w.Cars.Add(new Car { Z = w.Player.Z + Field.PlayerZ + 600, X = 0, Speed = Field.MaxSpeed * 0.4 });
        Run(w, 20, Gas);
        Assert.True(w.Player.Speed < Field.MaxSpeed * 0.5);
    }

    [Fact]
    public void The_fork_sends_the_car_down_the_road_it_is_on()
    {
        foreach (int side in new[] { -1, 1 })
        {
            var w = Empty();
            var s = w.Stage;
            w.Player.Z = s.Length - Field.PlayerZ - Field.SegmentLength * 30;
            w.Player.X = side * s.Segments[^1].Split;
            w.Player.Speed = Field.MaxSpeed * 0.6;
            double time = w.TimeLeft;
            Run(w, 60 * 3, Gas);
            Assert.Equal(1, w.Level);
            Assert.Equal(side < 0 ? 0 : 1, w.Branch);
            Assert.InRange(w.Player.X, -0.3, 0.3);
            Assert.True(w.TimeLeft > time + 50, "time extended");
            Assert.Equal(StageBook.Get(1, w.Branch), w.Stage);
        }
    }

    // ───────────────────────────── the game ─────────────────────────────

    [Fact]
    public void Out_of_time_the_car_rolls_to_a_stop_and_the_game_is_over()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Go();
        g.World.TimeLeft = 0.05;
        g.World.Player.Speed = Field.MaxSpeed * 0.5;
        g.Run(10, Gas);
        Assert.Equal(Phase.TimeUp, g.Phase);
        g.Run((int)(60 * GameEngine.TimeUpSeconds) + 5, Gas);
        Assert.Equal(0, g.World.Player.Speed);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void Over_the_line_at_a_goal_the_time_left_is_counted_into_the_score()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Go();
        var w = g.World;
        w.Level = StageBook.Levels - 1;
        w.Stage = StageBook.Get(w.Level, 2);
        w.Player.Z = w.Stage.Length - Field.PlayerZ - Field.SegmentLength * 10;
        w.Player.Speed = Field.MaxSpeed * 0.5;
        w.TimeLeft = 10;
        g.Run(60, Gas);
        Assert.Equal(Phase.Goal, g.Phase);
        int score = g.Score;
        g.Run((int)(60 * GameEngine.GoalSeconds) + 5);
        Assert.True(g.Score >= score + 9 * GameEngine.TimeBonus);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void The_game_starts_on_the_radio_then_the_lights()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.StartGame();
        Assert.Equal(Phase.Radio, g.Phase);
        g.Tick(new Controls(1, 0));
        g.Tick(default);
        Assert.Equal(1, g.Station);
        g.Tick(new Controls(0, 0, true));
        Assert.Equal(Phase.Countdown, g.Phase);
        // No going anywhere on red.
        g.Run(60, Gas);
        Assert.Equal(0, g.World.Player.Speed);
        g.Run((int)(60 * GameEngine.CountdownSeconds), Gas);
        Assert.Equal(Phase.Drive, g.Phase);
        g.Run(30, Gas);
        Assert.True(g.World.Player.Speed > 0);
    }

    [Fact]
    public void Speed_scores_and_the_clock_runs()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Go();
        double t = g.World.TimeLeft;
        g.Run(120, Gas);
        Assert.True(g.Score > 0);
        Assert.Equal(t - 2, g.World.TimeLeft, 1);
    }

    [Fact]
    public void Left_alone_the_title_runs_the_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Run((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Run((int)(60 * GameEngine.AttractSeconds) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Run((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_keys_steer_accelerate_brake_and_change_gear()
    {
        var input = new InputHandler();
        input.OnKeyDown(Key.Left);
        Assert.Equal(-1, input.Player.Dx);
        input.OnKeyUp(Key.Left);
        input.OnKeyDown(Key.Up);
        Assert.True(input.Player.Gas);
        input.OnKeyUp(Key.Up);
        input.OnKeyDown(Key.X);
        Assert.True(input.Player.Gas);
        input.OnKeyDown(Key.Down);
        Assert.True(input.Player.Brake);
        input.OnKeyDown(Key.Space);
        Assert.True(input.Player.Act);
        input.Dispose();
    }
}
