using Transkription.Core;
using Transkription.Input;
using Transkription.Settings;
using Xunit;

namespace Transkription.Tests;

/// <summary>
/// Simuliert einen Philips-Fußschalter mit vier Pedalen. USB-Modelle (LFH/ACC 2310–2330,
/// Hersteller-ID 0911) schicken bei jeder Änderung einen Bericht, in dem jedes Pedal ein Bit
/// belegt; serielle Modelle schalten Leitungen (CTS, DSR, RI, DCD).
/// </summary>
public class PedalDecoderTests
{
    private static readonly PedalDevice Philips = new("hid-1", 0x0911, 0x1844, null, "Philips Foot Control");
    private static readonly PedalDevice Gamepad = new("hid-2", 0x045E, 0x028E, null, "Gamepad");
    private static readonly PedalDevice Serial = new("com:COM1", 0, 0, "COM1", "Serieller Fußschalter COM1");

    private readonly PedalDecoder _decoder = new();
    private readonly List<string> _events = new();
    private long _time;

    public PedalDecoderTests()
    {
        _decoder.ActionDown += a => _events.Add("+" + a);
        _decoder.ActionUp += a => _events.Add("-" + a);
    }

    private void Send(PedalDevice device, params byte[] report)
    {
        _time += 100;
        _decoder.Process(device, report, _time);
    }

    private PedalBinding Learn(PlayerAction action, Action press)
    {
        PedalBinding? learned = null;
        _decoder.BeginLearn(b => learned = b);
        press();
        Assert.NotNull(learned);
        Assert.False(_decoder.IsLearning);
        learned!.Action = action;
        return learned;
    }

    [Fact]
    public void Vier_Pedale_anlernen_und_benutzen()
    {
        Send(Philips, 0x00, 0x00); // Ruhezustand (kommt z. B. beim Einstecken)

        var rewind = Learn(PlayerAction.Rewind, () => { Send(Philips, 0, 0x01); Send(Philips, 0, 0x00); });
        var play = Learn(PlayerAction.Play, () => { Send(Philips, 0, 0x02); Send(Philips, 0, 0x00); });
        var forward = Learn(PlayerAction.FastForward, () => { Send(Philips, 0, 0x04); Send(Philips, 0, 0x00); });
        var stop = Learn(PlayerAction.Stop, () => { Send(Philips, 0, 0x08); Send(Philips, 0, 0x00); });

        Assert.Equal((1, 0x01), (rewind.ByteIndex, rewind.Mask));
        Assert.Equal((1, 0x02), (play.ByteIndex, play.Mask));
        Assert.Equal((1, 0x04), (forward.ByteIndex, forward.Mask));
        Assert.Equal((1, 0x08), (stop.ByteIndex, stop.Mask));
        Assert.Equal(0x0911, play.VendorId);
        Assert.Null(play.Port);
        Assert.Equal("Philips Foot Control – Knopf 10", play.Describe());

        _decoder.Bindings = new[] { rewind, play, forward, stop };
        _events.Clear();

        Send(Philips, 0, 0x02);       // Wiedergabe treten
        Send(Philips, 0, 0x03);       // zusätzlich Rücklauf
        Send(Philips, 0, 0x02);       // Rücklauf los
        Send(Philips, 0, 0x02);       // unveränderter Bericht: nichts
        Send(Philips, 0, 0x00);       // Wiedergabe los
        Send(Philips, 0, 0x08);
        Send(Philips, 0, 0x00);

        Assert.Equal(new[] { "+Play", "+Rewind", "-Rewind", "-Play", "+Stop", "-Stop" }, _events);
    }

    [Fact]
    public void Anlernen_ohne_vorherigen_Bericht_braucht_zweiten_Tritt()
    {
        PedalBinding? learned = null;
        _decoder.BeginLearn(b => learned = b);
        Send(Philips, 0x02);   // erster Bericht überhaupt: noch kein Vergleich möglich
        Send(Philips, 0x00);   // Loslassen: Bit springt um (1. Änderung)
        Assert.Null(learned);
        Send(Philips, 0x02);   // erneut treten: dasselbe Bit springt zurück → erkannt
        Assert.NotNull(learned);
        Assert.Equal(0x02, learned!.Mask);
    }

