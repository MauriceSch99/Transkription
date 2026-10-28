using Transkription.Core;
using Transkription.Settings;
using Xunit;

namespace Transkription.Tests;

/// <summary>Bedienverhalten mit echter MP3-Datei, simulierter Soundkarte und simulierter Uhr.</summary>
public class PlayerControllerTests
{
    private DateTime _now = new(2026, 1, 1);
    private readonly List<FakeOutput> _outputs;
    private readonly PlayerController _player;
    private readonly AppSettings _settings = new(); // Standard: 1 s Rücksprung, 3 s Sprung, 4-fach spulen

    public PlayerControllerTests()
    {
        var engine = FakeOutput.CreateEngine(out _outputs);
        _player = new PlayerController(_settings, engine, now: () => _now);
    }

    private FakeOutput Output => _outputs[^1];

    private async Task LoadAsync(double startSeconds = 0)
    {
        await _player.LoadAsync(Signal.TestFile("tones-44k-stereo-cbr.mp3"), TimeSpan.FromSeconds(startSeconds));
        Assert.True(_player.HasMedia);
    }

    private void Advance(double seconds, int steps = 10)
    {
        for (var i = 0; i < steps; i++)
        {
            _now += TimeSpan.FromSeconds(seconds / steps);
            _player.Tick();
        }
    }

    private static void Near(double expected, TimeSpan actual, double tolerance = 0.02) =>
        Assert.InRange(actual.TotalSeconds, expected - tolerance, expected + tolerance);

    [Fact]
    public async Task Laden_setzt_an_gemerkter_Position_fort()
    {
        await LoadAsync(startSeconds: 7.5);
        Near(7.5, _player.Position);
        Assert.False(_player.IsPlaying);
        Near(20.04, _player.Duration, 0.1);
    }

    private void Down(PlayerAction a) => _player.ActionDown(a, InputSource.Pedal);
    private void Up(PlayerAction a) => _player.ActionUp(a, InputSource.Pedal);

    private void Tap(PlayerAction a)
    {
        Down(a);
        Up(a);
    }

    private double HeardFrequency(double seconds = 0.3)
    {
        var s = Output.Pull(seconds);
        var sr = Output.Format.SampleRate;
        return Signal.Frequency(s.AsSpan(sr / 10, s.Length - sr / 10), sr);
    }

    // ---------- Abspielen, solange gedrückt ----------

    [Fact]
    public async Task Abspielen_solange_gedrueckt_und_Loslassen_springt_zurueck()
    {
        await LoadAsync(5);
        Down(PlayerAction.PlayHold);
        Assert.True(_player.IsPlaying);
        Output.Pull(3.0);
        Near(8.0, _player.Position);

        Up(PlayerAction.PlayHold);
        Assert.False(_player.IsPlaying);
        Near(7.0, _player.Position); // 8 s gehört, 1 s Rücksprung

        // Erneut treten: geht an der zurückgesprungenen Stelle weiter.
        Down(PlayerAction.PlayHold);
        Assert.InRange(HeardFrequency(), 990, 1010); // Sekunde 7
    }

    [Fact]
    public async Task Ruecksprung_kann_ausgeschaltet_werden()
    {
        _settings.BackstepSeconds = 0;
        await LoadAsync(2);
        Down(PlayerAction.PlayHold);
        Output.Pull(1.0);
        Up(PlayerAction.PlayHold);
        Near(3.0, _player.Position);
    }

    // ---------- Abspielen / Anhalten ----------

    [Fact]
    public async Task Abspielen_Anhalten_schaltet_bei_jedem_Druck_um()
    {
        await LoadAsync(2);
        Tap(PlayerAction.PlayToggle);
        Assert.True(_player.IsPlaying); // Loslassen ändert nichts
        Output.Pull(2.0);
        Tap(PlayerAction.PlayToggle);
        Assert.False(_player.IsPlaying);
        Near(3.0, _player.Position); // 4 s – 1 s Rücksprung
        Tap(PlayerAction.PlayToggle);
        Assert.True(_player.IsPlaying);
    }

    [Fact]
    public async Task Anhalten_haelt_an_und_beendet_Halten()
    {
        await LoadAsync(4);
        Down(PlayerAction.PlayHold);
        Output.Pull(1.0);
        Tap(PlayerAction.Stop);
        Assert.False(_player.IsPlaying);
        Near(4.0, _player.Position);
        Up(PlayerAction.PlayHold); // nachträgliches Loslassen: kein weiterer Rücksprung
        Near(4.0, _player.Position);
        Tap(PlayerAction.Stop);    // erneutes Anhalten ändert nichts
        Near(4.0, _player.Position);
    }

