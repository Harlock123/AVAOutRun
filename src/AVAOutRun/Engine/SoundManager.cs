using AVAOutRun.Audio;

namespace AVAOutRun.Engine;

/// <summary>
/// Plays the game's sounds through OpenAL Soft, which mixes as many at once as the game needs. The
/// tunes play on the music channel. In a virtual machine a larger mixing buffer keeps the sound from breaking up.
/// </summary>
public sealed class SoundManager : IDisposable
{
    private readonly IAudioEngine _engine;
    private readonly string _folder;

    public SoundManager(bool silent = false)
    {
        _folder = Path.Combine(AppContext.BaseDirectory, "Sounds");
        _engine = silent
            ? new NullAudioEngine("silent")
            : OpenAlAudioEngine.CreateOrSilent(AudioBuffer.PeriodFrames(AudioBuffer.Automatic, VirtualMachine.Detect()));
        _engine.SetVolumes(0.9f, 0.8f, 0.7f);
        _engine.SetAmbienceVolume(0.35f);
    }

    public bool IsAvailable => _engine.IsAvailable;

    public bool IsMuted
    {
        get => _muted;
        set
        {
            _muted = value;
            if (value) StopAll();
        }
    }
    private bool _muted;

    private string File(Sfx s) => Path.Combine(_folder, s + ".wav");

    public void Play(Sfx sfx)
    {
        if (_muted) return;
        switch (sfx)
        {
            case Sfx.TitleTune or Sfx.Station0 or Sfx.Station1 or Sfx.Station2: _engine.PlayMusic(File(sfx), loop: true); return;
            case Sfx.GameOver or Sfx.Victory: _engine.PlayMusic(File(sfx), loop: false); return;
            case Sfx.StationStop: _engine.StopMusic(); return;
            // The engine's note runs on the ambience channel, a loop for each band of revs.
            case Sfx.Engine0 or Sfx.Engine1 or Sfx.Engine2 or Sfx.Engine3: _engine.StopAmbience(); _engine.PlayAmbience(File(sfx)); return;
            case Sfx.EngineOff: _engine.StopAmbience(); return;
        }
        _engine.PlayEffect(File(sfx), sfx switch { Sfx.Tick => 0.6f, Sfx.Gear => 0.6f, _ => 1f });
    }

    public void StopAll()
    {
        _engine.StopAmbience();
        _engine.StopMusic();
    }

    public void Dispose() => _engine.Dispose();
}
