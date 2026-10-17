namespace AVAOutRun.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

public enum Phase
{
    /// <summary>Choosing a station on the car radio.</summary>
    Radio,
    /// <summary>The lights at the start: red, red, red, green.</summary>
    Countdown,
    Drive,
    /// <summary>Out of time: the car rolls to a stop.</summary>
    TimeUp,
    /// <summary>Over the line at a goal: the time left counted into the score.</summary>
    Goal,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Engine0, Engine1, Engine2, Engine3, EngineOff, Gear, Bump, Crash, Skid, Checkpoint, Beep, StartBeep, GoBeep, Tick, ExtraTime,
    SayReady, SayExtended, SayGoal,
    Station0, Station1, Station2, StationStop, TitleTune, GameOver, Victory,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 16;
    public const double AttractSeconds = 60;
    public const double HighScoreSeconds = 9;
    public const double RadioSeconds = 8, CountdownSeconds = 3.2, TimeUpSeconds = 5, GoalSeconds = 9;
    public const int TimeBonus = 10000;
    public static readonly string[] Stations = ["COASTLINE CRUISE", "SILVER BREEZE", "NIGHT HIGHWAY"];

    private double _accumulator, _idle;
    private bool _startPrev, _tunePending = true, _actPrev, _firePrev;
    private int _dxPrev, _band = -1;
    private readonly int _seed;
    private int _games;

    public GameEngine(HighScoreTable? table = null, int seed = 1)
    {
        HighScores = table ?? HighScoreTable.Load();
        _seed = seed;
        NewGame();
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Radio;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    public int Score => World.Score;
    /// <summary>The station chosen.</summary>
    public int Station { get; private set; }
    public string? Message { get; private set; }
    public string? SubMessage { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    private void Say(string text, string? sub = null, double seconds = 2.5)
    {
        Message = text;
        SubMessage = sub;
        MessageTime = seconds;
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = SubMessage = null; }
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed || c.Fire && StateTime > 0.5) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this), false);
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) { Quiet(); ShowHighScores(-1); }
                break;
            case GameState.Playing:
                Simulate(dt, c, startPressed);
                break;
            case GameState.GameOver:
                if (StateTime > 4)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, World.Level + 1);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    private void NewGame()
    {
        World = new World();
        Sim.Start(World, _seed * 1000 + _games++);
        SetPhase(Phase.Radio);
        _band = -1;
    }

    public void StartGame()
    {
        NewGame();
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
        Play(Sfx.StationStop);
        Play(Station switch { 0 => Sfx.Station0, 1 => Sfx.Station1, _ => Sfx.Station2 });
    }

    private void StartAttract()
    {
        NewGame();
        State = GameState.Attract;
        StateTime = 0;
        Pilot.Reset();
        SetPhase(Phase.Countdown);
        PhaseTime = CountdownSeconds - 0.5;
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Quiet();
        Message = SubMessage = null;
        NewGame();
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Play(Sfx.EngineOff);
        _band = -1;
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
    }

    private void Quiet()
    {
        Play(Sfx.EngineOff);
        Play(Sfx.StationStop);
        _band = -1;
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    /// <summary>The engine note: idle, low, middling or high revs (by speed and gear).</summary>
    private void EngineNote(bool on)
    {
        var p = World.Player;
        int band = !on ? -1 : p.Crash != Crash.None ? 0 : (p.High ? (p.Kmh - 80) / 213.0 : p.Kmh / 190.0) switch { < 0.15 => 0, < 0.45 => 1, < 0.75 => 2, _ => 3 };
        if (band == _band) return;
        _band = band;
        Play(band switch { -1 => Sfx.EngineOff, 0 => Sfx.Engine0, 1 => Sfx.Engine1, 2 => Sfx.Engine2, _ => Sfx.Engine3 });
    }

    private void Simulate(double dt, Controls c, bool startPressed)
    {
        PhaseTime += dt;
        var w = World;
        int dxEdge = c.Dx != _dxPrev ? c.Dx : 0;
        bool fire = c.Fire && !_firePrev;
        _dxPrev = c.Dx;
        _firePrev = c.Fire;
        _actPrev = c.Act;
        switch (Phase)
        {
            case Phase.Radio:
                if (dxEdge != 0)
                {
                    Station = (Station + dxEdge + Stations.Length) % Stations.Length;
                    Play(Sfx.StationStop);
                    Play(Station switch { 0 => Sfx.Station0, 1 => Sfx.Station1, _ => Sfx.Station2 });
                }
                if (fire || startPressed && PhaseTime > 0.3 || PhaseTime > RadioSeconds)
                {
                    SetPhase(Phase.Countdown);
                    Play(Sfx.SayReady);
                }
                break;
            case Phase.Countdown:
            {
                EngineNote(true);
                int before = (int)((PhaseTime - dt) / 0.8), now = (int)(PhaseTime / 0.8);
                if (now != before && now < 4) Play(now < 3 ? Sfx.Beep : Sfx.GoBeep);
                if (PhaseTime > CountdownSeconds) SetPhase(Phase.Drive);
                break;
            }
            case Phase.Drive:
            {
                int level = w.Level;
                double before = w.TimeLeft;
                Sim.Step(w, c);
                EngineNote(true);
                if (w.Extended > 0) Say("CHECKPOINT", $"EXTENDED PLAY  +{w.Extended:0} SECONDS  -  {w.Stage.Look.Name}", 3);
                if (Math.Ceiling(w.TimeLeft) < Math.Ceiling(before) && w.TimeLeft < 5.5) Play(Sfx.Tick);
                if (w.Finished)
                {
                    SetPhase(Phase.Goal);
                    Play(Sfx.Victory);
                    Play(Sfx.SayGoal);
                    Say("GOAL!", w.Stage.Look.Name, GoalSeconds);
                }
                else if (w.TimeLeft <= 0)
                {
                    w.TimeLeft = 0;
                    SetPhase(Phase.TimeUp);
                    Say("TIME UP", null, TimeUpSeconds);
                }
                break;
            }
            case Phase.TimeUp:
                Sim.Step(w, default, drive: false);
                EngineNote(w.Player.Speed > 1);
                if (PhaseTime > TimeUpSeconds) GameOver();
                break;
            case Phase.Goal:
                Sim.Step(w, default, drive: false);
                EngineNote(w.Player.Speed > 1);
                // The time left, counted in.
                if (w.TimeLeft > 0 && PhaseTime > 1.5)
                {
                    double take = Math.Min(w.TimeLeft, dt * 12);
                    w.TimeLeft -= take;
                    w.Score += (int)(take * TimeBonus);
                    if ((int)(PhaseTime * 20) % 2 == 0) Play(Sfx.Tick);
                }
                if (PhaseTime > GoalSeconds && w.TimeLeft <= 0) GameOver();
                break;
        }
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    private void GameOver()
    {
        Quiet();
        Play(Sfx.GameOver);
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        State = GameState.GameOver;
        StateTime = 0;
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into the drive, past the radio and the lights.</summary>
    public void Go()
    {
        NewGame();
        State = GameState.Playing;
        SetPhase(Phase.Drive);
        Message = null;
    }
}