    [Fact]
    public void Anlernen_ignoriert_dauernd_sendende_Geraete()
    {
        // Gamepad sendet ständig 50 Berichte pro Sekunde mit wackelnden Achsenwerten –
        // schon bevor „Anlernen“ geklickt wird und auch währenddessen.
        PedalBinding? learned = null;
        var t = 0L;
        for (var i = 0; i < 100; i++, t += 20)
        {
            if (i == 30) _decoder.BeginLearn(b => learned = b);
            _decoder.Process(Gamepad, new[] { (byte)(i % 2), (byte)(i % 3) }, t);
        }
        Assert.Null(learned);
        Assert.True(_decoder.IsLearning);

        // Das echte Pedal wird trotzdem erkannt.
        _decoder.Process(Philips, new byte[] { 0, 0 }, t += 20);
        _decoder.Process(Gamepad, new byte[] { 1, 1 }, t += 20);
        _decoder.Process(Philips, new byte[] { 0, 2 }, t += 20);
        _decoder.Process(Gamepad, new byte[] { 0, 2 }, t += 20);
        _decoder.Process(Philips, new byte[] { 0, 0 }, t += 200);
        Assert.Equal(0x0911, learned!.VendorId);
        Assert.Equal(0x02, learned.Mask);
    }

    [Fact]
    public void Pedal_das_staendig_seinen_Zustand_sendet_wird_erkannt()
    {
        // Manche USB-Geräte schicken ihren Zustand alle 8 ms, auch ohne Änderung.
        PedalBinding? learned = null;
        var t = 0L;
        void Stream(byte state, int ms)
        {
            for (var end = t + ms; t < end; t += 8) _decoder.Process(Philips, new byte[] { 0, state }, t);
        }
        Stream(0, 2000);
        _decoder.BeginLearn(b => learned = b);
        Stream(0, 500);
        Stream(0x04, 250);
        Stream(0, 500);
        Assert.NotNull(learned);
        Assert.Equal(0x04, learned!.Mask);

        learned.Action = PlayerAction.FastForward;
        _decoder.Bindings = new[] { learned };
        Stream(0x04, 300);
        Stream(0, 300);
        Assert.Equal(new[] { "+FastForward", "-FastForward" }, _events);
    }

    [Fact]
    public void Prellen_wird_nicht_als_Tritt_gewertet()
    {
        PedalBinding? learned = null;
        _decoder.BeginLearn(b => learned = b);
        _decoder.Process(Philips, new byte[] { 0 }, 1000);
        _decoder.Process(Philips, new byte[] { 1 }, 1005); // Kontakt prellt: 5 ms an, wieder aus
        _decoder.Process(Philips, new byte[] { 0 }, 1010);
        Assert.Null(learned);
        _decoder.Process(Philips, new byte[] { 1 }, 1500);
        _decoder.Process(Philips, new byte[] { 0 }, 1700);
        Assert.NotNull(learned);
    }

    [Fact]
    public void Einzelne_Stoerung_eines_anderen_Geraets_wird_nicht_angelernt()
    {
        PedalBinding? learned = null;
        _decoder.BeginLearn(b => learned = b);
        Send(Gamepad, 0x00);
        Send(Gamepad, 0x10);   // einmalige Änderung, kein Zurückspringen
        Send(Philips, 0, 0x00);
        Send(Philips, 0, 0x04);
        Send(Philips, 0, 0x00);
        Assert.NotNull(learned);
        Assert.Equal(0x0911, learned!.VendorId);
        Assert.Equal(0x04, learned.Mask);
    }

    [Fact]
    public void Gleiches_Modell_an_anderem_USB_Anschluss_funktioniert()
    {
        var play = new PedalBinding { Action = PlayerAction.Play, VendorId = 0x0911, ProductId = 0x1844, ByteIndex = 0, Mask = 0x02 };
        _decoder.Bindings = new[] { play };
        var replugged = Philips with { InstanceKey = "hid-99" };
        Send(replugged, 0x02);
        Send(replugged, 0x00);
        Assert.Equal(new[] { "+Play", "-Play" }, _events);
    }

