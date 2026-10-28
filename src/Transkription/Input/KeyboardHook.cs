using System.Runtime.InteropServices;
using Transkription.Core;
using Transkription.Settings;
using static Transkription.Input.NativeMethods;

namespace Transkription.Input;

/// <summary>
/// Systemweiter Tastatur-Hook. Erkennt die eingestellten Tasten auch dann, wenn ein anderes
/// Programm (z. B. Word) im Vordergrund ist, und meldet Drücken und Loslassen getrennt,
/// damit Rück-/Vorspulen und "Wiedergabe halten" funktionieren. Auch Fußschalter, die sich
/// als Tastatur ausgeben, werden so unterstützt.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hook;
    private readonly Dictionary<int, PlayerAction> _activeKeys = new();
    private Action<KeyBinding?>? _capture;

    public IReadOnlyList<KeyBinding> Bindings { get; set; } = Array.Empty<KeyBinding>();

    /// <summary>Wenn false, werden alle Tasten unverändert durchgereicht.</summary>
    public bool Enabled { get; set; } = true;

    public event Action<PlayerAction>? ActionDown;
    public event Action<PlayerAction>? ActionUp;

    public KeyboardHook()
    {
        _proc = HookProc; // Referenz halten, sonst räumt der GC den Delegaten weg
    }

    public bool Install()
    {
        if (_hook != IntPtr.Zero) return true;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        return _hook != IntPtr.Zero;
    }

    /// <summary>
    /// Die nächste gedrückte Taste (mit Umschalttasten) wird an <paramref name="callback"/> gemeldet
    /// statt eine Aktion auszulösen. Esc bricht ab (callback erhält null).
    /// </summary>
    public void BeginCapture(Action<KeyBinding?> callback) => _capture = callback;

    public void CancelCapture() => _capture = null;

    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var msg = (int)wParam;
            var isDown = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
            var isUp = msg is WM_KEYUP or WM_SYSKEYUP;
            var vk = (int)data.vkCode;

            // Selbst erzeugte Eingaben (Zeitmarke) nie auswerten.
            if ((data.flags & LLKHF_INJECTED) == 0 && (isDown || isUp) && Handle(vk, isDown))
                return (IntPtr)1; // Taste verschlucken
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool Handle(int vk, bool isDown)
    {
        if (_capture != null)
        {
            if (!isDown || KeyNames.IsModifier(vk)) return false;
            var cb = _capture;
            _capture = null;
            cb(vk == 0x1B ? null : new KeyBinding
            {
                VirtualKey = vk,
                Ctrl = Down(0x11),
                Alt = Down(0x12),
                Shift = Down(0x10),
                Win = Down(0x5B) || Down(0x5C),
            });
            return true;
        }

        if (!isDown)
        {
            // Loslassen immer melden, auch wenn die Hotkeys inzwischen deaktiviert wurden.
            if (!_activeKeys.Remove(vk, out var released)) return false;
            ActionUp?.Invoke(released);
            return true;
        }

        if (_activeKeys.ContainsKey(vk)) return true; // automatische Tastenwiederholung
        if (!Enabled) return false;

        var ctrl = Down(0x11);
        var alt = Down(0x12);
        var shift = Down(0x10);
        var win = Down(0x5B) || Down(0x5C);
        foreach (var b in Bindings)
        {
            if (b.VirtualKey == vk && b.Ctrl == ctrl && b.Alt == alt && b.Shift == shift && b.Win == win)
            {
                _activeKeys[vk] = b.Action;
                ActionDown?.Invoke(b.Action);
                return true;
            }
        }
        return false;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
