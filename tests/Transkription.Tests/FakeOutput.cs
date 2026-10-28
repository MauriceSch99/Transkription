using NAudio.Wave;
using Transkription.Audio;

namespace Transkription.Tests;

/// <summary>
/// Ersetzt die Soundkarte: Was „abgespielt“ wird, wird mitgeschrieben, und die
/// gespielte Menge steuert der Test selbst.
/// </summary>
public sealed class FakeOutput : IAudioOutput
{
    private IWaveProvider? _session;

    /// <summary>Engine, deren Ausgaben in <paramref name="outputs"/> gesammelt werden.</summary>
    public static AudioEngine CreateEngine(out List<FakeOutput> outputs)
    {
        var list = new List<FakeOutput>();
        outputs = list;
        return new AudioEngine(() =>
        {
            var o = new FakeOutput();
            list.Add(o);
            return o;
        });
    }

    public bool Playing { get; private set; }
    public bool Disposed { get; private set; }
    public int Starts { get; private set; }
    public long PlayedBytes { get; set; }
    public WaveFormat Format => _session!.WaveFormat;
    public IWaveProvider? Session => _session;
    public event Action<IWaveProvider, Exception?>? PlaybackStopped;

    public void Start(IWaveProvider session)
    {
        _session = session;
        PlayedBytes = 0;
        Playing = true;
        Starts++;
    }

    public void Stop()
    {
        Playing = false;
        PlayedBytes = 0;
    }

    public void Dispose() => Disposed = true;

    /// <summary>Spielt <paramref name="seconds"/> Sekunden Ausgabe ab und liefert sie als Mono-Samples.</summary>
    public float[] Pull(double seconds)
    {
        var fmt = Format;
        var bytesWanted = (int)(seconds * fmt.SampleRate) * fmt.BlockAlign;
        var buffer = new byte[bytesWanted];
        var got = 0;
        while (got < bytesWanted)
        {
            var n = _session!.Read(buffer, got, Math.Min(4096 * fmt.BlockAlign, bytesWanted - got));
            if (n == 0)
            {
                if (Playing)
                {
                    Playing = false;
                    PlaybackStopped?.Invoke(_session, null);
                }
                break;
            }
            got += n;
        }
        PlayedBytes += got;

        var frames = got / fmt.BlockAlign;
        var mono = new float[frames];
        for (var i = 0; i < frames; i++)
            mono[i] = BitConverter.ToInt16(buffer, i * fmt.BlockAlign) / 32768f; // erster Kanal
        return mono;
    }
}

public static class Signal
{
    /// <summary>Frequenz über Nulldurchgänge (genügt für reine Sinustöne).</summary>
    public static double Frequency(ReadOnlySpan<float> s, int sampleRate)
    {
        var crossings = 0;
        for (var i = 1; i < s.Length; i++)
            if ((s[i - 1] < 0) != (s[i] < 0)) crossings++;
        return crossings / 2.0 / (s.Length / (double)sampleRate);
    }

    /// <summary>Tonhöhe der Testdateien in Sekunde k.</summary>
    public static double ToneAt(int second) => 300 + 100 * second;

    public static double Rms(ReadOnlySpan<float> s)
    {
        double sum = 0;
        foreach (var v in s) sum += v * v;
        return Math.Sqrt(sum / Math.Max(1, s.Length));
    }

    /// <summary>Zeitpunkt (s), ab dem die Tonhöhe von <paramref name="from"/> auf <paramref name="to"/> wechselt.</summary>
    public static double FindToneChange(float[] s, int sampleRate, double from, double to)
    {
        var win = sampleRate / 50; // 20 ms
        var mid = (from + to) / 2;
        for (var start = 0; start + win <= s.Length; start += win / 4)
        {
            if (Frequency(s.AsSpan(start, win), sampleRate) > mid)
                return (start + win / 2.0) / sampleRate;
        }
        return double.NaN;
    }

    public static string TestFile(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", name);
}
