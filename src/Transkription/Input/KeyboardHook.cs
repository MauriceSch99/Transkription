using System.Runtime.InteropServices;
using static Transkription.Input.NativeMethods;

namespace Transkription.Input;

/// <summary>
/// Systemweiter Tastatur-Hook. Erkennt die eingestellten Tasten auch dann, wenn ein anderes
/// Programm (z. B. Word) im Vordergrund ist. Die Zuordnung übernimmt <see cref="HotkeyMatcher"/>.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hook;

    public KeyboardHook(HotkeyMatcher matcher)
    {
        Matcher = matcher;
        _proc = HookProc; // Referenz halten, sonst räumt der GC den Delegaten weg
    }

    public HotkeyMatcher Matcher { get; }

    public bool Install()
    {
        if (_hook != IntPtr.Zero) return true;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        return _hook != IntPtr.Zero;
    }

    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static Modifiers CurrentModifiers() =>
        (Down(0x11) ? Modifiers.Ctrl : 0) |
        (Down(0x12) ? Modifiers.Alt : 0) |
        (Down(0x10) ? Modifiers.Shift : 0) |
        (Down(0x5B) || Down(0x5C) ? Modifiers.Win : 0);

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var msg = (int)wParam;
            var isDown = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
            var isUp = msg is WM_KEYUP or WM_SYSKEYUP;

            // Selbst erzeugte Eingaben (Zeitmarke) nie auswerten.
            if ((data.flags & LLKHF_INJECTED) == 0 && (isDown || isUp) &&
                Matcher.Handle((int)data.vkCode, isDown, CurrentModifiers()))
                return (IntPtr)1; // Taste verschlucken
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
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
