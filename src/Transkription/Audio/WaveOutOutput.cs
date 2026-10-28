using NAudio.Wave;

namespace Transkription.Audio;

/// <summary>
/// Ausgabe über das Standard-Wiedergabegerät von Windows. Das Gerät bleibt geöffnet und wird
/// für jede Wiedergabe nur angehalten und wieder gestartet; ständiges Neu-Öffnen ist bei manchen
/// Treibern unzuverlässig.
/// </summary>
public sealed class WaveOutOutput : IAudioOutput
{
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;
    private readonly ManualResetEventSlim _threadFinished = new(true);
    private readonly SwitchableProvider _source = new();
    private WaveOutEvent? _device;
    private WaveFormat? _deviceFormat;
    private IWaveProvider? _session;
    private long _startBytes;

    public event Action<IWaveProvider, Exception?>? PlaybackStopped;

    // Der Zähler des Geräts wird nur durch Stop() zurückgesetzt, nicht wenn eine Wiedergabe von
    // selbst endet. Deshalb den Stand beim Start abziehen.
    public long PlayedBytes => _device == null ? 0 : Math.Max(0, _device.GetPosition() - _startBytes);

    public void Start(IWaveProvider session)
    {
        Stop();
        if (_device == null || !_deviceFormat!.Equals(session.WaveFormat))
        {
            _device?.Dispose();
            _device = null;
            _source.Format = session.WaveFormat;
            _device = CreateDevice();
            _device.Init(_source);
            _deviceFormat = session.WaveFormat;
        }
        _session = session;
        _source.Inner = session;
        _startBytes = _device.GetPosition();
        _threadFinished.Reset();
        _device.Play();
    }

    public void Stop()
    {
        if (_device == null) return;
        if (_device.PlaybackState != PlaybackState.Stopped) _device.Stop();
        // NAudio beendet seinen Ausgabe-Thread erst kurz nach Stop(). Würde sofort neu gestartet,
        // liefen kurzzeitig zwei Threads und die neue Wiedergabe könnte abbrechen.
        _threadFinished.Wait(TimeSpan.FromSeconds(1));
        _source.Inner = null;
    }

    private WaveOutEvent CreateDevice()
    {
        // Ohne SynchronizationContext erzeugen: Dann meldet NAudio das Ende direkt aus dem
        // Ausgabe-Thread, und Stop() kann darauf warten, ohne den UI-Thread zu blockieren.
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            var device = new WaveOutEvent { DesiredLatency = 200, NumberOfBuffers = 3 };
            device.PlaybackStopped += OnDeviceStopped;
            return device;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private void OnDeviceStopped(object? sender, StoppedEventArgs e)
    {
        var session = _session;
        _threadFinished.Set();
        if (session == null) return;
        void Raise() => PlaybackStopped?.Invoke(session, e.Exception);
        if (_uiContext != null) _uiContext.Post(_ => Raise(), null);
        else Raise();
    }

    public void Dispose()
    {
        Stop();
        _device?.Dispose();
        _device = null;
        _threadFinished.Dispose();
    }

    /// <summary>Quelle, die ausgetauscht werden kann, ohne das Gerät neu zu öffnen.</summary>
    private sealed class SwitchableProvider : IWaveProvider
    {
        private volatile IWaveProvider? _inner;

        public WaveFormat Format { get; set; } = new();
        public WaveFormat WaveFormat => Format;

        public IWaveProvider? Inner
        {
            get => _inner;
            set => _inner = value;
        }

        public int Read(byte[] buffer, int offset, int count) => _inner?.Read(buffer, offset, count) ?? 0;
    }
}
