using Transkription.Core;
using Transkription.Settings;

namespace Transkription.Input;

[Flags]
public enum Modifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// Ordnet Tastendrücke den eingestellten Tastenkürzeln zu und meldet Drücken und Loslassen
/// getrennt. Unabhängig von Windows, damit es getestet werden kann.
/// </summary>
public sealed class HotkeyMatcher
{
    private readonly Dictionary<int, PlayerAction> _activeKeys = new();
    private Action<KeyBinding?>? _capture;

    public IReadOnlyList<KeyBinding> Bindings { get; set; } = Array.Empty<KeyBinding>();

    /// <summary>Wenn false, werden alle Tasten unverändert durchgereicht.</summary>
    public bool Enabled { get; set; } = true;

    public static bool IsModifier(int vk) => vk is
        0x10 or 0x11 or 0x12 or // Shift, Strg, Alt
        0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or
        0x5B or 0x5C; // Win

    public event Action<PlayerAction>? ActionDown;
    public event Action<PlayerAction>? ActionUp;

    /// <summary>Die nächste Taste wird an <paramref name="callback"/> gemeldet; Esc bricht ab (null).</summary>
    public void BeginCapture(Action<KeyBinding?> callback) => _capture = callback;

    public void CancelCapture() => _capture = null;

    /// <summary>Gibt true zurück, wenn die Taste verschluckt (nicht an andere Programme weitergegeben) werden soll.</summary>
    public bool Handle(int vk, bool isDown, Modifiers mods)
    {
        if (_capture != null)
        {
            if (!isDown || IsModifier(vk)) return false;
            var cb = _capture;
            _capture = null;
            cb(vk == 0x1B ? null : new KeyBinding
            {
                VirtualKey = vk,
                Ctrl = mods.HasFlag(Modifiers.Ctrl),
                Alt = mods.HasFlag(Modifiers.Alt),
                Shift = mods.HasFlag(Modifiers.Shift),
                Win = mods.HasFlag(Modifiers.Win),
            });
            return true;
        }

        if (!isDown)
        {
            // Loslassen immer melden, auch wenn die Kürzel inzwischen deaktiviert wurden.
            if (!_activeKeys.Remove(vk, out var released)) return false;
            ActionUp?.Invoke(released);
            return true;
        }

        if (_activeKeys.ContainsKey(vk)) return true; // automatische Tastenwiederholung
        if (!Enabled) return false;

        foreach (var b in Bindings)
        {
            if (b.VirtualKey == vk &&
                b.Ctrl == mods.HasFlag(Modifiers.Ctrl) && b.Alt == mods.HasFlag(Modifiers.Alt) &&
                b.Shift == mods.HasFlag(Modifiers.Shift) && b.Win == mods.HasFlag(Modifiers.Win))
            {
                _activeKeys[vk] = b.Action;
                ActionDown?.Invoke(b.Action);
                return true;
            }
        }
        return false;
    }
}
