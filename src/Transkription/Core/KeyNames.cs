using System.Windows.Input;
using Transkription.Settings;

namespace Transkription.Core;

public static class KeyNames
{
    public static string Describe(Settings.KeyBinding b)
    {
        var parts = new List<string>();
        if (b.Ctrl) parts.Add("Strg");
        if (b.Alt) parts.Add("Alt");
        if (b.Shift) parts.Add("Umschalt");
        if (b.Win) parts.Add("Win");
        parts.Add(Describe(b.VirtualKey));
        return string.Join(" + ", parts);
    }

    public static string Describe(int vk)
    {
        var key = KeyInterop.KeyFromVirtualKey(vk);
        return key switch
        {
            Key.None => $"Taste {vk:X2}",
            >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (int)(key - Key.NumPad0),
            Key.Space => "Leertaste",
            Key.Return => "Eingabe",
            Key.Back => "Rücktaste",
            Key.Escape => "Esc",
            Key.Insert => "Einfg",
            Key.Delete => "Entf",
            Key.Home => "Pos1",
            Key.End => "Ende",
            Key.PageUp => "Bild auf",
            Key.PageDown => "Bild ab",
            Key.Left => "Pfeil links",
            Key.Right => "Pfeil rechts",
            Key.Up => "Pfeil oben",
            Key.Down => "Pfeil unten",
            Key.Pause => "Pause",
            Key.Scroll => "Rollen",
            Key.MediaPlayPause => "Medien Wiedergabe/Pause",
            Key.MediaStop => "Medien Stopp",
            Key.MediaNextTrack => "Medien Weiter",
            Key.MediaPreviousTrack => "Medien Zurück",
            Key.Multiply => "Num *",
            Key.Add => "Num +",
            Key.Subtract => "Num -",
            Key.Divide => "Num /",
            Key.Decimal => "Num ,",
            _ => key.ToString(),
        };
    }
}
