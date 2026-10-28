using Transkription.Audio;
using Xunit;
using Xunit.Abstractions;

namespace Transkription.Tests;

public class AudioEngineTests
{
    private readonly ITestOutputHelper _log;

    public AudioEngineTests(ITestOutputHelper log) => _log = log;

    public static IEnumerable<object[]> Files => new[]
    {
        new object[] { "tones-44k-stereo-cbr.mp3" },
        new object[] { "tones-22k-mono-vbr.mp3" },
        new object[] { "tones-16k-mono.mp3" },
        new object[] { "tones-8k-mono.mp3" },
    };

    private static (AudioEngine Engine, Func<FakeOutput> Output) Open(string file)
    {
        var engine = FakeOutput.CreateEngine(out var outputs);
        engine.Attach(AudioReaders.Open(Signal.TestFile(file)));
        return (engine, () => outputs[^1]);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Mp3_wird_geoeffnet_und_Dauer_stimmt(string file)
    {
        var (engine, _) = Open(file);
        _log.WriteLine($"{file}: {engine.Duration}");
        Assert.InRange(engine.Duration.TotalSeconds, 19.9, 20.3);
        Assert.False(engine.IsPlaying);
        Assert.Equal(TimeSpan.Zero, engine.Position);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Wiedergabe_liefert_den_richtigen_Inhalt(string file)
    {
        var (engine, output) = Open(file);
        engine.Play();
        Assert.True(engine.IsPlaying);
        var o = output();
        var sr = o.Format.SampleRate;
        var samples = o.Pull(3.0);

        // Sekunde 0 und 2 jeweils in der Mitte messen.
        // Bei 8 kHz / 16 kbit/s verfälscht die starke Kompression die Messung etwas.
        Assert.InRange(Signal.Frequency(samples.AsSpan(sr / 4, sr / 2), sr), 285, 320);
        Assert.InRange(Signal.Frequency(samples.AsSpan(2 * sr + sr / 4, sr / 2), sr), 485, 515);
        Assert.InRange(engine.Position.TotalSeconds, 2.99, 3.01);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Springen_landet_an_der_richtigen_Stelle(string file)
    {
        var (engine, output) = Open(file);
        engine.Play();
        var first = output().Pull(1.0);
        var rmsNormal = Signal.Rms(first.AsSpan(first.Length / 2));
        engine.Pause();
        // Auch „krumme“ Ziele, die nicht auf ganze Samples fallen (früher lautes Rauschen).
        // Die Ziele liegen weit genug vor dem nächsten Tonwechsel, damit die Messung eindeutig ist.
        foreach (var target in new[] { 12.5, 3.5, 17.4, 0.5, 9.5, 9.37, 3.333, 14.4001, 6.423456 })
        {
            engine.Seek(TimeSpan.FromSeconds(target));
            engine.Play();
            var o = output();
            var sr = o.Format.SampleRate;
            var s = o.Pull(0.3);
            // Gleich ab dem ersten Moment muss der richtige, saubere Ton kommen (kein Knistern).
            var f = Signal.Frequency(s.AsSpan(0, sr / 5), sr);
            var rms = Signal.Rms(s.AsSpan(0, sr / 20));
            _log.WriteLine($"{file}: Sprung auf {target}s -> {f:0} Hz, Pegel {rms:0.00}");
            Assert.InRange(f, Signal.ToneAt((int)target) - 15, Signal.ToneAt((int)target) + 15);
            Assert.InRange(rms, rmsNormal * 0.85, rmsNormal * 1.15);
            engine.Pause();
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Springen_ist_auf_wenige_Millisekunden_genau(string file)
    {
        // Kurz vor einem Tonwechsel einsteigen und messen, wann der Wechsel in der Ausgabe kommt.
        var (engine, output) = Open(file);
        var delays = new List<double>();
        foreach (var boundary in new[] { 5, 11, 16 })
        {
            engine.Seek(TimeSpan.FromSeconds(boundary - 0.3));
            engine.Play();
            var o = output();
            var sr = o.Format.SampleRate;
            var s = o.Pull(0.6);
            var change = Signal.FindToneChange(s, sr, Signal.ToneAt(boundary - 1), Signal.ToneAt(boundary));
            _log.WriteLine($"{file}: Wechsel bei {boundary}s erwartet nach 0,300 s, gemessen {change:0.000} s");
            // MP3 hat eine kleine, feste Encoder-Verzögerung (1–2 Frames, bei 8 kHz am größten);
            // entscheidend ist, dass sie bei jedem Sprung gleich ist.
            Assert.InRange(change, 0.29, 0.53);
            delays.Add(change);
            engine.Pause();
        }
        Assert.True(delays.Max() - delays.Min() < 0.015, "Sprünge sind unterschiedlich genau: " + string.Join(", ", delays));
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(1.5)]
    public void Tempo_aendert_Geschwindigkeit_aber_nicht_die_Tonhoehe(double tempo)
    {
        var (engine, output) = Open("tones-44k-stereo-cbr.mp3");
        engine.Tempo = tempo;
        engine.Seek(TimeSpan.FromSeconds(4.0));
        engine.Play();
        var o = output();
        var sr = o.Format.SampleRate;

        // 2 Sekunden Quelle (Sek. 4 und 5) dauern bei Tempo t genau 2/t Sekunden.
        var outSeconds = 2.0 / tempo;
        var s = o.Pull(outSeconds);
        var mid1 = (int)(0.5 / tempo * sr);
        var f1 = Signal.Frequency(s.AsSpan(mid1 - sr / 10, sr / 5), sr);
        Assert.InRange(f1, 690, 710); // Tonhöhe bleibt (keine Mickymaus-Stimme)

        var change = Signal.FindToneChange(s, sr, 700, 800);
        _log.WriteLine($"Tempo {tempo}: Wechsel 700→800 Hz erwartet bei {1 / tempo:0.000} s, gemessen {change:0.000} s, Position {engine.Position}");
        Assert.InRange(change, 1 / tempo - 0.12, 1 / tempo + 0.12);

        // Angezeigte Position entspricht der Stelle in der Datei.
        Assert.InRange(engine.Position.TotalSeconds, 5.98, 6.02);
    }

    [Fact]
    public void Pause_merkt_sich_die_gehoerte_Position()
    {
        var (engine, output) = Open("tones-44k-stereo-cbr.mp3");
        engine.Seek(TimeSpan.FromSeconds(7));
        engine.Play();
        var o = output();
        o.Pull(1.0);
        // Die Ausgabe hat mehr gepuffert, als schon zu hören war: nur 0,4 s davon sind gespielt.
        o.PlayedBytes = (long)(0.4 * o.Format.SampleRate) * o.Format.BlockAlign;
        engine.Pause();
        Assert.False(engine.IsPlaying);
        Assert.False(o.Playing);
        Assert.InRange(engine.Position.TotalSeconds, 7.39, 7.41);

        // Fortsetzen beginnt genau dort – mit demselben Ausgabegerät.
        engine.Play();
        var o2 = output();
        Assert.Same(o, o2);
        Assert.Equal(2, o.Starts);
        var s = o2.Pull(0.7);
        var change = Signal.FindToneChange(s, o2.Format.SampleRate, 1000, 1100);
        Assert.InRange(change, 0.58, 0.66);
    }

    [Fact]
    public void Dateiende_beendet_die_Wiedergabe()
    {
        var (engine, output) = Open("tones-44k-stereo-cbr.mp3");
        var ended = 0;
        engine.Ended += () => ended++;
        engine.Seek(engine.Duration - TimeSpan.FromSeconds(0.5));
        engine.Play();
        var s = output().Pull(2.0);
        Assert.InRange(s.Length / (double)output().Format.SampleRate, 0.45, 0.55);
        Assert.Equal(1, ended);
        Assert.False(engine.IsPlaying);
        Assert.Equal(engine.Duration, engine.Position);
    }

    [Fact]
    public void Dateiende_auch_bei_veraenderter_Geschwindigkeit()
    {
        var (engine, output) = Open("tones-22k-mono-vbr.mp3");
        var ended = 0;
        engine.Ended += () => ended++;
        engine.Tempo = 0.7;
        engine.Seek(engine.Duration - TimeSpan.FromSeconds(1));
        engine.Play();
        output().Pull(5.0);
        Assert.Equal(1, ended);
        Assert.False(engine.IsPlaying);
    }

    [Fact]
    public void Lautstaerke_wirkt_sofort()
    {
        var (engine, output) = Open("tones-44k-stereo-cbr.mp3");
        engine.Play();
        var loud = Signal.Rms(output().Pull(0.5));
        engine.Volume = 0.25f;
        var quiet = Signal.Rms(output().Pull(0.5));
        Assert.InRange(quiet / loud, 0.23, 0.27);
    }

    [Fact]
    public void Tempo_aendern_waehrend_der_Wiedergabe_behaelt_die_Position()
    {
        var (engine, output) = Open("tones-44k-stereo-cbr.mp3");
        engine.Seek(TimeSpan.FromSeconds(3));
        engine.Play();
        output().Pull(0.5);
        engine.Tempo = 0.5;
        Assert.True(engine.IsPlaying);
        Assert.InRange(engine.Position.TotalSeconds, 3.49, 3.51);
        output().Pull(1.0); // eine Sekunde Ausgabe = eine halbe Sekunde Quelle
        Assert.InRange(engine.Position.TotalSeconds, 3.99, 4.01);
    }

    [Fact]
    public void Alte_Ausgabe_liest_nach_dem_Anhalten_nichts_mehr()
    {
        var engine = FakeOutput.CreateEngine(out var outputs);
        engine.Attach(AudioReaders.Open(Signal.TestFile("tones-44k-stereo-cbr.mp3")));
        engine.Play();
        var o = outputs[0];
        o.Pull(0.2);
        var oldSession = o.Session!;
        engine.Pause();

        // Ein nachlaufender Ausgabe-Thread mit der alten Quelle darf nichts mehr lesen
        // (sonst würde er die Leseposition der Datei verschieben).
        Assert.Equal(0, oldSession.Read(new byte[4096], 0, 4096));

        engine.Play();
        Assert.Single(outputs);
        var s = o.Pull(0.1);
        Assert.InRange(Signal.Frequency(s.AsSpan(), o.Format.SampleRate), 290, 310);
        Assert.InRange(engine.Position.TotalSeconds, 0.29, 0.31);
    }

    [Fact]
    public void Grosse_Datei_oeffnet_zuegig()
    {
        // 60 Minuten Diktat (Mono, 32 kbit/s) – wird nur erzeugt, wenn ffmpeg vorhanden ist.
        var path = Path.Combine(Path.GetTempPath(), "transkription-60min.mp3");
        if (!File.Exists(path))
        {
            try
            {
                var p = System.Diagnostics.Process.Start("ffmpeg",
                    $"-hide_banner -loglevel error -y -f lavfi -i \"sine=frequency=440:duration=3600:sample_rate=16000\" -ac 1 -b:a 32k \"{path}\"");
                p!.WaitForExit();
            }
            catch
            {
                return; // ohne ffmpeg übersprungen
            }
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var engine = FakeOutput.CreateEngine(out var outputs);
        Func<FakeOutput> output = () => outputs[^1];
        engine.Attach(AudioReaders.Open(path));
        var openTime = sw.Elapsed;
        engine.Seek(TimeSpan.FromMinutes(45));
        engine.Play();
        var s = output().Pull(0.2);
        _log.WriteLine($"60-Minuten-Datei geöffnet in {openTime.TotalMilliseconds:0} ms, Sprung + Start in {(sw.Elapsed - openTime).TotalMilliseconds:0} ms");
        Assert.InRange(engine.Duration.TotalMinutes, 59.9, 60.1);
        Assert.InRange(Signal.Frequency(s.AsSpan(), output().Format.SampleRate), 430, 450);
        Assert.True(openTime < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Fehlendes_Ausgabegeraet_meldet_Fehler_statt_Absturz()
    {
        var engine = new AudioEngine(() => throw new InvalidOperationException("BadDeviceId calling waveOutOpen"));
        engine.Attach(AudioReaders.Open(Signal.TestFile("tones-44k-stereo-cbr.mp3")));
        Exception? error = null;
        engine.Failed += e => error = e;
        engine.Seek(TimeSpan.FromSeconds(3));
        engine.Play();
        Assert.NotNull(error);
        Assert.False(engine.IsPlaying);
        Assert.Equal(TimeSpan.FromSeconds(3), engine.Position);
        engine.Play(); // erneuter Versuch ebenfalls ohne Absturz
    }
}
