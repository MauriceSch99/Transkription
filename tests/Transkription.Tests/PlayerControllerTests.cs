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
    private readonly AppSettings _settings = new(); // Standard: Pedal halten, Tastatur umschalten, 1 s Rücksprung

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

    [Fact]
    public async Task Pedal_halten_spielt_und_Loslassen_springt_zurueck()
    {
        await LoadAsync(5);
        _player.ActionDown(PlayerAction.Play, InputSource.Pedal);
        Assert.True(_player.IsPlaying);
        Output.Pull(3.0);
        Near(8.0, _player.Position);

        _player.ActionUp(PlayerAction.Play, InputSource.Pedal);
        Assert.False(_player.IsPlaying);
        Near(7.0, _player.Position); // 8 s gehört, 1 s Rücksprung

        // Erneut treten: geht an der zurückgesprungenen Stelle weiter.
        _player.ActionDown(PlayerAction.Play, InputSource.Pedal);
        var s = Output.Pull(0.3);
        var sr = Output.Format.SampleRate;
        Assert.InRange(Signal.Frequency(s.AsSpan(sr / 10, sr / 5), sr), 990, 1010); // Sekunde 7
    }

    [Fact]
    public async Task Taste_schaltet_um()
    {
        await LoadAsync(2);
        _player.ActionDown(PlayerAction.Play, InputSource.Keyboard);
        _player.ActionUp(PlayerAction.Play, InputSource.Keyboard);
        Assert.True(_player.IsPlaying); // Loslassen ändert nichts
        Output.Pull(2.0);
        _player.ActionDown(PlayerAction.Play, InputSource.Keyboard);
        Assert.False(_player.IsPlaying);
        Near(3.0, _player.Position);
    }

    [Fact]
    public async Task Ruecksprung_kann_ausgeschaltet_werden()
    {
        _settings.BackstepSeconds = 0;
        await LoadAsync(2);
        _player.ActionDown(PlayerAction.Play, InputSource.Pedal);
        Output.Pull(1.0);
        _player.ActionUp(PlayerAction.Play, InputSource.Pedal);
        Near(3.0, _player.Position);
    }

    [Fact]
    public async Task Zurueckspulen_antippen_springt_und_spielt_weiter()
    {
        await LoadAsync(10);
        _player.ActionDown(PlayerAction.Play, InputSource.Keyboard);
        Output.Pull(1.0);
        _player.ActionDown(PlayerAction.Rewind, InputSource.Pedal);
        Assert.False(_player.IsPlaying);
        Near(9.0, _player.Position); // 11 - 2
        Advance(0.1);
        _player.ActionUp(PlayerAction.Rewind, InputSource.Pedal);
        Assert.True(_player.IsPlaying);
        Near(9.0, _player.Position);
    }

    [Fact]
    public async Task Zurueckspulen_halten_spult_schnell()
    {
        await LoadAsync(15);
        _player.ActionDown(PlayerAction.Rewind, InputSource.Pedal);
        Near(13.0, _player.Position);
        Advance(0.3);              // innerhalb der Verzögerung: noch kein Spulen
        Near(13.0, _player.Position);
        Advance(1.0);              // 4-fache Geschwindigkeit: ca. 4 s zurück (+ Rest der Verzögerung)
        Assert.InRange(_player.Position.TotalSeconds, 8.6, 9.2);
        _player.ActionUp(PlayerAction.Rewind, InputSource.Pedal);
        Assert.False(_player.IsPlaying); // war vorher angehalten
    }

    [Fact]
    public async Task Zurueckspulen_bleibt_am_Anfang_stehen()
    {
        await LoadAsync(1);
        _player.ActionDown(PlayerAction.Rewind, InputSource.Pedal);
        Advance(3.0);
        Assert.Equal(TimeSpan.Zero, _player.Position);
        _player.ActionUp(PlayerAction.Rewind, InputSource.Pedal);
    }

    [Fact]
    public async Task Vorspulen_waehrend_Wiedergabe_pedal_gehalten()
    {
        await LoadAsync(3);
        _player.ActionDown(PlayerAction.Play, InputSource.Pedal);
        Output.Pull(1.0);
        _player.ActionDown(PlayerAction.FastForward, InputSource.Pedal);
        Advance(1.35);
        // 4 s + 2 s Sprung + ca. 1,05 s Spulen mit 4-facher Geschwindigkeit
        Assert.InRange(_player.Position.TotalSeconds, 10.0, 10.5);
        _player.ActionUp(PlayerAction.FastForward, InputSource.Pedal);
        Assert.True(_player.IsPlaying); // Wiedergabe-Pedal ist noch getreten
        var s = Output.Pull(0.3);
        Assert.InRange(Signal.Frequency(s.AsSpan(), Output.Format.SampleRate), 1250, 1350); // ~ Sekunde 10

        _player.ActionUp(PlayerAction.Play, InputSource.Pedal);
        Assert.False(_player.IsPlaying);
    }

    [Fact]
    public async Task Wiedergabe_pedal_waehrend_Spulen_losgelassen_spielt_danach_nicht()
    {
        await LoadAsync(3);
        _player.ActionDown(PlayerAction.Play, InputSource.Pedal);
        _player.ActionDown(PlayerAction.Rewind, InputSource.Pedal);
        _player.ActionUp(PlayerAction.Play, InputSource.Pedal);
        _player.ActionUp(PlayerAction.Rewind, InputSource.Pedal);
        Assert.False(_player.IsPlaying);
    }

    [Fact]
    public async Task Vorspulen_bis_zum_Ende_startet_nicht_von_vorne()
    {
        await LoadAsync(17);
        _player.ActionDown(PlayerAction.Play, InputSource.Keyboard);
        _player.ActionDown(PlayerAction.FastForward, InputSource.Pedal);
        Advance(3.0);
        Assert.Equal(_player.Duration, _player.Position);
        _player.ActionUp(PlayerAction.FastForward, InputSource.Pedal);
        Assert.False(_player.IsPlaying);
        Assert.Equal(_player.Duration, _player.Position);
    }

    [Fact]
    public async Task Dateiende_und_erneutes_Abspielen_beginnt_von_vorne()
    {
        await LoadAsync(19.5);
        var changes = 0;
        _player.StateChanged += () => changes++;
        _player.ActionDown(PlayerAction.Play, InputSource.Keyboard);
        Output.Pull(2.0);
        Assert.False(_player.IsPlaying);
        Assert.True(changes >= 2);

        _player.ActionDown(PlayerAction.Play, InputSource.Keyboard);
        Assert.True(_player.IsPlaying);
        var s = Output.Pull(0.4);
        var sr = Output.Format.SampleRate;
        Assert.InRange(Signal.Frequency(s.AsSpan(sr / 10, sr / 5), sr), 290, 310);
    }

    [Fact]
    public async Task Stopp_haelt_an_und_beendet_Halten()
    {
        await LoadAsync(4);
        _player.ActionDown(PlayerAction.Play, InputSource.Pedal);
        Output.Pull(1.0);
        _player.ActionDown(PlayerAction.Stop, InputSource.Keyboard);
        Assert.False(_player.IsPlaying);
        Near(4.0, _player.Position);
        _player.ActionUp(PlayerAction.Play, InputSource.Pedal); // nachträgliches Loslassen: kein weiterer Rücksprung
        Near(4.0, _player.Position);
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
        _player.ActionDown(PlayerAction.Play, InputSource.Pedal);
        Output.Pull(1.4); // 1,4 s bei 70 % = 0,98 s der Datei
        Near(2.98, _player.Position);
        _player.ActionUp(PlayerAction.Play, InputSource.Pedal);
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
        _player.ActionDown(PlayerAction.Play, InputSource.Pedal); // darf nicht abstürzen
        _player.ActionDown(PlayerAction.Rewind, InputSource.Pedal);
        File.Delete(path);
    }
}
