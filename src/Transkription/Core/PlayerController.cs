using NAudio.Wave;
using Transkription.Audio;
using Transkription.Settings;

namespace Transkription.Core;

/// <summary>
/// Steuert die Wiedergabe. Kümmert sich um das Verhalten, das beim Transkribieren wichtig ist:
/// Wiedergabe solange das Pedal gehalten wird, automatisches Zurückspringen beim Anhalten,
/// Spulen beim Gedrückthalten und Geschwindigkeitsänderung.
/// <see cref="Tick"/> muss regelmäßig (etwa alle 50 ms) aufgerufen werden.
/// </summary>
public sealed class PlayerController : IDisposable
{
    private readonly AudioEngine _engine;
    private readonly Func<string, WaveStream> _openReader;
    private readonly Func<DateTime> _now;
    private TimeSpan _pendingStart;
    private int _loadId;
    private int _windDirection;
    private DateTime _lastWindTick;
    private bool _resumeAfterWind;
    private bool _playHeld;

    public PlayerController(AppSettings settings, AudioEngine engine,
        Func<string, WaveStream>? openReader = null, Func<DateTime>? now = null)
    {
        Settings = settings;
        _engine = engine;
        _openReader = openReader ?? AudioReaders.Open;
        _now = now ?? (() => DateTime.UtcNow);
        _engine.Ended += () => StateChanged?.Invoke();
        _engine.Failed += ex =>
        {
            PlaybackFailed?.Invoke(ex.Message);
            StateChanged?.Invoke();
        };
    }

    public AppSettings Settings { get; set; }

    public bool HasMedia => _engine.IsOpen;
    public bool IsLoading { get; private set; }
    public bool IsPlaying => _engine.IsPlaying;
    public bool IsWinding => _windDirection != 0;
    public string? CurrentPath { get; private set; }

    public TimeSpan Position => HasMedia ? _engine.Position : _pendingStart;
    public TimeSpan Duration => _engine.Duration;

    public event Action? StateChanged;
    public event Action? MediaOpened;
    public event Action<string>? MediaFailed;

    /// <summary>Die Soundausgabe ist nicht möglich (kein Gerät, Gerät abgezogen …).</summary>
    public event Action<string>? PlaybackFailed;

    /// <summary>Öffnet eine Datei im Hintergrund (große MP3-Dateien müssen erst eingelesen werden).</summary>
    public async Task LoadAsync(string path, TimeSpan start)
    {
        Unload();
        var id = ++_loadId;
        CurrentPath = path;
        _pendingStart = start;
        IsLoading = true;
        StateChanged?.Invoke();

        WaveStream reader;
        try
        {
            reader = await Task.Run(() => _openReader(path));
        }
        catch (Exception ex)
        {
            if (id != _loadId) return;
            IsLoading = false;
            CurrentPath = null;
            MediaFailed?.Invoke(ex.Message);
            StateChanged?.Invoke();
            return;
        }

        if (id != _loadId)
        {
            // Inzwischen wurde eine andere Datei gewählt.
            reader.Dispose();
            return;
        }

        IsLoading = false;
        _engine.Attach(reader);
        _engine.Volume = (float)Settings.Volume;
        _engine.Tempo = Settings.SpeedPercent / 100.0;
        if (start > TimeSpan.Zero && start < _engine.Duration) _engine.Seek(start);
        MediaOpened?.Invoke();
        StateChanged?.Invoke();
    }

    public void Unload()
    {
        _loadId++;
        StopWind(resume: false);
        _playHeld = false;
        _engine.Close();
        IsLoading = false;
        CurrentPath = null;
        _pendingStart = TimeSpan.Zero;
        StateChanged?.Invoke();
    }

    // ---------- Aktionen von Tastatur, Pedal und Schaltflächen ----------

