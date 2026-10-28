using System.Windows.Media;
using System.Windows.Threading;
using Transkription.Settings;

namespace Transkription.Core;

/// <summary>
/// Steuert die Wiedergabe. Kümmert sich um das Verhalten, das beim Transkribieren wichtig ist:
/// Wiedergabe solange das Pedal gehalten wird, automatisches Zurückspringen beim Anhalten,
/// Spulen beim Gedrückthalten und Geschwindigkeitsänderung.
/// </summary>
public sealed class PlayerController
{
    private static readonly TimeSpan WindTick = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan WindDelay = TimeSpan.FromMilliseconds(350);

    private readonly MediaPlayer _player = new();
    private readonly DispatcherTimer _windTimer;
    private TimeSpan _pendingStart;
    private int _windDirection;
    private DateTime _windStarted;
    private bool _resumeAfterWind;
    private bool _playHeld;

    public PlayerController(AppSettings settings)
    {
        Settings = settings;
        _windTimer = new DispatcherTimer { Interval = WindTick };
        _windTimer.Tick += OnWindTick;

        _player.MediaOpened += (_, _) =>
        {
            HasMedia = true;
            if (_pendingStart > TimeSpan.Zero && _pendingStart < Duration)
                _player.Position = _pendingStart;
            ApplyVolume();
            MediaOpened?.Invoke();
            StateChanged?.Invoke();
        };
        _player.MediaFailed += (_, e) =>
        {
            HasMedia = false;
            IsPlaying = false;
            MediaFailed?.Invoke(e.ErrorException?.Message ?? "Unbekannter Fehler");
            StateChanged?.Invoke();
        };
        _player.MediaEnded += (_, _) =>
        {
            _player.Pause();
            IsPlaying = false;
            StateChanged?.Invoke();
        };
    }

    public AppSettings Settings { get; set; }

    public bool HasMedia { get; private set; }
    public bool IsPlaying { get; private set; }
    public bool IsWinding => _windDirection != 0;
    public string? CurrentPath { get; private set; }

    public TimeSpan Position => HasMedia ? _player.Position : _pendingStart;

    public TimeSpan Duration =>
        HasMedia && _player.NaturalDuration.HasTimeSpan ? _player.NaturalDuration.TimeSpan : TimeSpan.Zero;

    public event Action? StateChanged;
    public event Action? MediaOpened;
    public event Action<string>? MediaFailed;

    public void Load(string path, TimeSpan start)
    {
        Unload();
        CurrentPath = path;
        _pendingStart = start;
        _player.Open(new Uri(path, UriKind.Absolute));
        StateChanged?.Invoke();
    }

    public void Unload()
    {
        StopWind(resume: false);
        _playHeld = false;
        if (HasMedia) _player.Stop();
        _player.Close();
        HasMedia = false;
        IsPlaying = false;
        CurrentPath = null;
        _pendingStart = TimeSpan.Zero;
        StateChanged?.Invoke();
    }

    // ---------- Aktionen von Tastatur, Pedal und Schaltflächen ----------

    public void ActionDown(PlayerAction action, InputSource source)
    {
        switch (action)
        {
            case PlayerAction.Play:
                if (ModeFor(source) == PlayMode.Hold)
                {
                    _playHeld = true;
                    if (!IsWinding) Play();
                }
                else
                {
                    TogglePlay();
                }
                break;
            case PlayerAction.Stop:
                Stop();
                break;
            case PlayerAction.Rewind:
                StartWind(-1);
                break;
            case PlayerAction.FastForward:
                StartWind(+1);
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
            case PlayerAction.Play when ModeFor(source) == PlayMode.Hold:
                _playHeld = false;
                if (IsWinding) _resumeAfterWind = false;
                else Pause(backstep: true);
                break;
            case PlayerAction.Rewind when _windDirection < 0:
            case PlayerAction.FastForward when _windDirection > 0:
                StopWind(resume: true);
                break;
        }
    }

    private PlayMode ModeFor(InputSource source) => source switch
    {
        InputSource.Pedal => Settings.PedalPlayMode,
        InputSource.Keyboard => Settings.KeyboardPlayMode,
        _ => PlayMode.Toggle,
    };

    // ---------- Grundfunktionen ----------

    public void Play() => Play(restartAtEnd: true);

    private void Play(bool restartAtEnd)
    {
        if (!HasMedia || IsPlaying) return;
        if (restartAtEnd && Duration > TimeSpan.Zero && _player.Position >= Duration - TimeSpan.FromMilliseconds(100))
            _player.Position = TimeSpan.Zero;
        _player.Play();
        _player.SpeedRatio = Settings.SpeedPercent / 100.0; // muss nach Play() gesetzt werden
        IsPlaying = true;
        StateChanged?.Invoke();
    }

    public void Pause(bool backstep)
    {
        if (!IsPlaying) return;
        _player.Pause();
        IsPlaying = false;
        if (backstep && Settings.BackstepSeconds > 0)
            SeekBy(-Settings.BackstepSeconds);
        StateChanged?.Invoke();
    }

    public void TogglePlay()
    {
        if (IsWinding)
        {
            // Während des Spulens: danach weiterspielen bzw. nicht.
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
            _pendingStart = position;
            return;
        }
        if (position < TimeSpan.Zero) position = TimeSpan.Zero;
        if (Duration > TimeSpan.Zero && position > Duration) position = Duration;
        _player.Position = position;
    }

    public void SeekBy(double seconds) => Seek(_player.Position + TimeSpan.FromSeconds(seconds));

    public void SetSpeed(int percent)
    {
        Settings.SpeedPercent = Math.Clamp(percent, 30, 300);
        if (IsPlaying) _player.SpeedRatio = Settings.SpeedPercent / 100.0;
        StateChanged?.Invoke();
    }

    public void SetVolume(double volume)
    {
        Settings.Volume = Math.Clamp(volume, 0, 1);
        ApplyVolume();
    }

    private void ApplyVolume() => _player.Volume = Settings.Volume;

    // ---------- Spulen ----------

    /// <summary>
    /// Kurzes Antippen springt um <see cref="AppSettings.SkipSeconds"/>; wird länger gehalten,
    /// wird mit <see cref="AppSettings.WindSpeed"/>-facher Geschwindigkeit gespult.
    /// </summary>
    public void StartWind(int direction)
    {
        if (!HasMedia || IsWinding) return;
        _resumeAfterWind = IsPlaying || _playHeld;
        if (IsPlaying)
        {
            _player.Pause();
            IsPlaying = false;
        }
        _windDirection = direction;
        _windStarted = DateTime.UtcNow;
        SeekBy(direction * Settings.SkipSeconds);
        _windTimer.Start();
        StateChanged?.Invoke();
    }

    public void StopWind(bool resume)
    {
        if (!IsWinding) return;
        _windTimer.Stop();
        _windDirection = 0;
        if (resume && (_resumeAfterWind || _playHeld)) Play(restartAtEnd: false);
        StateChanged?.Invoke();
    }

    private void OnWindTick(object? sender, EventArgs e)
    {
        if (!IsWinding || DateTime.UtcNow - _windStarted < WindDelay) return;
        SeekBy(_windDirection * Settings.WindSpeed * WindTick.TotalSeconds);
    }
}
