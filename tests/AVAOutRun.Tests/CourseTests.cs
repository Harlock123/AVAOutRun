using AVAOutRun.Engine;

namespace AVAOutRun.Tests;

public class CourseTests
{
    private static IEnumerable<StageDef> All() =>
        Enumerable.Range(0, StageBook.Levels).SelectMany(l => Enumerable.Range(0, l + 1).Select(b => StageBook.Get(l, b)));

    [Fact]
    public void There_are_fifteen_stages_each_with_its_own_look()
    {
        Assert.Equal(15, All().Count());
        Assert.Equal(15, All().Select(s => s.Look.Name).Distinct().Count());
    }

    [Fact]
    public void Every_stage_starts_and_ends_level_and_bends_gently_from_one_segment_to_the_next()
    {
        foreach (var s in All())
        {
            Assert.Equal(0, s.Segments[0].Y1, 3);
            Assert.Equal(0, s.Segments[^1].Y2, 3);
            for (int i = 1; i < s.Segments.Count; i++)
            {
                Assert.Equal(s.Segments[i - 1].Y2, s.Segments[i].Y1, 3);
                Assert.True(Math.Abs(s.Segments[i].Curve - s.Segments[i - 1].Curve) < 0.6, $"{s.Look.Name}: the bend jumps at {i}");
            }
        }
    }

    [Fact]
    public void Each_stage_but_the_last_forks_at_its_end_and_the_last_ends_in_a_goal()
    {
        foreach (var s in All())
        {
            var end = s.Segments[^1];
            if (s.Level < StageBook.Levels - 1) Assert.True(end.Split > 1.5, s.Look.Name);
            else Assert.Equal(0, end.Split);
            Assert.Contains(s.Segments, x => x.Checkpoint);
            Assert.All(s.Segments.Take(s.ForkStart), x => Assert.Equal(0, x.Split));
        }
    }

    [Fact]
    public void Scenery_stands_off_the_road()
    {
        foreach (var s in All())
            foreach (var seg in s.Segments)
                foreach (var p in seg.Props.Where(p => p.Offset != 0))
                    Assert.False(Sim.OnRoad(p.Offset, seg.Split), $"{s.Look.Name}: {p.Kind} on the road at {seg.Index}");
    }

    [Fact]
    public void The_computer_driver_reaches_a_goal()
    {
        foreach (int seed in new[] { 5, 6 })
        {
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "outrun-course-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
            g.Go();
            g.Pilot.Reset();
            for (int t = 0; t < 60 * 600 && g.Phase == Phase.Drive; t++) g.Tick(g.Pilot.Decide(g));
            Assert.True(g.Phase == Phase.Goal, $"seed {seed}: stopped at stage {g.World.Level + 1} ({g.Phase})");
        }
    }
}
