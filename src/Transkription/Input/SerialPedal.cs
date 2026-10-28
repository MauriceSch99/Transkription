using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.Windows.Threading;
using static Transkription.Input.NativeMethods;

namespace Transkription.Input;

/// <summary>
/// Ältere Fußschalter mit 9-poligem Stecker (seriell, auch über USB-Seriell-Adapter).
/// Solche Pedale verbinden beim Treten die Leitungen DTR/RTS mit CTS, DSR, RI oder DCD.
/// Der Zustand dieser Leitungen wird abgefragt und als 1-Byte-Bericht an den
/// <see cref="PedalDecoder"/> gegeben (Bits 0x10 CTS, 0x20 DSR, 0x40 RI, 0x80 DCD).
/// </summary>
public sealed class SerialPedal : IDisposable
{
    private readonly SafeFileHandle _handle;
    private readonly PedalDecoder _decoder;
    private readonly Dispatcher _dispatcher;
    private readonly PedalDevice _device;
    private readonly Thread _thread;
    private volatile bool _stop;

    private SerialPedal(string port, SafeFileHandle handle, PedalDecoder decoder, Dispatcher dispatcher)
    {
        Port = port;
        _handle = handle;
        _decoder = decoder;
        _dispatcher = dispatcher;
        _device = new PedalDevice("com:" + port, 0, 0, port, "Serieller Fußschalter " + port);
        _thread = new Thread(Poll) { IsBackground = true, Name = "Serieller Fußschalter" };
        _thread.Start();
    }

    public string Port { get; }

    /// <summary>Alle COM-Anschlüsse des Rechners.</summary>
    public static List<string> PortNames()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            return key?.GetValueNames().Select(n => key.GetValue(n) as string).OfType<string>()
                       .OrderBy(p => p.Length).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToList()
                   ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>Öffnet den Anschluss. Gibt null zurück, wenn er nicht verfügbar ist.</summary>
    public static SerialPedal? Open(string port, PedalDecoder decoder, Dispatcher dispatcher, out string? error)
    {
        error = null;
        var handle = CreateFile(@"\\.\" + port, GENERIC_READ | GENERIC_WRITE, 0, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            error = $"{port} kann nicht geöffnet werden (belegt oder nicht vorhanden).";
            handle.Dispose();
            return null;
        }
        // DTR und RTS einschalten: darüber wird das Pedal mit Spannung versorgt.
        EscapeCommFunction(handle, SETDTR);
        EscapeCommFunction(handle, SETRTS);
        return new SerialPedal(port, handle, decoder, dispatcher);
    }

    private void Poll()
    {
        uint last = uint.MaxValue;
        while (!_stop)
        {
            if (!GetCommModemStatus(_handle, out var status))
            {
                // Adapter abgezogen.
                _dispatcher.BeginInvoke(() => _decoder.DeviceRemoved(_device));
                return;
            }
            status &= 0xF0;
            if (status != last)
            {
                last = status;
                var report = new[] { (byte)status };
                _dispatcher.BeginInvoke(() => _decoder.Process(_device, report, Environment.TickCount64));
            }
            Thread.Sleep(10);
        }
    }

    public void Dispose()
    {
        _stop = true;
        _thread.Join(500);
        _handle.Dispose();
        _decoder.DeviceRemoved(_device);
    }
}
