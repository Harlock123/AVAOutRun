using AVAOutRun.Engine;

namespace AVAOutRun.Tests;

/// <summary>
/// How the computer driver gets on (set OUTRUN_PROBE to run it, for tuning: "1" for the driver, "lazy" for one who
/// only holds the accelerator down and steers for the middle).
/// </summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_driver_does()
    {
        string? mode = Environment.GetEnvironmentVariable("OUTRUN_PROBE");
        if (mode is null) return;
        var seeds = (Environment.GetEnvironmentVariable("OUTRUN_SEEDS") ?? "1,2,3").Split(',').Select(int.Parse);
        foreach (int seed in seeds)
        {
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "outrun-probe-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
            g.Go();
            g.Pilot.Reset();
            int crashes = 0, t = 0;
            var last = Crash.None;
            var stages = new List<string>();
            int level = 0;
            double stageAt = 0, offroad = 0;
            for (; t < 60 * 700 && g.State == GameState.Playing && g.Phase is Phase.Drive or Phase.Goal or Phase.TimeUp; t++)
            {
                var w = g.World;
                var c = mode == "lazy" ? new Controls(Math.Abs(w.Player.X) > 0.1 ? -Math.Sign(w.Player.X) : 0, 0, true, false) : g.Pilot.Decide(g);
                g.Tick(c);
                if (w.Player.Crash != Crash.None && last == Crash.None) crashes++;
                last = w.Player.Crash;
                if (!Sim.OnRoad(w.Player.X, w.Stage.At(w.Player.Z + Field.PlayerZ).Split)) offroad += Field.Step;
                if (w.Level != level) { stages.Add($"{t / 60.0 - stageAt:0}s"); stageAt = t / 60.0; level = w.Level; }
                if (g.Phase == Phase.Goal && g.PhaseTime < 0.02) stages.Add($"{t / 60.0 - stageAt:0}s GOAL");
            }
            output.WriteLine($"seed {seed}: {g.Phase} at level {g.World.Level + 1} after {t / 60}s, crashes {crashes}, offroad {offroad:0}s, time left {g.World.TimeLeft:0}, score {g.Score}, route {string.Join("", g.World.Route)}, stages {string.Join(" ", stages)}");
        }
    }
}
