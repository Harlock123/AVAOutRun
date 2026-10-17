namespace AVAOutRun.Engine;

/// <summary>Steering (left -1, right +1), up and down, fire (the accelerator), and the button (the gear lever).</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false, bool Act = false)
{
    public bool Gas => Fire || Dy < 0;
    public bool Brake => Dy > 0;
}

/// <summary>The screen, and the road's measures: segments of road, its width, the camera.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double SegmentLength = 200, RoadWidth = 2000, CameraHeight = 1000;
    public const int RumbleLength = 3, DrawDistance = 220;
    /// <summary>How far the camera sees (from a field of view of 100°).</summary>
    public static readonly double CameraDepth = 1 / Math.Tan(100 * Math.PI / 360);
    /// <summary>How far ahead of the camera the car sits.</summary>
    public static readonly double PlayerZ = CameraHeight * CameraDepth;
    /// <summary>Top speed (in road units a second) and how that shows on the speedometer.</summary>
    public const double MaxSpeed = SegmentLength * 58;
    public const double TopKmh = 293;
}

/// <summary>What stands by the road.</summary>
public enum Scenery
{
    Palm, Bush, Rock, Cactus, Pine, Cypress, Wheat, Windmill, House, Column, Lamp, Umbrella, Sign, Snowman, Barrel, Arch, Billboard,
}

/// <summary>Something by the road: what, and how far to the side (1 is the road's edge).</summary>
public readonly record struct Prop(Scenery Kind, double Offset);

/// <summary>A length of road: how it bends and climbs, and what stands beside it.</summary>
public sealed class Segment
{
    public int Index;
    /// <summary>The road's height at the segment's start and end.</summary>
    public double Y1, Y2;
    public double Curve;
    /// <summary>In the fork at a stage's end: how far apart the two roads' middles are each side (in half road widths).</summary>
    public double Split;
    public List<Prop> Props = [];
    public bool Checkpoint;
}

/// <summary>How a stage looks: its sky, its land, its road, and what grows or stands beside it.</summary>
public sealed record Look(string Name, uint SkyTop, uint SkyLow, uint Land1, uint Land2, uint Road, uint Rumble, uint Far, Scenery[] Props, int Backdrop);

/// <summary>A stage's road.</summary>
public sealed class StageDef
{
    public int Level, Branch;
    public Look Look = StageBook.Looks[0];
    public List<Segment> Segments = [];
    public double Length => Segments.Count * Field.SegmentLength;
    /// <summary>The segments at the end where the road splits in two.</summary>
    public const int ForkLength = 140;
    public int ForkStart => Segments.Count - ForkLength;
    public Segment At(double z) => Segments[Math.Clamp((int)Math.Floor(z / Field.SegmentLength), 0, Segments.Count - 1)];
}

/// <summary>
/// The fifteen stages, five levels deep: one to start, then at each stage's end the road forks left or right, and
/// each level has one more stage to choose from, to five different goals.
/// </summary>
public static class StageBook
{
    public const int Levels = 5;

    public static readonly Look[] Looks =
    [
        new("SUNSET COAST", 0xFF3A8AE0, 0xFFA8D8F8, 0xFF3AA04A, 0xFF34944A, 0xFF6A6A72, 0xFFE8E8E8, 0xFF2A6AB0, [Scenery.Palm, Scenery.Palm, Scenery.Umbrella, Scenery.Bush], 0),
        new("OLIVE HILLS", 0xFF4A90E0, 0xFFC0E0F8, 0xFF6AA040, 0xFF62983C, 0xFF707070, 0xFFE8E8E8, 0xFF6A8A5A, [Scenery.Cypress, Scenery.Bush, Scenery.House], 1),
        new("SANDY BAY", 0xFF2A98E8, 0xFFB0E8FF, 0xFFE0C888, 0xFFD8C080, 0xFF6A6A72, 0xFFF0F0F0, 0xFF2A7AC0, [Scenery.Palm, Scenery.Umbrella, Scenery.Sign], 0),
        new("CANYON PASS", 0xFFE08840, 0xFFF8D090, 0xFFB86A3A, 0xFFAE6236, 0xFF706058, 0xFFE8E0D8, 0xFF8A4A2A, [Scenery.Rock, Scenery.Cactus, Scenery.Rock], 2),
        new("OLD TOWN", 0xFF5A80C0, 0xFFC0D0E8, 0xFF8A8A7A, 0xFF828274, 0xFF686868, 0xFFE0E0E0, 0xFF6A6A7A, [Scenery.House, Scenery.Lamp, Scenery.House], 3),
        new("SEA CLIFFS", 0xFF3A70C0, 0xFF90C8F0, 0xFF5A9A5A, 0xFF529252, 0xFF6A6A72, 0xFFE8E8E8, 0xFF2A5AA0, [Scenery.Rock, Scenery.Pine, Scenery.Bush], 0),
        new("WHEAT FIELDS", 0xFF4A98E8, 0xFFC8E8F8, 0xFFD8B850, 0xFFD0B048, 0xFF6A6A6A, 0xFFE8E8E8, 0xFF8AA060, [Scenery.Wheat, Scenery.Windmill, Scenery.Wheat], 1),
        new("SNOW PASS", 0xFF6A88B0, 0xFFD0E0F0, 0xFFE8F0F8, 0xFFDDE6F0, 0xFF707680, 0xFFE04040, 0xFF8A9AB8, [Scenery.Pine, Scenery.Snowman, Scenery.Pine], 2),
        new("DESERT FLATS", 0xFFF0A040, 0xFFFFE0A0, 0xFFE0B070, 0xFFD8A868, 0xFF706860, 0xFFF0E0D0, 0xFFC08050, [Scenery.Cactus, Scenery.Rock, Scenery.Barrel], 2),
        new("LAKESIDE", 0xFF3A8AE0, 0xFFB0D8F0, 0xFF4AA060, 0xFF42985A, 0xFF6A6A72, 0xFFE8E8E8, 0xFF3A7AB8, [Scenery.Pine, Scenery.Bush, Scenery.Lamp], 0),
        new("THE OBSERVATORY", 0xFF1A2A6A, 0xFF7A6AB0, 0xFF3A6A4A, 0xFF346246, 0xFF5A5A66, 0xFFE0E0E8, 0xFF2A2A5A, [Scenery.Pine, Scenery.Lamp, Scenery.Rock], 2),
        new("ANCIENT RUINS", 0xFFE0A060, 0xFFF8E0B0, 0xFFC8B080, 0xFFC0A878, 0xFF7A7068, 0xFFF0E8E0, 0xFFA08060, [Scenery.Column, Scenery.Column, Scenery.Bush], 1),
        new("THE HARBOUR", 0xFF4A90E0, 0xFFC0E0FF, 0xFF8A8A8A, 0xFF828282, 0xFF606068, 0xFFE8E8E8, 0xFF2A6AB0, [Scenery.Lamp, Scenery.Barrel, Scenery.Billboard], 3),
        new("THE CASTLE", 0xFF5A7AB0, 0xFFC8D8E8, 0xFF4A8A4A, 0xFF428242, 0xFF686868, 0xFFE8E8E8, 0xFF5A6A7A, [Scenery.Cypress, Scenery.Rock, Scenery.Pine], 1),
        new("THE VINEYARD", 0xFF5A98E0, 0xFFE0E8F8, 0xFF7AA040, 0xFF72983C, 0xFF6A6A6A, 0xFFE8E8E8, 0xFF7A8A5A, [Scenery.Bush, Scenery.Cypress, Scenery.House], 1),
    ];

