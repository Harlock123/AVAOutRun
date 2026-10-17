namespace AVAOutRun.Engine;

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
    public double Between(double a, double b) => a + Next() * (b - a);
}

/// <summary>A car or lorry going the same way, slower.</summary>
public sealed class Car
{
    public double Z, X, Speed;
    /// <summary>0 a small car, 1 a van, 2 a lorry.</summary>
    public int Kind;
    public uint Colour;
    public double Width => Kind switch { 0 => 0.45, 1 => 0.52, _ => 0.6 };
}

public enum Crash { None, Spin, Roll }

/// <summary>The red convertible: where it is on the road, how fast, in which gear, and whether it's crashing.</summary>
public sealed class Player
{
    public const double Width = 0.56;
    public double Z, X, Speed, CrashTime, Steer, Skid;
    public bool High, GearPrev;
    public Crash Crash;
    public double Kmh => Speed / Field.MaxSpeed * Field.TopKmh;
}

public sealed class World
{
    public int Level, Branch;
    public StageDef Stage = StageBook.Get(0, 0);
    public Player Player = new();
    public List<Car> Cars = [];
    /// <summary>The branches taken so far, one a level.</summary>
    public List<int> Route = [0];
    public double TimeLeft, Clock, SkyOffset;
    public int Score;
    public bool Finished;
    public Dice Dice;
    public List<Sfx> Heard = [];
    /// <summary>Set by a step: a checkpoint passed (the time it added).</summary>
    public double Extended;
}

/// <summary>The drive, step by step: the car's speed and steering, the bends pulling it wide, the traffic, the scenery, the forks.</summary>
public static class Sim
{
    private const double Dt = Field.Step;
    public const double Centrifugal = 0.32, LowTopKmh = 190;
    public const int Traffic = 16;

    public static void Start(World w, int seed)
    {
        w.Level = w.Branch = 0;
        w.Stage = StageBook.Get(0, 0);
        w.Route = [0];
        w.Player = new Player();
        w.TimeLeft = StageBook.Time[0];
        w.Clock = w.SkyOffset = 0;
        w.Score = 0;
        w.Finished = false;
        w.Dice = new Dice(seed);
        Populate(w);
    }

    /// <summary>The traffic on a new stage, spread along it ahead.</summary>
    private static void Populate(World w)
    {
        w.Cars.Clear();
        for (int i = 0; i < Traffic; i++)
            w.Cars.Add(NewCar(w, w.Player.Z + 2500 + i * (w.Stage.Length - 30000) / Traffic));
    }

    private static Car NewCar(World w, double z)
    {
        int kind = w.Dice.Next(10) < 6 ? 0 : w.Dice.Next(10) < 6 ? 1 : 2;
        uint[] paint = [0xFFE0E0E0, 0xFF3A6AD0, 0xFFE0C030, 0xFF40A060, 0xFF8A4AC0, 0xFF303038, 0xFFE07030];
        return new Car
        {
            Z = z, X = (w.Dice.Next(3) - 1) * 0.66, Kind = kind, Colour = paint[w.Dice.Next(paint.Length)],
            Speed = Field.MaxSpeed * (kind == 2 ? w.Dice.Between(0.32, 0.42) : w.Dice.Between(0.38, 0.55)) * (1 + 0.04 * w.Level),
        };
    }

    /// <summary>Whether a spot across the road is on tarmac (either road, in a fork).</summary>
    public static bool OnRoad(double x, double split) => split <= 0 ? Math.Abs(x) <= 1.05 : Math.Min(Math.Abs(x - split), Math.Abs(x + split)) <= 1.05;

    /// <summary>How wide (across) each kind of scenery is to hit, in half road widths.</summary>
    public static double Width(Scenery k) => k switch
    {
        Scenery.Palm or Scenery.Lamp or Scenery.Sign or Scenery.Column => 0.16,
        Scenery.Cypress or Scenery.Pine or Scenery.Cactus => 0.24,
        Scenery.Bush or Scenery.Wheat or Scenery.Barrel or Scenery.Snowman or Scenery.Umbrella => 0.3,
        Scenery.Rock or Scenery.Windmill => 0.45,
        Scenery.House or Scenery.Billboard => 0.8,
        _ => 0,
    };

