using Transkription.Core;
using Transkription.Input;
using Transkription.Settings;
using Xunit;

namespace Transkription.Tests;

/// <summary>
/// Ende-zu-Ende: Philips-Fußschalter mit vier Pedalen → Anlernen → frei gewählte Funktionen →
/// Wiedergabe einer echten MP3-Datei. Geprüft wird, was tatsächlich zu hören ist.
/// </summary>
public class FourPedalTests
{
    private static readonly PedalDevice Philips = new("hid-1", 0x0911, 0x1844, null, "Philips Foot Control");
    private static readonly byte[] PedalBits = { 0x01, 0x02, 0x04, 0x08 };

    private readonly PedalDecoder _decoder = new();
    private readonly PlayerController _player;
    private readonly List<FakeOutput> _outputs;
    private readonly AppSettings _settings = new();
    private DateTime _now = new(2026, 1, 1);
    private long _ms;
    private byte _state;

    public FourPedalTests()
    {
        var engine = FakeOutput.CreateEngine(out _outputs);
        _player = new PlayerController(_settings, engine, now: () => _now);
        // Wie im Hauptfenster: Pedal-Ereignisse gehen an die Wiedergabesteuerung.
        _decoder.ActionDown += a => _player.ActionDown(a, InputSource.Pedal);
        _decoder.ActionUp += a => _player.ActionUp(a, InputSource.Pedal);
    }

    private FakeOutput Output => _outputs[^1];

    private void Report()
    {
        _ms += 150;
        _decoder.Process(Philips, new byte[] { 0, _state }, _ms);
    }

    private void Press(int pedal) { _state |= PedalBits[pedal]; Report(); }
    private void Release(int pedal) { _state &= (byte)~PedalBits[pedal]; Report(); }

    private void Wait(double seconds)
    {
        for (var i = 0; i < 20; i++)
        {
            _now += TimeSpan.FromSeconds(seconds / 20);
            _player.Tick();
        }
    }

    /// <summary>Lernt die vier Pedale an und ordnet ihnen die gewünschten Funktionen zu.</summary>
    private void Configure(params PlayerAction[] functions)
    {
        Report(); // Ruhezustand
        var bindings = new List<PedalBinding>();
        for (var i = 0; i < 4; i++)
        {
            PedalBinding? learned = null;
            _decoder.BeginLearn(b => learned = b);
            Press(i);
            Release(i);
            Assert.NotNull(learned);
            learned!.Action = functions[i];
            bindings.Add(learned);
        }
        _settings.PedalBindings = bindings;
        _decoder.Bindings = bindings;
    }

    private double Heard()
    {
        var s = Output.Pull(0.3);
        var sr = Output.Format.SampleRate;
        return Signal.Frequency(s.AsSpan(sr / 10, s.Length - sr / 10), sr);
    }

    private async Task Load(double start) =>
        await _player.LoadAsync(Signal.TestFile("tones-44k-stereo-cbr.mp3"), TimeSpan.FromSeconds(start));

    [Fact]
    public async Task Belegung_Halten_wie_beim_Diktiergeraet()
    {
        Configure(PlayerAction.RewindHold, PlayerAction.PlayHold, PlayerAction.ForwardHold, PlayerAction.JumpBack);
        await Load(5);

        Press(1);                              // Mitte: abspielen, solange getreten
        Assert.True(_player.IsPlaying);
        Assert.InRange(Heard(), 790, 810);     // Sekunde 5
        Output.Pull(1.7);
        Release(1);
        Assert.False(_player.IsPlaying);
        Assert.InRange(_player.Position.TotalSeconds, 5.98, 6.02); // 7 s gehört − 1 s Rücksprung

        Press(0); Wait(0.5); Release(0);       // links: schnell zurück (4×)
        Assert.InRange(_player.Position.TotalSeconds, 3.95, 4.05);

        Press(2); Wait(1.0); Release(2);       // rechts: schnell vor
        Assert.InRange(_player.Position.TotalSeconds, 7.95, 8.05);

        Press(3); Release(3);                  // viertes Pedal: kurz zurück (3 s)
        Assert.InRange(_player.Position.TotalSeconds, 4.95, 5.05);
        Assert.False(_player.IsPlaying);

        Press(1);
        Assert.InRange(Heard(), 790, 810);     // wieder Sekunde 5
        Release(1);
    }

    [Fact]
    public async Task Belegung_Umschalten_und_Springen()
    {
        Configure(PlayerAction.JumpBack, PlayerAction.PlayToggle, PlayerAction.JumpForward, PlayerAction.Stop);
        await Load(10);

        Press(1); Release(1);                  // Mitte: abspielen
        Assert.True(_player.IsPlaying);        // läuft nach dem Loslassen weiter
        Output.Pull(1.0);

        Press(0); Release(0);                  // links: kurz zurück, läuft weiter
        Assert.True(_player.IsPlaying);
        Assert.InRange(Heard(), 1090, 1110);   // 11 − 3 = Sekunde 8

        Press(2); Release(2);                  // rechts: kurz vor
        Assert.True(_player.IsPlaying);
        Assert.InRange(Heard(), 1390, 1410);   // 8,3 + 3 = Sekunde 11

        Press(1); Release(1);                  // Mitte: anhalten
        Assert.False(_player.IsPlaying);

        Press(1); Release(1);                  // Mitte: wieder abspielen
        Assert.True(_player.IsPlaying);
        Press(3); Release(3);                  // viertes Pedal: anhalten
        Assert.False(_player.IsPlaying);
    }

    [Fact]
    public async Task Gemischte_Belegung_nach_Wunsch()
    {
        // z. B. links kurz zurück, Mitte halten, rechts schnell vor, oben langsamer
        Configure(PlayerAction.JumpBack, PlayerAction.PlayHold, PlayerAction.ForwardHold, PlayerAction.SpeedDown);
        await Load(3);

        Press(3); Release(3);
        Assert.Equal(90, _settings.SpeedPercent);

        Press(1);
        Output.Pull(1.8);                      // 1,8 s bei 90 % = 1,62 s Datei
        Press(0); Release(0);                  // während das Abspiel-Pedal gehalten wird: kurz zurück
        Assert.True(_player.IsPlaying);
        Assert.InRange(_player.Position.TotalSeconds, 1.6, 1.64); // 4,62 − 3
        Release(1);
        Assert.False(_player.IsPlaying);
    }

    [Fact]
    public async Task Zwei_Pedale_gleichzeitig()
    {
        Configure(PlayerAction.RewindHold, PlayerAction.PlayHold, PlayerAction.ForwardHold, PlayerAction.None);
        await Load(12);
        Press(1);                              // abspielen halten
        Output.Pull(0.5);
        Press(0);                              // zusätzlich zurückspulen
        Assert.False(_player.IsPlaying);
        Wait(1.0);
        Release(0);                            // Abspiel-Pedal noch getreten → spielt weiter
        Assert.True(_player.IsPlaying);
        Assert.InRange(_player.Position.TotalSeconds, 8.45, 8.55); // 12,5 − 4
        Press(3); Release(3);                  // Pedal ohne Funktion
        Assert.True(_player.IsPlaying);
        Release(1);
        Assert.False(_player.IsPlaying);
    }
}