    // ---------- Schnell spulen, solange gedrückt ----------

    [Fact]
    public async Task Schnell_zurueckspulen_solange_gedrueckt()
    {
        await LoadAsync(15);
        Down(PlayerAction.RewindHold);
        Assert.True(_player.IsWinding);
        Advance(1.0); // 4-fache Geschwindigkeit: 4 s zurück
        Near(11.0, _player.Position, 0.05);
        Advance(0.5);
        Near(9.0, _player.Position, 0.05);
        Up(PlayerAction.RewindHold);
        Assert.False(_player.IsWinding);
        Assert.False(_player.IsPlaying); // war vorher angehalten
        Advance(1.0);
        Near(9.0, _player.Position, 0.05); // nach dem Loslassen bewegt sich nichts mehr
    }

    [Fact]
    public async Task Schnell_vorspulen_solange_gedrueckt()
    {
        await LoadAsync(2);
        Down(PlayerAction.ForwardHold);
        Advance(1.0);
        Near(6.0, _player.Position, 0.05);
        Up(PlayerAction.ForwardHold);
        Assert.False(_player.IsPlaying);
    }

    [Fact]
    public async Task Spulgeschwindigkeit_ist_einstellbar()
    {
        _settings.WindSpeed = 8;
        await LoadAsync(1);
        Down(PlayerAction.ForwardHold);
        Advance(1.0);
        Up(PlayerAction.ForwardHold);
        Near(9.0, _player.Position, 0.05);
    }

    [Fact]
    public async Task Spulen_waehrend_der_Wiedergabe_spielt_danach_weiter()
    {
        await LoadAsync(10);
        Tap(PlayerAction.PlayToggle);
        Output.Pull(1.0);
        Down(PlayerAction.RewindHold);
        Assert.False(_player.IsPlaying);
        Advance(1.0);
        Up(PlayerAction.RewindHold);
        Assert.True(_player.IsPlaying);
        Near(7.0, _player.Position, 0.05); // 11 − 4
        Assert.InRange(HeardFrequency(), 990, 1010); // Sekunde 7 ist zu hören
    }

    [Fact]
    public async Task Vorspulen_waehrend_Abspiel_pedal_gehalten_spielt_weiter()
    {
        await LoadAsync(3);
        Down(PlayerAction.PlayHold);
        Output.Pull(1.0);
        Down(PlayerAction.ForwardHold);
        Advance(1.5);
        Near(10.0, _player.Position, 0.05); // 4 + 6
        Up(PlayerAction.ForwardHold);
        Assert.True(_player.IsPlaying); // Abspiel-Pedal ist noch getreten
        Assert.InRange(HeardFrequency(), 1290, 1310); // Sekunde 10

        Up(PlayerAction.PlayHold);
        Assert.False(_player.IsPlaying);
    }

    [Fact]
    public async Task Abspiel_pedal_waehrend_Spulen_losgelassen_spielt_danach_nicht()
    {
        await LoadAsync(3);
        Down(PlayerAction.PlayHold);
        Down(PlayerAction.RewindHold);
        Up(PlayerAction.PlayHold);
        Up(PlayerAction.RewindHold);
        Assert.False(_player.IsPlaying);
    }

    [Fact]
    public async Task Zurueckspulen_bleibt_am_Anfang_stehen()
    {
        await LoadAsync(1);
        Down(PlayerAction.RewindHold);
        Advance(3.0);
        Assert.Equal(TimeSpan.Zero, _player.Position);
        Up(PlayerAction.RewindHold);
    }

    [Fact]
    public async Task Vorspulen_bis_zum_Ende_startet_nicht_von_vorne()
    {
        await LoadAsync(17);
        Tap(PlayerAction.PlayToggle);
        Down(PlayerAction.ForwardHold);
        Advance(3.0);
        Assert.Equal(_player.Duration, _player.Position);
        Up(PlayerAction.ForwardHold);
        Assert.False(_player.IsPlaying);
        Assert.Equal(_player.Duration, _player.Position);
    }

    // ---------- Kurz springen ----------

    [Fact]
    public async Task Kurz_zurueckspringen_waehrend_der_Wiedergabe()
    {
        await LoadAsync(10);
        Tap(PlayerAction.PlayToggle);
        Output.Pull(1.0);
        Tap(PlayerAction.JumpBack);
        Assert.True(_player.IsPlaying); // läuft weiter
        Near(8.0, _player.Position);    // 11 − 3
        Assert.InRange(HeardFrequency(), 1090, 1110); // Sekunde 8
        Tap(PlayerAction.JumpBack);
        Tap(PlayerAction.JumpBack);
        Near(2.3, _player.Position, 0.05); // 8,3 − 6
    }

