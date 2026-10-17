using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAOutRun.Engine;

namespace AVAOutRun.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAOutRun/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused or GameState.GameOver;

    private const double W = Field.Width, H = Field.Height, RW = Field.RoadWidth, SL = Field.SegmentLength;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, W, H));
        if (InGame) DrawRoad(dc, g.World);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default:
                if (InGame) DrawHud(dc);
                if (InGame && g.Phase == Phase.Radio) DrawRadio(dc);
                if (InGame && g.Phase == Phase.Countdown) DrawLights(dc);
                DrawMessages(dc);
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, W, H));
            Text(dc, "PAUSED", new Point(512, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(512, 330), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, H - 18), 24, Yellow, center: true);
    }

    // ───────────────────────────── the road ─────────────────────────────

    /// <summary>Where a segment's near edge falls on the screen, for the sprites.</summary>
    private readonly (double X, double Y, double Scale, double Clip, double Split, double Off, bool Drawn)[] _proj = new (double, double, double, double, double, double, bool)[Field.DrawDistance + 2];

    private static uint Mix(uint a, uint b, double f)
    {
        uint C(int shift) => (uint)((((a >> shift) & 0xFF) * (1 - f) + ((b >> shift) & 0xFF) * f)) << shift;
        return 0xFF000000 | C(16) | C(8) | C(0);
    }

    private static uint Shade(uint c, double f) => Mix(c, 0xFF000000, f);

    private void DrawRoad(DrawingContext dc, World w)
    {
        var p = w.Player;
        var look = w.Stage.Look;
        double horizon = H * 0.5;
        // The sky, and what's far off.
        for (int i = 0; i < 24; i++)
            dc.FillRectangle(Brush(Mix(look.SkyTop, look.SkyLow, i / 23.0)), new Rect(0, i * horizon / 24, W, horizon / 24 + 1));
        DrawBackdrop(dc, look, w.SkyOffset, horizon);
        dc.FillRectangle(Brush(look.Land1), new Rect(0, horizon - 2, W, H - horizon + 2));
        double depth = Field.CameraDepth;
        int baseIndex = (int)(p.Z / SL);
        double basePercent = p.Z % SL / SL;
        var (pseg, _, _) = Sim.Ahead(w, (int)((p.Z + Field.PlayerZ) / SL));
        double pp = (p.Z + Field.PlayerZ) % SL / SL;
        double playerY = pseg.Y1 + (pseg.Y2 - pseg.Y1) * pp;
        var (bseg, _, _) = Sim.Ahead(w, baseIndex);
        double maxy = H, x = 0, dx = -(bseg.Curve * basePercent);
        double camY = playerY + Field.CameraHeight;
        for (int n = 0; n < Field.DrawDistance; n++)
        {
            var (seg, stage, off) = Sim.Ahead(w, baseIndex + n);
            double z1 = n * SL - basePercent * SL, z2 = z1 + SL;
            double cx1 = p.X * RW - x, cx2 = p.X * RW - x - dx;
            double s1 = depth / Math.Max(z1, 1), s2 = depth / Math.Max(z2, 1);
            double sx1 = W / 2 + s1 * (off * RW - cx1) * W / 2, sx2 = W / 2 + s2 * (off * RW - cx2) * W / 2;
            double sy1 = H / 2 - s1 * (seg.Y1 - camY) * H / 2, sy2 = H / 2 - s2 * (seg.Y2 - camY) * H / 2;
            double sw1 = s1 * RW * W / 2, sw2 = s2 * RW * W / 2;
            _proj[n] = (sx1, sy1, s1, maxy, seg.Split, off, false);
            x += dx;
            dx += seg.Curve;
            if (z1 <= depth || sy2 >= sy1 || sy2 >= maxy) continue;
            _proj[n].Drawn = true;
            DrawSegment(dc, stage.Look, seg, sx1, sy1, sw1, sx2, sy2, sw2, n);
            maxy = sy1;
        }
        // Scenery and traffic, far to near.
        for (int n = Field.DrawDistance - 1; n > 0; n--)
        {
            var pr = _proj[n];
            if (!pr.Drawn && pr.Clip >= H) continue;
            var (seg, _, _) = Sim.Ahead(w, baseIndex + n);
            using (dc.PushClip(new Rect(0, 0, W, Math.Max(0, pr.Clip))))
            {
                foreach (var prop in seg.Props)
                {
                    double px = pr.X + pr.Scale * prop.Offset * RW * W / 2;
                    if (prop.Kind == Scenery.Arch) { DrawArch(dc, pr.X, pr.Y, pr.Scale); continue; }
                    if (prop.Offset == 0 && prop.Kind == Scenery.Sign) { DrawForkSign(dc, pr.X, pr.Y, pr.Scale); continue; }
                    DrawProp(dc, prop.Kind, px, pr.Y, pr.Scale, seg.Index * 7 + (int)(prop.Offset * 10));
                }
                foreach (var car in w.Cars)
                {
                    int ci = (int)(car.Z / SL);
                    if (ci != baseIndex + n) continue;
                    double cx = pr.X + pr.Scale * car.X * RW * W / 2;
                    DrawTraffic(dc, car, cx, pr.Y, pr.Scale);
                }
            }
        }
        DrawCar(dc, p, w.Clock);
    }

    private void DrawSegment(DrawingContext dc, Look look, Segment seg, double x1, double y1, double w1, double x2, double y2, double w2, int n)
    {
        bool light = seg.Index / Field.RumbleLength % 2 == 0;
        double fog = Math.Clamp((n - Field.DrawDistance * 0.55) / (Field.DrawDistance * 0.45), 0, 0.8);
        uint Fog(uint c) => Mix(c, look.SkyLow, fog);
        dc.FillRectangle(Brush(Fog(light ? look.Land1 : look.Land2)), new Rect(0, y2, W, y1 - y2 + 1));
        void Strip(double c1, double c2)
        {
            double r1 = w1 / 6.5, r2 = w2 / 6.5;
            Poly(dc, Fog(light ? look.Rumble : 0xFFD03030), null, 0, new Point(c1 - w1 - r1, y1), new Point(c1 - w1, y1), new Point(c2 - w2, y2), new Point(c2 - w2 - r2, y2));
            Poly(dc, Fog(light ? look.Rumble : 0xFFD03030), null, 0, new Point(c1 + w1 + r1, y1), new Point(c1 + w1, y1), new Point(c2 + w2, y2), new Point(c2 + w2 + r2, y2));
            Poly(dc, Fog(light ? look.Road : Shade(look.Road, 0.06)), null, 0, new Point(c1 - w1, y1), new Point(c1 + w1, y1), new Point(c2 + w2, y2), new Point(c2 - w2, y2));
            if (light)
            {
                double l1 = w1 / 40, l2 = w2 / 40;
                foreach (double f in new[] { -1 / 3.0, 1 / 3.0 })
                    Poly(dc, Fog(0xFFF0F0F0), null, 0, new Point(c1 + w1 * 2 * f - l1, y1), new Point(c1 + w1 * 2 * f + l1, y1), new Point(c2 + w2 * 2 * f + l2, y2), new Point(c2 + w2 * 2 * f - l2, y2));
            }
        }
        if (seg.Split > 0)
        {
            double o1 = seg.Split * w1, o2 = seg.Split * w2;
            Strip(x1 - o1, x2 - o2);
            Strip(x1 + o1, x2 + o2);
        }
        else Strip(x1, x2);
        if (seg.Checkpoint) Poly(dc, 0xFFF0F0F0, null, 0, new Point(x1 - w1, y1), new Point(x1 + w1, y1), new Point(x2 + w2, y2), new Point(x2 - w2, y2));
    }

    private void DrawBackdrop(DrawingContext dc, Look look, double offset, double horizon)
    {
        double shift = (offset * W) % W;
        if (shift < 0) shift += W;
        uint far = look.Far;
        // The sun, low.
        dc.FillEllipse(W * 0.72 - shift * 0.1, horizon - 120, 46, Alpha(0xFFFFF0C0, 0.9));
        for (int copy = -1; copy <= 1; copy++)
        {
            double ox = copy * W - shift;
            switch (look.Backdrop)
            {
                case 0:
                    // The sea, and an island.
                    dc.FillRectangle(Brush(far), new Rect(ox, horizon - 24, W + 1, 26));
                    Poly(dc, Shade(far, 0.3), null, 0, new Point(ox + 120, horizon - 22), new Point(ox + 200, horizon - 58), new Point(ox + 290, horizon - 40), new Point(ox + 360, horizon - 22));
                    for (int k = 0; k < 6; k++) dc.FillRectangle(Brush(Alpha(0xFFFFFFFF, 0.4)), new Rect(ox + 60 + k * 160, horizon - 12 + (k % 2) * 6, 40, 2));
                    break;
                case 1:
                    // Rolling hills.
                    for (int k = 0; k < 8; k++)
                        dc.DrawEllipse(Brush(Shade(far, 0.1 + 0.05 * (k % 2))), null, new Point(ox + k * 140 + 40, horizon + 10), 130, 46 + 14 * (k % 3));
                    break;
                case 2:
                    // Mountains.
                    for (int k = 0; k < 7; k++)
                    {
                        double bx = ox + k * 160 - 20, hgt = 90 + Hash(k, 1, 4) % 70;
                        Poly(dc, Shade(far, 0.05 * (k % 3)), null, 0, new Point(bx, horizon), new Point(bx + 90, horizon - hgt), new Point(bx + 190, horizon));
                        Poly(dc, Alpha(0xFFFFFFFF, 0.7), null, 0, new Point(bx + 70, horizon - hgt + 22), new Point(bx + 90, horizon - hgt), new Point(bx + 112, horizon - hgt + 24));
                    }
                    break;
                default:
                    // A skyline.
                    for (int k = 0; k < 18; k++)
                    {
                        double bx = ox + k * 58, hgt = 30 + Hash(k, 2, 6) % 80;
                        dc.FillRectangle(Brush(Shade(far, 0.06 * (k % 3))), new Rect(bx, horizon - hgt, 50, hgt + 1));
                        for (int j = 0; j < hgt / 14; j++) dc.FillRectangle(Brush(Alpha(0xFFFFF0B0, 0.5)), new Rect(bx + 8 + (j % 3) * 12, horizon - hgt + 6 + j * 12, 5, 5));
                    }
                    break;
            }
        }
        // Clouds.
        for (int k = 0; k < 5; k++)
        {
            double cx = ((k * 260 - shift * 0.5) % (W + 300) + W + 300) % (W + 300) - 150, cy = 70 + (Hash(k, 3, 3) % 120);
            dc.DrawEllipse(Brush(Alpha(0xFFFFFFFF, 0.75)), null, new Point(cx, cy), 70, 16);
            dc.DrawEllipse(Brush(Alpha(0xFFFFFFFF, 0.75)), null, new Point(cx + 30, cy - 10), 40, 14);
        }
    }

    // ───────────────────────────── by the road ─────────────────────────────

    /// <summary>A piece of scenery, its foot at (x, y), sized by the road's scale there.</summary>
    private void DrawProp(DrawingContext dc, Scenery k, double x, double y, double scale, int salt)
    {
        double u = scale * W / 2;   // screen pixels per road unit
        double S(double units) => units * u;
        if (S(100) < 0.6) return;
        switch (k)
        {
            case Scenery.Palm:
            {
                double h = S(2600), lean = S(260) * (salt % 2 == 0 ? 1 : -1);
                dc.DrawLine(Pen(0xFF8A6A40, Math.Max(1, S(110))), new Point(x, y), new Point(x + lean, y - h));
                for (int i = 0; i < 7; i++)
                {
                    double a = -Math.PI / 2 + (i - 3) * 0.45;
                    Poly(dc, i % 2 == 0 ? 0xFF2A8A3A : 0xFF3AA048, null, 0, new Point(x + lean, y - h), new Point(x + lean + Math.Cos(a - 0.4) * S(700), y - h + Math.Sin(a) * S(380) + S(300)), new Point(x + lean + Math.Cos(a) * S(1100), y - h + Math.Sin(a) * S(500) + S(500)));
                }
                break;
            }
            case Scenery.Bush:
                dc.DrawEllipse(Brush(0xFF2E7A34), null, new Point(x, y - S(250)), S(450), S(280));
                dc.DrawEllipse(Brush(0xFF3A9040), null, new Point(x - S(120), y - S(330)), S(250), S(180));
                break;
            case Scenery.Rock:
                Poly(dc, 0xFF8A7A6A, 0xFF5A4A40, Math.Max(1, S(30)), new Point(x - S(700), y), new Point(x - S(500), y - S(700)), new Point(x + S(100), y - S(950)), new Point(x + S(650), y - S(400)), new Point(x + S(720), y));
                break;
            case Scenery.Cactus:
                dc.FillRectangle(Brush(0xFF3A8A4A), new Rect(x - S(110), y - S(1500), S(220), S(1500)), S(100));
                dc.FillRectangle(Brush(0xFF3A8A4A), new Rect(x - S(420), y - S(1000), S(140), S(500)), S(60));
                dc.FillRectangle(Brush(0xFF3A8A4A), new Rect(x - S(420), y - S(620), S(400), S(130)));
                dc.FillRectangle(Brush(0xFF3A8A4A), new Rect(x + S(280), y - S(1200), S(140), S(450)), S(60));
                dc.FillRectangle(Brush(0xFF3A8A4A), new Rect(x + S(40), y - S(870), S(380), S(130)));
                break;
            case Scenery.Pine:
                dc.FillRectangle(Brush(0xFF5A3A20), new Rect(x - S(70), y - S(400), S(140), S(400)));
                for (int i = 0; i < 3; i++)
                    Poly(dc, i % 2 == 0 ? 0xFF1E5A30 : 0xFF246838, null, 0, new Point(x - S(560 - i * 120), y - S(300 + i * 550)), new Point(x, y - S(1100 + i * 600)), new Point(x + S(560 - i * 120), y - S(300 + i * 550)));
                break;
            case Scenery.Cypress:
                dc.DrawEllipse(Brush(0xFF1E5028), null, new Point(x, y - S(1100)), S(260), S(1100));
                dc.DrawEllipse(Brush(0xFF286034), null, new Point(x - S(60), y - S(1200)), S(140), S(900));
                break;
            case Scenery.Wheat:
                for (int i = 0; i < 5; i++) dc.DrawLine(Pen(0xFFE8C860, Math.Max(1, S(60))), new Point(x - S(400) + i * S(200), y), new Point(x - S(380) + i * S(200) + S(60), y - S(500)));
                break;
            case Scenery.Windmill:
            {
                Poly(dc, 0xFFE8E0D0, 0xFF8A7A6A, Math.Max(1, S(20)), new Point(x - S(500), y), new Point(x - S(300), y - S(2000)), new Point(x + S(300), y - S(2000)), new Point(x + S(500), y));
                Poly(dc, 0xFFA04A30, null, 0, new Point(x - S(380), y - S(2000)), new Point(x, y - S(2400)), new Point(x + S(380), y - S(2000)));
                double a = _clock * 1.5 + salt;
                for (int i = 0; i < 4; i++)
                {
                    double t = a + i * Math.PI / 2;
                    dc.DrawLine(Pen(0xFF6A5A4A, Math.Max(1, S(80))), new Point(x, y - S(2000)), new Point(x + Math.Cos(t) * S(1300), y - S(2000) + Math.Sin(t) * S(1300)));
                }
                break;
            }
            case Scenery.House:
            {
                uint wall = (salt % 3) switch { 0 => 0xFFF0E8D8, 1 => 0xFFE8D0B0, _ => 0xFFD8E0E8 };
                dc.FillRectangle(Brush(wall), new Rect(x - S(900), y - S(1200), S(1800), S(1200)));
                Poly(dc, 0xFFC0603A, null, 0, new Point(x - S(1050), y - S(1200)), new Point(x, y - S(1800)), new Point(x + S(1050), y - S(1200)));
                for (int i = 0; i < 2; i++) dc.FillRectangle(Brush(0xFF3A5A8A), new Rect(x - S(600) + i * S(800), y - S(900), S(400), S(350)));
                dc.FillRectangle(Brush(0xFF6A4A30), new Rect(x - S(180), y - S(600), S(360), S(600)));
                break;
            }
            case Scenery.Column:
                dc.FillRectangle(Brush(0xFFE8E0C8), new Rect(x - S(180), y - S(2200), S(360), S(2200)));
                for (int i = 0; i < 3; i++) dc.FillRectangle(Brush(0xFFC8C0A8), new Rect(x - S(120) + i * S(110), y - S(2100), S(30), S(2000)));
                dc.FillRectangle(Brush(0xFFF0E8D0), new Rect(x - S(260), y - S(2350), S(520), S(160)));
                break;
            case Scenery.Lamp:
                dc.DrawLine(Pen(0xFF3A3A44, Math.Max(1, S(70))), new Point(x, y), new Point(x, y - S(2400)));
                dc.DrawLine(Pen(0xFF3A3A44, Math.Max(1, S(60))), new Point(x, y - S(2400)), new Point(x - S(500) * Math.Sign(salt % 2 - 0.5), y - S(2400)));
                dc.FillEllipse(x - S(500) * Math.Sign(salt % 2 - 0.5), y - S(2350), Math.Max(1, S(110)), 0xFFFFF0B0);
                break;
            case Scenery.Umbrella:
                dc.DrawLine(Pen(0xFFE0E0E0, Math.Max(1, S(40))), new Point(x, y), new Point(x, y - S(1100)));
                Poly(dc, salt % 2 == 0 ? 0xFFE04040 : 0xFF3A70E0, null, 0, new Point(x - S(600), y - S(950)), new Point(x, y - S(1300)), new Point(x + S(600), y - S(950)));
                break;
            case Scenery.Sign:
                dc.DrawLine(Pen(0xFF8A8A8A, Math.Max(1, S(50))), new Point(x, y), new Point(x, y - S(1200)));
                dc.FillRectangle(Brush(0xFF2A7A3A), new Rect(x - S(500), y - S(1700), S(1000), S(550)));
                dc.DrawRectangle(null, Pen(0xFFF0F0F0, Math.Max(1, S(40))), new Rect(x - S(460), y - S(1660), S(920), S(470)));
                break;
            case Scenery.Snowman:
                dc.FillEllipse(x, y - S(350), S(350), 0xFFF8F8FF);
                dc.FillEllipse(x, y - S(900), S(240), 0xFFF8F8FF);
                dc.FillEllipse(x, y - S(900), Math.Max(0.5, S(40)), 0xFFE07020);
                break;
            case Scenery.Barrel:
                dc.FillRectangle(Brush(0xFFC05030), new Rect(x - S(250), y - S(600), S(500), S(600)), S(60));
                dc.FillRectangle(Brush(0xFFE0E0E0), new Rect(x - S(250), y - S(420), S(500), S(80)));
                break;
            case Scenery.Billboard:
                dc.FillRectangle(Brush(0xFF6A6A6A), new Rect(x - S(600), y - S(1300), S(80), S(1300)));
                dc.FillRectangle(Brush(0xFF6A6A6A), new Rect(x + S(520), y - S(1300), S(80), S(1300)));
                dc.FillRectangle(Brush(0xFF1A3A8A), new Rect(x - S(1100), y - S(2300), S(2200), S(1000)));
                if (S(2200) > 60) Text(dc, "AVA OUT RUN", new Point(x, y - S(1800)), S(500), 0xFFFFD040, center: true, shadow: false);
                break;
        }
    }

    /// <summary>The sign in the middle of a fork: one way left, the other right.</summary>
    private void DrawForkSign(DrawingContext dc, double x, double y, double scale)
    {
        double u = scale * W / 2;
        double S(double units) => units * u;
        if (S(100) < 0.6) return;
        dc.FillRectangle(Brush(0xFF8A8A8A), new Rect(x - S(40), y - S(1300), S(80), S(1300)));
        dc.FillRectangle(Brush(0xFF2A5AB0), new Rect(x - S(900), y - S(1900), S(1800), S(650)));
        Poly(dc, 0xFFF0F0F0, null, 0, new Point(x - S(800), y - S(1575)), new Point(x - S(450), y - S(1800)), new Point(x - S(450), y - S(1350)));
        Poly(dc, 0xFFF0F0F0, null, 0, new Point(x + S(800), y - S(1575)), new Point(x + S(450), y - S(1800)), new Point(x + S(450), y - S(1350)));
    }

    /// <summary>The checkpoint arch over the road.</summary>
    private void DrawArch(DrawingContext dc, double x, double y, double scale)
    {
        double u = scale * W / 2;
        double S(double units) => units * u;
        if (S(100) < 0.6) return;
        double half = S(RW * 1.3);
        dc.FillRectangle(Brush(0xFFE0E0E0), new Rect(x - half - S(150), y - S(2600), S(300), S(2600)));
        dc.FillRectangle(Brush(0xFFE0E0E0), new Rect(x + half - S(150), y - S(2600), S(300), S(2600)));
        dc.FillRectangle(Brush(0xFFE04030), new Rect(x - half - S(150), y - S(3200), half * 2 + S(300), S(650)));
        if (S(400) > 8) Text(dc, Engine.World.Level == StageBook.Levels - 1 ? "GOAL" : "CHECKPOINT", new Point(x, y - S(2880)), S(440), 0xFFFFFFFF, center: true, shadow: false);
    }

    /// <summary>A car, van or lorry from behind.</summary>
    private void DrawTraffic(DrawingContext dc, Car car, double x, double y, double scale)
    {
        double u = scale * W / 2;
        double S(double units) => units * u;
        double hw = S(car.Width * RW / 2), h = car.Kind switch { 0 => S(720), 1 => S(1050), _ => S(1500) };
        if (hw < 1) return;
        dc.DrawEllipse(Brush(0x50000000), null, new Point(x, y), hw * 1.1, hw * 0.12);
        uint body = car.Colour;
        if (car.Kind == 2)
        {
            dc.FillRectangle(Brush(0xFFE8E8E8), new Rect(x - hw, y - h, hw * 2, h * 0.78), S(30));
            dc.FillRectangle(Brush(body), new Rect(x - hw, y - h * 0.3, hw * 2, h * 0.24));
        }
        else
        {
            dc.FillRectangle(Brush(body), new Rect(x - hw, y - h * 0.62, hw * 2, h * 0.5), S(60));
            dc.FillRectangle(Brush(Shade(body, 0.15)), new Rect(x - hw * 0.78, y - h, hw * 1.56, h * 0.42), S(80));
            dc.FillRectangle(Brush(0xFF203040), new Rect(x - hw * 0.66, y - h * 0.93, hw * 1.32, h * 0.3), S(40));
        }
        dc.FillRectangle(Brush(0xFFFF3030), new Rect(x - hw * 0.92, y - h * 0.42, hw * 0.3, h * 0.1));
        dc.FillRectangle(Brush(0xFFFF3030), new Rect(x + hw * 0.62, y - h * 0.42, hw * 0.3, h * 0.1));
        dc.FillRectangle(Brush(0xFF1A1A1A), new Rect(x - hw * 0.95, y - h * 0.14, hw * 0.4, h * 0.14));
        dc.FillRectangle(Brush(0xFF1A1A1A), new Rect(x + hw * 0.55, y - h * 0.14, hw * 0.4, h * 0.14));
    }

    /// <summary>The red convertible from behind, with the driver and a passenger: leaning into the steering, bouncing, or crashing.</summary>
    private void DrawCar(DrawingContext dc, Player p, double t)
    {
        double x = W / 2, y = H - 40;
        double bounce = p.Speed > 1 ? Math.Sin(t * 25) * 1.5 * (p.Speed / Field.MaxSpeed) : 0;
        double angle = p.Steer * 0.04;
        double roll = 0;
        if (p.Crash == Crash.Spin) angle = p.CrashTime * 5;
        if (p.Crash == Crash.Roll) { roll = Math.Abs(Math.Sin(p.CrashTime * 3)) * 90; angle = p.CrashTime * 2.2; }
        using (dc.PushTransform(Matrix.CreateRotation(angle) * Matrix.CreateTranslation(x, y + bounce - roll)))
        {
            dc.DrawEllipse(Brush(0x60000000), null, new Point(0, 4), 150, 16);
            // Tyres.
            dc.FillRectangle(Brush(0xFF141414), new Rect(-138, -40, 46, 44), 8);
            dc.FillRectangle(Brush(0xFF141414), new Rect(92, -40, 46, 44), 8);
            // The body.
            Poly(dc, 0xFFD81E1E, 0xFF5A0A0A, 2, new Point(-142, -26), new Point(-132, -86), new Point(-90, -100), new Point(90, -100), new Point(132, -86), new Point(142, -26), new Point(120, -12), new Point(-120, -12));
            dc.FillRectangle(Brush(0xFFA01414), new Rect(-130, -58, 260, 10));
            // Tail lights and the grille.
            dc.FillRectangle(Brush(0xFFFF6040), new Rect(-128, -80, 58, 16), 3);
            dc.FillRectangle(Brush(0xFFFF6040), new Rect(70, -80, 58, 16), 3);
            dc.FillRectangle(Brush(0xFF2A2A2A), new Rect(-50, -82, 100, 18), 3);
            dc.FillRectangle(Brush(0xFFE8E8E8), new Rect(-22, -40, 44, 12), 2);
            // The windscreen's edge and the two of them.
            dc.DrawLine(Pen(0xFF404850, 4), new Point(-92, -100), new Point(-76, -128));
            dc.DrawLine(Pen(0xFF404850, 4), new Point(92, -100), new Point(76, -128));
            dc.DrawLine(Pen(0xFF404850, 3), new Point(-76, -128), new Point(76, -128));
            double sway = p.Steer * 4;
            dc.FillEllipse(-36 + sway, -122, 17, 0xFF2A1A10);
            dc.FillRectangle(Brush(0xFF2A3A6A), new Rect(-54 + sway, -112, 36, 14), 4);
            dc.FillEllipse(38 + sway, -120, 16, 0xFFF0D070);
            // Her hair in the wind.
            Poly(dc, 0xFFF0D070, null, 0, new Point(28 + sway, -132), new Point(70 + sway + Math.Sin(t * 12) * 6, -118), new Point(50 + sway, -108));
            if (p.Skid > 0 && p.Crash == Crash.None)
                for (int i = 0; i < 4; i++) dc.FillEllipse(-110 + (i % 2) * 220 + Math.Sin(t * 30 + i) * 8, -2, 10 + i * 2, Alpha(0xFFE0E0E0, 0.5));
        }
    }

    // ───────────────────────────── around the edges ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        var p = w.Player;
        // The time, big.
        Text(dc, "TIME", new Point(W / 2, 22), 24, Yellow, center: true);
        bool low = w.TimeLeft < 10 && (int)(_clock * 3) % 2 == 0;
        Text(dc, Math.Ceiling(w.TimeLeft).ToString("0"), new Point(W / 2, 64), 64, low ? Red : 0xFFFFE040, center: true);
        Text(dc, "SCORE", new Point(24, 22), 22, 0xFFFF80C0);
        Text(dc, g.Score.ToString("N0").Replace(",", " "), new Point(24, 52), 36, White);
        Text(dc, "STAGE " + (w.Level + 1), new Point(W - 24, 22), 24, 0xFF80E0FF, right: true);
        Text(dc, w.Stage.Look.Name, new Point(W - 24, 50), 22, White, right: true);
        // The speed, the revs and the gear.
        double kmh = p.Kmh;
        Text(dc, kmh.ToString("0"), new Point(150, H - 50), 54, White, right: true);
        Text(dc, "KM/H", new Point(156, H - 42), 22, Yellow);
        int bars = (int)Math.Round(kmh / Field.TopKmh * 20);
        for (int i = 0; i < 20; i++)
            dc.FillRectangle(Brush(i < bars ? (i < 12 ? 0xFF40E060 : i < 17 ? 0xFFE0E040 : 0xFFE04040) : 0x40FFFFFF), new Rect(30 + i * 12, H - 100, 9, 18 + i * 1.2));
        Text(dc, p.High ? "HIGH" : "LOW", new Point(250, H - 50), 30, p.High ? 0xFFFF8040 : 0xFF80E0FF);
        DrawRouteMap(dc, w, W - 200, H - 132);
    }

    /// <summary>The five levels of stages as a little tree, with the road taken lit.</summary>
    private void DrawRouteMap(DrawingContext dc, World w, double x0, double y0)
    {
        dc.FillRectangle(Brush(0x80000000), new Rect(x0 - 10, y0 - 10, 190, 130), 8);
        for (int l = 0; l < StageBook.Levels; l++)
        for (int b = 0; b <= l; b++)
        {
            double x = x0 + l * 40 + 4, y = y0 + 50 + (b - l / 2.0) * 24;
            if (l < StageBook.Levels - 1)
                foreach (int nb in new[] { b, b + 1 })
                    dc.DrawLine(Pen(w.Route.Count > l + 1 && w.Route[l] == b && w.Route[l + 1] == nb ? 0xFFFFE040 : 0x60FFFFFF, 2), new Point(x, y), new Point(x + 40, y0 + 50 + (nb - (l + 1) / 2.0) * 24));
            bool taken = w.Route.Count > l && w.Route[l] == b, here = l == w.Level && b == w.Branch;
            dc.FillEllipse(x, y, here ? 7 : 5, here ? ((int)(_clock * 4) % 2 == 0 ? 0xFFFF4040 : 0xFFFFFFFF) : taken ? 0xFFFFE040 : 0xFF8090A0);
        }
    }

    private void DrawRadio(DrawingContext dc)
    {
        var g = Engine;
        double cx = W / 2, cy = H / 2 + 40;
        dc.FillRectangle(Brush(0xA0000000), new Rect(0, 0, W, H));
        dc.DrawRectangle(Brush(0xFF2A2420), Pen(0xFF8A7A60, 4), new Rect(cx - 360, cy - 170, 720, 340), 24, 24);
        Text(dc, "SELECT MUSIC", new Point(cx, cy - 120), 48, 0xFFFFC040, center: true);
        // The dial, and the needle on the station chosen.
        dc.DrawRectangle(Brush(0xFF0A1A10), Pen(0xFF4A6A50, 2), new Rect(cx - 300, cy - 70, 600, 70), 8, 8);
        for (int i = 0; i <= 30; i++) dc.DrawLine(Pen(0xFF3AA060, 1.5), new Point(cx - 280 + i * 18.6, cy - 60), new Point(cx - 280 + i * 18.6, cy - 60 + (i % 5 == 0 ? 20 : 10)));
        double nx = cx - 200 + g.Station * 200;
        dc.DrawLine(Pen(0xFFFF4040, 4), new Point(nx, cy - 66), new Point(nx, cy - 4));
        for (int i = 0; i < GameEngine.Stations.Length; i++)
            Text(dc, GameEngine.Stations[i], new Point(cx - 200 + i * 200, cy + 40), 26, i == g.Station ? 0xFFFFE040 : 0xFF8A9A8A, center: true);
        Text(dc, "LEFT / RIGHT TO TUNE  -  FIRE TO START", new Point(cx, cy + 120), 24, White, center: true);
    }

    private void DrawLights(DrawingContext dc)
    {
        var g = Engine;
        int lit = Math.Min(4, (int)(g.PhaseTime / 0.8) + 1);
        double cx = W / 2, cy = 200;
        dc.DrawRectangle(Brush(0xFF1A1A1A), Pen(0xFF4A4A4A, 3), new Rect(cx - 150, cy - 40, 300, 80), 12, 12);
        for (int i = 0; i < 4; i++)
        {
            bool on = i < lit;
            uint c = i == 3 ? (lit >= 4 ? 0xFF40FF60 : 0xFF103010) : (on && lit < 4 ? 0xFFFF3030 : 0xFF301010);
            dc.FillEllipse(cx - 105 + i * 70, cy, 26, c);
        }
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = W / 2, cy = 230;
        Text(dc, msg, new Point(cx, cy), msg.Length > 16 ? 60 : 84, 0xFFFFE040, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 56), 28, White, center: true);
    }

    // ───────────────────────────── title and scores ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = W / 2;
        var look = StageBook.Looks[0];
        for (int i = 0; i < 24; i++) dc.FillRectangle(Brush(Mix(0xFF2A1A6A, 0xFFF8A060, i / 23.0)), new Rect(0, i * 16, W, 17));
        dc.FillEllipse(cx, 380, 120, 0xFFFFE070);
        for (int i = 0; i < 5; i++) dc.FillRectangle(Brush(Mix(0xFF2A1A6A, 0xFFF8A060, 0.9)), new Rect(cx - 130, 300 + i * 18, 260, 6 + i));
        dc.FillRectangle(Brush(look.Far), new Rect(0, 380, W, 30));
        dc.FillRectangle(Brush(look.Land1), new Rect(0, 410, W, H - 410));
        // The road to the sunset.
        Poly(dc, look.Road, null, 0, new Point(cx - 20, 410), new Point(cx + 20, 410), new Point(cx + 520, H), new Point(cx - 520, H));
        for (int i = 0; i < 8; i++)
        {
            double f = ((i + _clock * 2) % 8) / 8, y = 410 + f * f * (H - 410), half = 20 + f * f * 500;
            dc.FillRectangle(Brush(0xFFF0F0F0), new Rect(cx - half * 0.03 - 2, y, half * 0.06 + 4, 4 + f * 20));
        }
        DrawProp(dc, Scenery.Palm, 160, 640, 0.00026, 1);
        DrawProp(dc, Scenery.Palm, 880, 660, 0.0003, 2);
        DrawCar(dc, new Player { Speed = Field.MaxSpeed * 0.8, Steer = Math.Sin(_clock) * 0.6 }, _clock);
        string title = "AVA OUT RUN";
        for (int i = 0; i < title.Length; i++)
        {
            double tx = cx - title.Length * 30 + 30 + i * 60, ty = 120 + Math.Sin(_clock * 2.4 + i * 0.5) * 4;
            Text(dc, title[i].ToString(), new Point(tx + 4, ty + 4), 116, 0xFF401040, center: true, shadow: false);
            Text(dc, title[i].ToString(), new Point(tx, ty), 116, i < 3 ? 0xFFFFE040 : (i % 2 == 0 ? 0xFFFF6080 : 0xFFFF9050), center: true, shadow: false);
        }
        Text(dc, "AFTER SEGA'S 1986 ARCADE GAME", new Point(cx, 190), 24, 0xFFFFF0E0, center: true);
        Text(dc, "A RED CONVERTIBLE, THE OPEN ROAD, FIVE STAGES AND A FORK AT THE END OF EACH.", new Point(cx, 236), 24, White, center: true);
        Text(dc, "BEAT THE CLOCK TO EACH CHECKPOINT. CHOOSE YOUR ROAD TO ONE OF FIVE GOALS.", new Point(cx, 262), 24, White, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER TO START", new Point(cx, 476), 44, 0xFFFFE040, center: true);
        Text(dc, "LEFT/RIGHT: STEER   UP OR X: ACCELERATE   DOWN: BRAKE   SPACE OR Z: GEAR   P: PAUSE", new Point(cx, 520), 20, 0xFFFFF0E0, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = W / 2;
        for (int i = 0; i < 24; i++) dc.FillRectangle(Brush(Mix(0xFF1A1040, 0xFF8A3A60, i / 23.0)), new Rect(0, i * 32, W, 33));
        Text(dc, "BEST OUT RUNNERS", new Point(cx, 110), 64, 0xFFFFE040, center: true);
        Text(dc, "RANK NAME        SCORE  STAGES", new Point(cx, 180), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,12:N0}   {e.Stage,4}".Replace(",", " "), new Point(cx, 226 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = W / 2;
        for (int i = 0; i < 24; i++) dc.FillRectangle(Brush(Mix(0xFF1A1040, 0xFF8A3A60, i / 23.0)), new Rect(0, i * 32, W, 33));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString("N0").Replace(",", " "), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    /// <summary>A rounded rectangle with a radius worked out in doubles.</summary>
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);

    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
}
