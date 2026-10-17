using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAOutRun.Engine;
using AVAOutRun.Views;
using Controls = AVAOutRun.Engine.Controls;

namespace AVAOutRun.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set OUTRUN_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaoutrun-ui-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("OUTRUN_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAOutRun-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.2);
        Shot(w, c, "title");
        g.HighScores.Insert("RUN", 4210000, 5);
        g.HighScores.Insert("AVA", 1980000, 3);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 30 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void The_radio_and_the_lights()
    {
        var (w, c, g) = Open();
        g.StartGame();
        g.Run(40);
        g.Tick(new Controls(1, 0));
        g.Run(10);
        Shot(w, c, "radio");
        g.Tick(new Controls(0, 0, true));
        g.Run(130);
        Shot(w, c, "lights");
    }

    private static void Play(string name, Func<GameEngine, bool> moment, int seconds = 600)
    {
        var (w, c, g) = Open();
        g.Go();
        g.Pilot.Reset();
        Assert.True(PlayUntil(g, c, moment, seconds), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_coast() => Play("coast", g => g.World.Player.Z > 300 * Field.SegmentLength && g.World.Player.Kmh > 200 && g.World.Cars.Any(x => x.Z - g.World.Player.Z > 1500 && x.Z - g.World.Player.Z < 12000));

    [AvaloniaFact]
    public void A_bend() => Play("bend", g => Math.Abs(g.World.Stage.At(g.World.Player.Z + 3000).Curve) > 2.2 && g.World.Player.Kmh > 140 && g.World.Level >= 1);

    [AvaloniaFact]
    public void The_fork() => Play("fork", g => g.World.Player.Z + Field.PlayerZ > (g.World.Stage.ForkStart + 40) * Field.SegmentLength && g.World.Level == 0);

    [AvaloniaFact]
    public void Checkpoint() => Play("checkpoint", g => g.World.Level == 1 && g.Message == "CHECKPOINT" && g.MessageTime < 2.6);

    [AvaloniaFact]
    public void The_canyon() => Play("canyon", g => g.World.Level >= 2 && g.World.Player.Z > 200 * Field.SegmentLength && g.World.Player.Kmh > 160);

    [AvaloniaFact]
    public void Deeper() => Play("stage-4", g => g.World.Level == 3 && g.World.Player.Z > 400 * Field.SegmentLength);

    [AvaloniaFact]
    public void A_crash()
    {
        var (w, c, g) = Open();
        g.Go();
        g.World.Player.Speed = Field.MaxSpeed * 0.8;
        for (int i = 0; i < 60 * 30 && g.World.Player.Crash == Crash.None; i++)
        {
            g.World.Player.Speed = Math.Max(g.World.Player.Speed, Field.MaxSpeed * 0.7);
            g.Tick(new Controls(g.World.Player.X < 1.9 ? 1 : 0, 0, true));
            if (i % 3 == 0) c.Advance(0.05);
        }
        g.Run(25);
        c.Advance(0.4);
        Shot(w, c, "crash");
    }

    [AvaloniaFact]
    public void Goal() => Play("goal", g => g.Phase == Phase.Goal && g.PhaseTime > 2, 700);
}