    [Fact]
    public async Task Kurz_vorspringen_im_Stillstand()
    {
        await LoadAsync(4);
        Tap(PlayerAction.JumpForward);
        Assert.False(_player.IsPlaying);
        Near(7.0, _player.Position);
        _settings.SkipSeconds = 5;
        Tap(PlayerAction.JumpForward);
        Near(12.0, _player.Position);
        Tap(PlayerAction.JumpForward);
        Tap(PlayerAction.JumpForward);
        Assert.Equal(_player.Duration, _player.Position); // nicht über das Ende hinaus
        Tap(PlayerAction.JumpBack);
        Near(_player.Duration.TotalSeconds - 5, _player.Position);
    }

    [Fact]
    public async Task Kurz_zurueckspringen_am_Anfang()
    {
        await LoadAsync(1);
        Tap(PlayerAction.JumpBack);
        Assert.Equal(TimeSpan.Zero, _player.Position);
    }

    [Fact]
    public async Task Keine_Funktion_tut_nichts()
    {
        await LoadAsync(5);
        Tap(PlayerAction.None);
        Assert.False(_player.IsPlaying);
        Near(5.0, _player.Position);
    }

    [Fact]
    public async Task Dateiende_und_erneutes_Abspielen_beginnt_von_vorne()
    {
        await LoadAsync(19.5);
        var changes = 0;
        _player.StateChanged += () => changes++;
        Tap(PlayerAction.PlayToggle);
        Output.Pull(2.0);
        Assert.False(_player.IsPlaying);
        Assert.True(changes >= 2);

        Tap(PlayerAction.PlayToggle);
        Assert.True(_player.IsPlaying);
        var s = Output.Pull(0.4);
        var sr = Output.Format.SampleRate;
        Assert.InRange(Signal.Frequency(s.AsSpan(sr / 10, sr / 5), sr), 290, 310);
    }

    [Fact]
    public async Task Geschwindigkeit_in_Schritten_und_begrenzt()
    {
        await LoadAsync();
        _player.ActionDown(PlayerAction.SpeedDown, InputSource.Keyboard);
        Assert.Equal(90, _settings.SpeedPercent);
        for (var i = 0; i < 20; i++) _player.ActionDown(PlayerAction.SpeedDown, InputSource.Keyboard);
        Assert.Equal(30, _settings.SpeedPercent);
        _player.ActionDown(PlayerAction.SpeedNormal, InputSource.Keyboard);
        Assert.Equal(100, _settings.SpeedPercent);
        for (var i = 0; i < 40; i++) _player.ActionDown(PlayerAction.SpeedUp, InputSource.Keyboard);
        Assert.Equal(300, _settings.SpeedPercent);
    }

    [Fact]
    public async Task Langsamere_Wiedergabe_mit_Pedal()
    {
        _settings.SpeedPercent = 70;
        await LoadAsync(2);
        Down(PlayerAction.PlayHold);
        Output.Pull(1.4); // 1,4 s bei 70 % = 0,98 s der Datei
        Near(2.98, _player.Position);
        Up(PlayerAction.PlayHold);
        Near(1.98, _player.Position);
    }

    [Fact]
    public async Task Schneller_Dateiwechsel_laedt_nur_die_letzte_Datei()
    {
        var first = _player.LoadAsync(Signal.TestFile("tones-8k-mono.mp3"), TimeSpan.Zero);
        var second = _player.LoadAsync(Signal.TestFile("tones-22k-mono-vbr.mp3"), TimeSpan.FromSeconds(3));
        await Task.WhenAll(first, second);
        Assert.EndsWith("tones-22k-mono-vbr.mp3", _player.CurrentPath);
        Near(3.0, _player.Position);
        _player.Play();
        Assert.Equal(22050, Output.Format.SampleRate);
    }

    [Fact]
    public async Task Defekte_Datei_meldet_Fehler()
    {
        var path = Path.Combine(Path.GetTempPath(), "kaputt-" + Guid.NewGuid() + ".mp3");
        await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3, 4, 5 });
        string? error = null;
        _player.MediaFailed += e => error = e;
        await _player.LoadAsync(path, TimeSpan.Zero);
        Assert.False(_player.HasMedia);
        Assert.False(_player.IsLoading);
        Assert.NotNull(error);
        foreach (var a in PlayerActions.All) Tap(a); // darf nicht abstürzen
        File.Delete(path);
    }
}