    public static void Step(World w, Controls c, bool drive = true)
    {
        w.Clock += Dt;
        w.Extended = 0;
        var p = w.Player;
        var s = w.Stage;
        double pct = p.Speed / Field.MaxSpeed;
        var seg = s.At(p.Z + Field.PlayerZ);
        double dx = Dt * 2.3 * pct;
        p.Skid = Math.Max(0, p.Skid - Dt);
        if (p.Crash != Crash.None)
        {
            // Spinning or rolling to a stop, then back on the road.
            p.CrashTime -= Dt;
            p.Speed = Math.Max(0, p.Speed - Field.MaxSpeed * 0.9 * Dt);
            if (p.CrashTime <= 0)
            {
                p.Crash = Crash.None;
                p.Speed = 0;
                p.High = false;
                double lane = seg.Split > 0 ? Math.Sign(p.X) * seg.Split : 0;
                if (!OnRoad(p.X, seg.Split)) p.X = lane + Math.Clamp(p.X - lane, -0.7, 0.7);
            }
        }
        else if (drive)
        {
            p.Steer += (c.Dx - p.Steer) * Math.Min(1, Dt * 8);
            p.X += dx * c.Dx;
            p.X -= dx * pct * seg.Curve * Centrifugal;
            if (Math.Abs(seg.Curve) > 3 && pct > 0.75) p.Skid = 0.2;
            if (c.Act && !p.GearPrev) { p.High = !p.High; w.Heard.Add(Sfx.Gear); }
            p.GearPrev = c.Act;
            double low = LowTopKmh / Field.TopKmh * Field.MaxSpeed;
            if (c.Brake) p.Speed -= Field.MaxSpeed * 1.2 * Dt;
            else if (c.Gas)
            {
                if (!p.High) p.Speed += p.Speed < low ? Field.MaxSpeed / 4.2 * Dt : -Field.MaxSpeed / 8 * Dt;
                else p.Speed += (p.Speed < 0.35 * Field.MaxSpeed ? Field.MaxSpeed / 26 : Field.MaxSpeed / 6.5) * Dt;
            }
            else p.Speed -= Field.MaxSpeed / 6 * Dt;
            if (!OnRoad(p.X, seg.Split) && p.Speed > Field.MaxSpeed / 3.2) p.Speed -= Field.MaxSpeed * 0.8 * Dt;
        }
        else p.Speed = Math.Max(0, p.Speed - Field.MaxSpeed / 4 * Dt);
        p.Speed = Math.Clamp(p.Speed, 0, Field.MaxSpeed);
        p.X = Math.Clamp(p.X, -4.5, 4.5);
        // Scenery off the road.
        if (p.Crash == Crash.None && !OnRoad(p.X, seg.Split))
            for (int k = 0; k <= 1; k++)
                foreach (var prop in s.At(p.Z + Field.PlayerZ + k * Field.SegmentLength).Props)
                {
                    if (prop.Offset == 0 || Math.Abs(p.X - prop.Offset) > (Player.Width + Width(prop.Kind)) / 2) continue;
                    if (p.Kmh > 50)
                    {
                        p.Crash = p.Kmh > 160 ? Crash.Roll : Crash.Spin;
                        p.CrashTime = p.Crash == Crash.Roll ? 2.8 : 1.8;
                        w.Heard.Add(Sfx.Crash);
                    }
                    else p.Speed = 0;
                    break;
                }
        // Traffic.
        foreach (var car in w.Cars)
        {
            car.Z += car.Speed * Dt;
            double ahead = car.Z - (p.Z + Field.PlayerZ);
            if (ahead < -Field.SegmentLength * 30 || car.Z > s.Length - Field.SegmentLength * (StageDef.ForkLength + 10))
            {
                // Gone behind, or into the fork: again further on (if there's room before the fork).
                double z = p.Z + Field.SegmentLength * (Field.DrawDistance + 20 + w.Dice.Next(120));
                var fresh = NewCar(w, z);
                (car.Z, car.X, car.Kind, car.Speed, car.Colour) = (fresh.Z, fresh.X, fresh.Kind, fresh.Speed, fresh.Colour);
                if (car.Z > s.Length - Field.SegmentLength * (StageDef.ForkLength + 10)) car.Z = -1e9;
                continue;
            }
            if (p.Crash != Crash.None || ahead < -Field.SegmentLength * 0.5 || ahead > Field.SegmentLength * 0.6 || p.Speed <= car.Speed) continue;
            if (Math.Abs(p.X - car.X) > (Player.Width + car.Width) / 2 * 0.9) continue;
            // Into the back of it.
            if (p.Kmh - car.Speed / Field.MaxSpeed * Field.TopKmh > 120)
            {
                p.Crash = Crash.Spin;
                p.CrashTime = 1.6;
                w.Heard.Add(Sfx.Crash);
            }
            else w.Heard.Add(Sfx.Bump);
            p.Speed = car.Speed * 0.5;
            p.Z = car.Z - Field.PlayerZ - Field.SegmentLength * 0.7;
        }
        p.Z += p.Speed * Dt;
        w.SkyOffset += seg.Curve * pct * Dt * 0.02;
        w.Score += (int)(p.Kmh * Dt * 12);
        if (drive && !w.Finished) w.TimeLeft -= Dt;
        // The end of the stage: through the checkpoint, down whichever road of the fork the car is on.
        if (p.Z + Field.PlayerZ >= s.Length - Field.SegmentLength * 2) NextStage(w);
    }

    private static void NextStage(World w)
    {
        var p = w.Player;
        var last = w.Stage.Segments[^1];
        if (w.Level == StageBook.Levels - 1)
        {
            w.Finished = true;
            p.Z = w.Stage.Length - Field.PlayerZ - Field.SegmentLength * 2;
            return;
        }
        int right = p.X >= 0 ? 1 : 0;
        p.X -= (right == 1 ? 1 : -1) * last.Split;
        p.Z -= w.Stage.Length - Field.SegmentLength * 2;
        w.Level++;
        w.Branch += right;
        w.Route.Add(w.Branch);
        w.Stage = StageBook.Get(w.Level, w.Branch);
        w.Extended = StageBook.Time[w.Level];
        w.TimeLeft += w.Extended;
        w.Heard.Add(Sfx.Checkpoint);
        w.Heard.Add(Sfx.SayExtended);
        Populate(w);
    }

    /// <summary>The segment at a distance along the road, running on into the stage the car is heading for.</summary>
    public static (Segment Seg, StageDef Stage, double Offset) Ahead(World w, int index)
    {
        var s = w.Stage;
        if (index < s.Segments.Count) return (s.Segments[index], s, 0);
        if (w.Level == StageBook.Levels - 1) return (s.Segments[^1], s, 0);
        int right = w.Player.X >= 0 ? 1 : 0;
        var next = StageBook.Get(w.Level + 1, w.Branch + right);
        int i = Math.Min(index - s.Segments.Count + 2, next.Segments.Count - 1);
        return (next.Segments[i], next, (right == 1 ? 1 : -1) * s.Segments[^1].Split);
    }
}
