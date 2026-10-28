using NAudio.Wave;
using SoundTouch;

namespace Transkription.Audio;

/// <summary>
/// Liefert die Samples für eine Wiedergabe-Sitzung (von Play bis Pause). Bei einer anderen
/// Geschwindigkeit als 100 % wird das Tempo mit SoundTouch geändert, ohne die Tonhöhe zu
/// verändern (keine „Mickymaus-Stimme“).
/// </summary>
internal sealed class PlaybackProvider : ISampleProvider
{
    private const int InputFrames = 2048;

    private readonly ISampleProvider _source;
    private readonly object _sync;
    private readonly SoundTouchProcessor? _tempo;
    private readonly float[] _input;
    private readonly int _channels;
    private bool _active = true;
    private bool _sourceDone;

    public PlaybackProvider(ISampleProvider source, object sync, double tempo, float volume)
    {
        _source = source;
        _sync = sync;
        _channels = source.WaveFormat.Channels;
        Volume = volume;
        _input = new float[InputFrames * _channels];

        if (Math.Abs(tempo - 1.0) > 0.001)
        {
            _tempo = new SoundTouchProcessor
            {
                SampleRate = source.WaveFormat.SampleRate,
                Channels = _channels,
                Tempo = tempo,
            };
            // Empfohlene Werte für Sprache (siehe SoundTouch-Dokumentation).
            _tempo.SetSetting(SettingId.SequenceDurationMs, 40);
            _tempo.SetSetting(SettingId.SeekWindowDurationMs, 15);
            _tempo.SetSetting(SettingId.OverlapDurationMs, 8);
        }
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public volatile float Volume;

    /// <summary>Quelle vollständig ausgegeben.</summary>
    public bool Ended { get; private set; }

    /// <summary>Nach dem Anhalten keine Daten mehr lesen (der Ausgabe-Thread kann noch kurz nachlaufen).</summary>
    public void Deactivate()
    {
        lock (_sync) _active = false;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (_sync)
        {
            if (!_active || Ended) return 0;
            count -= count % _channels;

            int read;
            if (_tempo == null)
            {
                read = ReadSource(buffer, offset, count);
            }
            else
            {
                var frames = count / _channels;
                while (_tempo.AvailableSamples < frames && !_sourceDone)
                {
                    var n = ReadSource(_input, 0, _input.Length);
                    if (n == 0)
                    {
                        _sourceDone = true;
                        _tempo.Flush();
                    }
                    else
                    {
                        _tempo.PutSamples(_input.AsSpan(0, n), n / _channels);
                    }
                }
                read = _tempo.ReceiveSamples(buffer.AsSpan(offset, count), frames) * _channels;
            }

            if (read == 0)
            {
                Ended = true;
                return 0;
            }

            var v = Volume;
            if (v != 1f)
                for (var i = offset; i < offset + read; i++) buffer[i] *= v;
            return read;
        }
    }

    private int ReadSource(float[] buffer, int offset, int count)
    {
        // Manche Leser liefern weniger als angefordert; bis zum Dateiende auffüllen.
        var total = 0;
        while (total < count)
        {
            var n = _source.Read(buffer, offset + total, count - total);
            if (n <= 0) break;
            total += n;
        }
        total -= total % _channels;
        if (total == 0) _sourceDone = true;
        return total;
    }
}
