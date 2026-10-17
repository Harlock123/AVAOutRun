namespace AVAOutRun.Engine;

/// <summary>
/// The computer driver, for the demonstration and the tests. It keeps its foot down in a lane clear of the traffic
/// ahead, eases off when a bend coming up is too tight to hold at its speed, keeps to the tarmac, changes up at
/// speed and down after a crash, and at each fork takes the road it has picked.
/// </summary>
public sealed class Pilot
{
    private int _tick;
    private double _lane;
    private readonly Dictionary<int, int> _fork = [];

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _tick = 0;
        _lane = 0;
        _fork.Clear();
    }

    public Controls Decide(GameEngine g)
    {
        _tick++;
        var w = g.World;
        var p = w.Player;
        if (g.Phase is Phase.Radio) return _tick % 30 == 0 ? new Controls(0, 0, true) : default;
        if (g.Phase != Phase.Drive || p.Crash != Crash.None) return default;
        var s = w.Stage;
        int at = (int)((p.Z + Field.PlayerZ) / Field.SegmentLength);
        // The fork: which way (a choice made once a stage).
        if (!_fork.TryGetValue(w.Level, out int side)) _fork[w.Level] = side = (w.Level * 7 + g.Score / 1000 + _tick) % 2 == 0 ? -1 : 1;
        double split = 0;
        for (int k = 0; k < 60 && at + k < s.Segments.Count; k++) split = Math.Max(split, s.Segments[at + k].Split);
        double centre = split > 0 ? side * split : 0;
        // A lane clear of traffic ahead.
        double best = double.MaxValue, lane = _lane;
        foreach (double l in new[] { -0.66, 0, 0.66 })
        {
            double x = centre + l, cost = Math.Abs(x - p.X) * 0.3 + Math.Abs(l - (_lane - centre)) * 0.2;
            foreach (var car in w.Cars)
            {
                double ahead = car.Z - (p.Z + Field.PlayerZ);
                if (ahead < -Field.SegmentLength || ahead > Field.SegmentLength * 45 || car.Speed >= p.Speed + 1) continue;
                if (Math.Abs(car.X - x) < (Player.Width + car.Width) / 2 + 0.04) cost += 10 / (1 + ahead / Field.SegmentLength / 8);
            }
            if (cost < best) { best = cost; lane = x; }
        }
        _lane = lane;
        // How tight the road ahead bends, for this speed.
        double pct = p.Speed / Field.MaxSpeed, bend = 0;
        for (int k = 0; k < 40 && at + k < s.Segments.Count; k++) bend = Math.Max(bend, Math.Abs(s.Segments[at + k].Curve));
        double pull = bend * pct * Sim.Centrifugal;
        // Steer for the lane, allowing for the bend pulling outward.
        var seg = s.At(p.Z + Field.PlayerZ);
        double drift = -pct * seg.Curve * Sim.Centrifugal * 0.6;
        double err = _lane - (p.X + drift * 0.25);
        int dx = Math.Abs(err) > 0.04 ? Math.Sign(err) : 0;
        bool gas = pull < 0.82 || pct < 0.55;
        bool brake = pull > 1.15 && pct > 0.6;
        Doing = brake ? "braking" : !gas ? "easing" : "flat out";
        bool shift = p.High != p.Kmh > 170 && (p.High ? p.Kmh < 110 : true);
        return new Controls(dx, brake ? 1 : 0, gas && !brake, shift && _tick % 4 == 0);
    }
}