    public void ActionDown(PlayerAction action, InputSource source)
    {
        switch (action)
        {
            case PlayerAction.PlayHold:
                _playHeld = true;
                if (!IsWinding) Play();
                break;
            case PlayerAction.PlayToggle:
                TogglePlay();
                break;
            case PlayerAction.Stop:
                Stop();
                break;
            case PlayerAction.RewindHold:
                StartWind(-1);
                break;
            case PlayerAction.ForwardHold:
                StartWind(+1);
                break;
            case PlayerAction.JumpBack:
                SeekBy(-Settings.SkipSeconds);
                break;
            case PlayerAction.JumpForward:
                SeekBy(Settings.SkipSeconds);
                break;
            case PlayerAction.SpeedDown:
                SetSpeed(Settings.SpeedPercent - Settings.SpeedStepPercent);
                break;
            case PlayerAction.SpeedUp:
                SetSpeed(Settings.SpeedPercent + Settings.SpeedStepPercent);
                break;
            case PlayerAction.SpeedNormal:
                SetSpeed(100);
                break;
            case PlayerAction.JumpToStart:
                Seek(TimeSpan.Zero);
                break;
        }
    }

    public void ActionUp(PlayerAction action, InputSource source)
    {
        switch (action)
        {
            case PlayerAction.PlayHold:
                _playHeld = false;
                if (IsWinding) _resumeAfterWind = false;
                else Pause(backstep: true);
                break;
            case PlayerAction.RewindHold when _windDirection < 0:
            case PlayerAction.ForwardHold when _windDirection > 0:
                StopWind(resume: true);
                break;
        }
    }

    // ---------- Grundfunktionen ----------

    public void Play() => Play(restartAtEnd: true);

    private void Play(bool restartAtEnd)
    {
        if (!HasMedia || IsPlaying) return;
        if (_engine.Position >= Duration - TimeSpan.FromMilliseconds(100))
        {
            if (!restartAtEnd) return;
            _engine.Seek(TimeSpan.Zero);
        }
        _engine.Play();
        StateChanged?.Invoke();
    }

    public void Pause(bool backstep)
    {
        if (!IsPlaying) return;
        _engine.Pause();
        if (backstep && Settings.BackstepSeconds > 0)
            SeekBy(-Settings.BackstepSeconds);
        StateChanged?.Invoke();
    }

    public void TogglePlay()
    {
        if (IsWinding)
        {
            // Während des Spulens: festlegen, ob es danach weitergeht.
            _resumeAfterWind = !_resumeAfterWind;
            return;
        }
        if (IsPlaying) Pause(backstep: true);
        else Play();
    }

    public void Stop()
    {
        _playHeld = false;
        StopWind(resume: false);
        Pause(backstep: true);
    }

    public void Seek(TimeSpan position)
    {
        if (!HasMedia)
        {
            _pendingStart = position < TimeSpan.Zero ? TimeSpan.Zero : position;
            return;
        }
        _engine.Seek(position);
    }

    public void SeekBy(double seconds) => Seek(Position + TimeSpan.FromSeconds(seconds));

    public void SetSpeed(int percent)
    {
        Settings.SpeedPercent = Math.Clamp(percent, 30, 300);
        _engine.Tempo = Settings.SpeedPercent / 100.0;
        StateChanged?.Invoke();
    }

    public void SetVolume(double volume)
    {
        Settings.Volume = Math.Clamp(volume, 0, 1);
        _engine.Volume = (float)Settings.Volume;
    }

    // ---------- Spulen ----------

    /// <summary>
    /// Spult mit <see cref="AppSettings.WindSpeed"/>-facher Geschwindigkeit, bis <see cref="StopWind"/>
    /// aufgerufen wird. Lief die Wiedergabe vorher, geht sie danach weiter.
    /// </summary>
    public void StartWind(int direction)
    {
        if (!HasMedia || IsWinding) return;
        _resumeAfterWind = IsPlaying || _playHeld;
        _engine.Pause();
        _windDirection = direction;
        _lastWindTick = _now();
        StateChanged?.Invoke();
    }

    public void StopWind(bool resume)
    {
        if (!IsWinding) return;
        _windDirection = 0;
        if (resume && (_resumeAfterWind || _playHeld)) Play(restartAtEnd: false);
        StateChanged?.Invoke();
    }

    public void Tick()
    {
        if (!IsWinding) return;
        var now = _now();
        var elapsed = now - _lastWindTick;
        _lastWindTick = now;
        SeekBy(_windDirection * Settings.WindSpeed * elapsed.TotalSeconds);
    }

    public void Dispose() => _engine.Dispose();
}