    [Fact]
    public void Andere_Geraete_loesen_nichts_aus()
    {
        _decoder.Bindings = new[] { new PedalBinding { Action = PlayerAction.Play, VendorId = 0x0911, ProductId = 0x1844, ByteIndex = 0, Mask = 0x02 } };
        Send(Gamepad, 0x02);
        Send(Serial, 0x02);
        Assert.Empty(_events);
    }

    [Fact]
    public void Ausgeschaltet_loest_nichts_aus_aber_Loslassen_kommt_an()
    {
        var play = new PedalBinding { Action = PlayerAction.Play, VendorId = 0x0911, ProductId = 0x1844, ByteIndex = 0, Mask = 0x02 };
        _decoder.Bindings = new[] { play };
        Send(Philips, 0x02);
        _decoder.Enabled = false;
        Send(Philips, 0x00);      // Loslassen nach dem Ausschalten muss trotzdem ankommen
        Send(Philips, 0x02);      // ausgeschaltet: kein Treten
        Assert.Equal(new[] { "+Play", "-Play" }, _events);
    }

    [Fact]
    public void Abziehen_waehrend_getreten_laesst_los()
    {
        var play = new PedalBinding { Action = PlayerAction.Play, VendorId = 0x0911, ProductId = 0x1844, ByteIndex = 0, Mask = 0x02 };
        _decoder.Bindings = new[] { play };
        Send(Philips, 0x02);
        _decoder.DeviceRemoved(Philips);
        Assert.Equal(new[] { "+Play", "-Play" }, _events);
    }

    [Fact]
    public void Serieller_Fusschalter_mit_vier_Leitungen()
    {
        Send(Serial, 0x00);
        var cts = Learn(PlayerAction.Rewind, () => { Send(Serial, 0x10); Send(Serial, 0x00); });
        var dsr = Learn(PlayerAction.Play, () => { Send(Serial, 0x20); Send(Serial, 0x00); });
        var ri = Learn(PlayerAction.FastForward, () => { Send(Serial, 0x40); Send(Serial, 0x00); });
        var dcd = Learn(PlayerAction.Stop, () => { Send(Serial, 0x80); Send(Serial, 0x00); });
        Assert.Equal("COM1", dsr.Port);
        Assert.Equal("Serieller Fußschalter COM1 – Leitung DSR", dsr.Describe());
        Assert.Equal("Serieller Fußschalter COM1 – Leitung DCD", dcd.Describe());

        _decoder.Bindings = new[] { cts, dsr, ri, dcd };
        Send(Serial, 0x20);
        Send(Serial, 0x60);
        Send(Serial, 0x20);
        Send(Serial, 0x00);
        Assert.Equal(new[] { "+Play", "+FastForward", "-FastForward", "-Play" }, _events);

        // Gleicher Anschluss in anderer Schreibweise
        _events.Clear();
        Send(Serial with { Port = "com1", InstanceKey = "com:com1" }, 0x10);
        Assert.Equal(new[] { "+Rewind" }, _events);
    }

    [Fact]
    public void Pedalbelegung_wird_gespeichert_und_geladen()
    {
        var settings = new AppSettings
        {
            PedalBindings =
            {
                new PedalBinding { Action = PlayerAction.Play, VendorId = 0x0911, ProductId = 0x1844, ByteIndex = 1, Mask = 2, DeviceName = "Philips" },
                new PedalBinding { Action = PlayerAction.Rewind, Port = "COM3", Mask = 0x10 },
            },
            SerialPort = "COM3",
        };
        var copy = settings.Clone();
        Assert.Equal(2, copy.PedalBindings.Count);
        Assert.Equal(PlayerAction.Play, copy.PedalBindings[0].Action);
        Assert.Null(copy.PedalBindings[0].Port);
        Assert.Equal("COM3", copy.PedalBindings[1].Port);
        Assert.Equal("COM3", copy.SerialPort);
        Assert.Equal(7, copy.KeyBindings.Count);
    }
}
