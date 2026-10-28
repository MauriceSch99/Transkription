using System.Runtime.InteropServices;
using static Transkription.Input.NativeMethods;

namespace Transkription.Input;

/// <summary>Tippt Text in das Programm, das gerade im Vordergrund ist (z. B. Word).</summary>
public static class TextSender
{
    public static void Type(string text)
    {
        var inputs = new List<INPUT>();
        foreach (var c in text)
        {
            inputs.Add(Key(c, 0));
            inputs.Add(Key(c, KEYEVENTF_KEYUP));
        }
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    private static INPUT Key(char c, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | flags } },
    };
}
