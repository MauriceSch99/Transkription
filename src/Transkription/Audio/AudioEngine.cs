using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Transkription.Audio;

/// <summary>
/// Spielt eine geöffnete Audiodatei ab. Beim Anhalten wird der Ausgabepuffer verworfen, damit
/// nichts nachklingt und die angezeigte Position genau dem Gehörten entspricht.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private readonly Func<IAudioOutput> _outputFactory;
    private readonly object _sync = new();
    private WaveStream? _reader;
    private ISampleProvider? _samples;
    private IAudioOutput? _output;
    private PlaybackProvider? _provider;
    private IWaveProvider? _session;
    private TimeSpan _position;
    private TimeSpan _playStart;
    private double _playTempo = 1;
    private double _tempo = 1;
    private float _volume = 1;

    public AudioEngine(Func<IAudioOutput> outputFactory) => _outputFactory = outputFactory;

    public bool IsOpen => _reader != null;
    public bool IsPlaying => _provider != null;
    public TimeSpan Duration { get; private set; }

    /// <summary>Wiedergabe hat das Dateiende erreicht.</summary>
    public event Action? Ended;

    /// <summary>Wiedergabe wurde durch einen Fehler der Ausgabe abgebrochen.</summary>
    public event Action<Exception>? Failed;

    public TimeSpan Position
    {
        get
        {
            if (_output == null || _provider == null) return _position;
            var bytesPerFrame = 2 * _provider.WaveFormat.Channels; // 16-Bit-Ausgabe
            var frames = _output.PlayedBytes / bytesPerFrame;
            var pos = _playStart + TimeSpan.FromSeconds(frames * _playTempo / _provider.WaveFormat.SampleRate);
            return pos > Duration ? Duration : pos;
        }
    }

    /// <summary>Wiedergabegeschwindigkeit (1.0 = normal).</summary>
    public double Tempo
    {
        get => _tempo;
        set
        {
            if (Math.Abs(value - _tempo) < 0.0001) return;
            _tempo = value;
            if (IsPlaying) Restart();
        }
    }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_provider != null) _provider.Volume = _volume;
        }
    }

    /// <summary>Übernimmt einen (z. B. im Hintergrund) mit <see cref="AudioReaders.Open"/> geöffneten Leser.</summary>
    public void Attach(WaveStream reader)
    {
        Close();
        lock (_sync)
        {
            _reader = reader;
            _samples = reader.ToSampleProvider();
        }
        Duration = reader.TotalTime;
        _position = TimeSpan.Zero;
    }

    public void Close()
    {
        StopOutput();
        _output?.Dispose();
        _output = null;
        lock (_sync)
        {
            _reader?.Dispose();
            _reader = null;
            _samples = null;
        }
        Duration = TimeSpan.Zero;
        _position = TimeSpan.Zero;
    }

    public void Play()
    {
        if (_reader == null || _samples == null || IsPlaying) return;
        if (_position >= Duration) return;

        lock (_sync) PositionReader(_position);
        _playStart = _position;
        _playTempo = _tempo;
        _provider = new PlaybackProvider(_samples, _sync, _tempo, _volume);

        var session = new SampleToWaveProvider16(_provider);
        try
        {
            if (_output == null)
            {
                _output = _outputFactory();
                _output.PlaybackStopped += OnPlaybackStopped;
            }
            _session = session;
            _output.Start(session);
        }
        catch (Exception ex)
        {
            // Z. B. kein Lautsprecher/Kopfhörer angeschlossen oder Gerät von anderem Programm belegt.
            _provider.Deactivate();
            _provider = null;
            _session = null;
            _output?.Dispose();
            _output = null;
            Failed?.Invoke(ex);
        }
    }

    public void Pause()
    {
        if (!IsPlaying) return;
        _position = Position;
        StopOutput();
    }

    public void Seek(TimeSpan position)
    {
        if (position < TimeSpan.Zero) position = TimeSpan.Zero;
        if (position > Duration) position = Duration;
        if (IsPlaying)
        {
            StopOutput();
            _position = position;
            Play();
        }
        else
        {
            _position = position;
        }
    }

    /// <summary>
    /// MP3-Frames verweisen auf Daten vorheriger Frames („Bit-Reservoir“). Wird direkt an die
    /// Zielstelle gesprungen, klingen die ersten Zehntelsekunden verzerrt – besonders bei
    /// Diktaten mit niedriger oder variabler Bitrate. Deshalb etwas früher beginnen und den
    /// Vorlauf unhörbar verwerfen.
    /// </summary>
    private void PositionReader(TimeSpan target)
    {
        var reader = _reader!;
        var targetBytes = AlignedBytes(reader, target);
        var start = target - Preroll;
        // Nicht CurrentTime verwenden: Das rundet auf eine Byte-Position mitten in einem Sample,
        // danach wird alles versetzt gelesen und es kommt lautes Rauschen heraus.
        reader.Position = AlignedBytes(reader, start > TimeSpan.Zero ? start : TimeSpan.Zero);

        var block = reader.WaveFormat.BlockAlign;
        var skip = targetBytes - reader.Position;
        var buffer = new byte[16384 - 16384 % block];
        while (skip > 0)
        {
            var n = reader.Read(buffer, 0, (int)Math.Min(buffer.Length, skip));
            if (n <= 0) break;
            skip -= n;
        }
    }

    private static readonly TimeSpan Preroll = TimeSpan.FromSeconds(1);

    private static long AlignedBytes(WaveStream reader, TimeSpan time)
    {
        var fmt = reader.WaveFormat;
        var frames = (long)Math.Round(time.TotalSeconds * fmt.SampleRate);
        return Math.Min(frames * fmt.BlockAlign, reader.Length - reader.Length % fmt.BlockAlign);
    }

    private void Restart()
    {
        _position = Position;
        StopOutput();
        Play();
    }

    private void StopOutput()
    {
        _provider?.Deactivate();
        _provider = null;
        _session = null;
        _output?.Stop();
    }

    private void OnPlaybackStopped(IWaveProvider session, Exception? error)
    {
        // Meldungen einer bereits beendeten Wiedergabe ignorieren.
        if (!ReferenceEquals(session, _session) || _provider == null) return;

        if (error != null)
        {
            _position = Position;
            StopOutput();
            Failed?.Invoke(error);
        }
        else if (_provider.Ended)
        {
            StopOutput();
            _position = Duration;
            Ended?.Invoke();
        }
    }

    public void Dispose() => Close();
}