    /// <summary>The look of the stage at a level and branch (level 0 has one, level 4 five).</summary>
    public static int LookIndex(int level, int branch) => level * (level + 1) / 2 + branch;

    private static readonly Dictionary<(int, int), StageDef> Built = [];

    public static StageDef Get(int level, int branch)
    {
        lock (Built)
        {
            if (!Built.TryGetValue((level, branch), out var s)) Built[(level, branch)] = s = Build(level, branch);
            return s;
        }
    }

    /// <summary>The seconds added to the clock for reaching a level (the first is the time to start with).</summary>
    public static readonly double[] Time = [75, 62, 60, 58, 56];

    private static StageDef Build(int level, int branch)
    {
        var look = Looks[LookIndex(level, branch)];
        var s = new StageDef { Level = level, Branch = branch, Look = look };
        var d = new Dice(1000 + level * 37 + branch * 11);
        double y = 0;
        void Add(int n, double curve, double climb, double easeIn = 0, double easeOut = 0)
        {
            double y0 = y;
            for (int i = 0; i < n; i++)
            {
                double f = (i + 1.0) / n;
                double c = curve * Math.Min(1, Math.Min(easeIn > 0 ? (i + 1) / (n * easeIn) : 1, easeOut > 0 ? (n - i) / (n * easeOut) : 1));
                double prev = y;
                y = y0 + climb * (1 - Math.Cos(f * Math.PI)) / 2;
                s.Segments.Add(new Segment { Index = s.Segments.Count, Y1 = prev, Y2 = y, Curve = c });
            }
        }
        // A straight start, then a run of bends and hills, harder deeper in.
        Add(60, 0, 0);
        int pieces = 22 + level * 2;
        double hard = 2.5 + level * 0.6;
        for (int p = 0; p < pieces; p++)
        {
            int n = 50 + d.Next(90);
            double curve = d.Next() < 0.25 ? 0 : (d.Next(2) == 0 ? -1 : 1) * d.Between(1, hard);
            double climb = d.Next() < 0.45 ? 0 : d.Between(-1, 1) * (1500 + level * 600);
            Add(n, curve, climb, 0.25, 0.25);
        }
        // Level off before the fork.
        Add(80, 0, -y);
        Add(StageDef.ForkLength, 0, 0);
        for (int i = 0; i < s.Segments.Count; i++)
        {
            var seg = s.Segments[i];
            int fromFork = i - s.ForkStart;
            if (fromFork >= 0) seg.Split = level == Levels - 1 ? 0 : Math.Min(1.8, Math.Max(0, (fromFork - 10) / 60.0) * 1.8);
            // Scenery along both sides, thick or thin by stretches.
            if (i > 10 && i % 3 == 0 && d.Next() < (i / 200 % 3 == 0 ? 0.85 : 0.45))
            {
                var kind = look.Props[d.Next(look.Props.Length)];
                double side = d.Next(2) == 0 ? -1 : 1;
                seg.Props.Add(new Prop(kind, side * (1.35 + d.Next() * 1.6 + Math.Max(0, seg.Split))));
            }
            if (fromFork >= 30 && fromFork % 20 == 0 && level < Levels - 1) seg.Props.Add(new Prop(Scenery.Sign, 0));
        }
        // The finish arch at the very end.
        s.Segments[^20].Checkpoint = true;
        s.Segments[^20].Props.Add(new Prop(Scenery.Arch, 0));
        // A start billboard.
        s.Segments[24].Props.Add(new Prop(Scenery.Billboard, -1.6));
        return s;
    }
}
