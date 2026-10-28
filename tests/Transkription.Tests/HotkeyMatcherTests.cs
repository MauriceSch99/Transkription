using Transkription.Core;
using Transkription.Input;
using Transkription.Settings;
using Xunit;

namespace Transkription.Tests;

public class HotkeyMatcherTests
{
    private const int F9 = 0x78, F7 = 0x76, A = 0x41, Ctrl = 0x11, Esc = 0x1B;

    private readonly HotkeyMatcher _m = new() { Bindings = AppSettings.DefaultKeyBindings() };
    private readonly List<string> _events = new();

    public HotkeyMatcherTests()
    {
        _m.ActionDown += a => _events.Add("+" + a);
        _m.ActionUp += a => _events.Add("-" + a);
    }

    [Fact]
    public void Belegte_Taste_wird_verschluckt_und_gemeldet()
    {
        Assert.True(_m.Handle(F9, true, Modifiers.None));
        Assert.True(_m.Handle(F9, true, Modifiers.None)); // Tastenwiederholung beim Halten
        Assert.True(_m.Handle(F9, false, Modifiers.None));
        Assert.Equal(new[] { "+Play", "-Play" }, _events);
    }

    [Fact]
    public void Andere_Tasten_gehen_unveraendert_an_Word()
    {
        Assert.False(_m.Handle(A, true, Modifiers.None));
        Assert.False(_m.Handle(A, false, Modifiers.None));
        Assert.False(_m.Handle(F9, true, Modifiers.Ctrl)); // Strg+F9 ist nicht belegt
        Assert.False(_m.Handle(F9, false, Modifiers.None));
        Assert.Empty(_events);
    }

    [Fact]
    public void Rueckspulen_halten_meldet_Loslassen()
    {
        _m.Handle(F7, true, Modifiers.None);
        _m.Handle(F7, true, Modifiers.None);
        _m.Handle(F7, false, Modifiers.Shift); // Umschalttaste beim Loslassen egal
        Assert.Equal(new[] { "+Rewind", "-Rewind" }, _events);
    }

    [Fact]
    public void Ausgeschaltet_wird_nichts_verschluckt()
    {
        _m.Enabled = false;
        Assert.False(_m.Handle(F9, true, Modifiers.None));
        Assert.False(_m.Handle(F9, false, Modifiers.None));
        Assert.Empty(_events);
    }

    [Fact]
    public void Tastenkombination_aufnehmen()
    {
        KeyBinding? captured = null;
        var called = false;
        _m.BeginCapture(b => { captured = b; called = true; });
        Assert.False(_m.Handle(Ctrl, true, Modifiers.Ctrl)); // Strg allein wird übersprungen
        Assert.True(_m.Handle(A, true, Modifiers.Ctrl));
        Assert.True(called);
        Assert.Equal(A, captured!.VirtualKey);
        Assert.True(captured.Ctrl);
        Assert.Empty(_events);

        _m.BeginCapture(b => captured = b);
        Assert.True(_m.Handle(Esc, true, Modifiers.None));
        Assert.Null(captured);
    }
}
