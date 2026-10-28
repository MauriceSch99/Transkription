using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Transkription.Core;
using Transkription.Settings;
using static Transkription.Input.NativeMethods;

namespace Transkription.Input;

public sealed record HidDeviceInfo(IntPtr Handle, int VendorId, int ProductId, int UsagePage, int Usage, string Name)
{
    public string Description => $"{Name}  (VID {VendorId:X4}, PID {ProductId:X4}, Usage {UsagePage:X2}/{Usage:X2})";
}

/// <summary>
/// Liest USB-Fußschalter (HID-Geräte wie Infinity IN-USB-1/2/3, Olympus, Philips u. a.)
/// über die Windows Raw-Input-Schnittstelle. Dank RIDEV_INPUTSINK kommen die Signale auch
/// an, wenn das Programm im Hintergrund oder im Infobereich läuft.
/// Pedale werden nicht anhand fester Gerätelisten erkannt, sondern angelernt: Beim Anlernen
/// wird gemerkt, welches Bit im Bericht des Geräts sich beim Treten ändert.
/// </summary>
public sealed class FootPedal : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly HashSet<(int Page, int Usage)> _registered = new();
    private readonly Dictionary<IntPtr, HidDeviceInfo> _devices = new();
    private readonly Dictionary<IntPtr, byte[]> _lastReport = new();
    private readonly HashSet<PedalBinding> _pressed = new();
    private readonly Dictionary<IntPtr, HashSet<(int Byte, int Bit)>> _learnCandidates = new();
    private readonly Dictionary<IntPtr, Queue<long>> _reportTimes = new();
    private readonly DispatcherTimer _rescanTimer;
    private readonly int _headerSize = Marshal.SizeOf<RAWINPUTHEADER>();
    private Action<PedalBinding>? _learn;

    private IReadOnlyList<PedalBinding> _bindings = Array.Empty<PedalBinding>();

    public IReadOnlyList<PedalBinding> Bindings
    {
        get => _bindings;
        set
        {
            _bindings = value;
            _pressed.Clear();
        }
    }

    /// <summary>Wenn false, werden keine Aktionen ausgelöst (Anlernen funktioniert weiterhin).</summary>
    public bool Enabled { get; set; } = true;

    public event Action<PlayerAction>? ActionDown;
    public event Action<PlayerAction>? ActionUp;
    public event Action? DevicesChanged;

    public FootPedal(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd) ?? throw new InvalidOperationException("Kein Fenster-Handle");
        _source.AddHook(WndProc);
        _rescanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _rescanTimer.Tick += (_, _) =>
        {
            _rescanTimer.Stop();
            Rescan();
        };
        Rescan();
    }

    public IReadOnlyCollection<HidDeviceInfo> Devices => _devices.Values;

    /// <summary>Ist ein Gerät angeschlossen, zu dem Pedalzuordnungen existieren?</summary>
    public bool HasBoundDevice =>
        _devices.Values.Any(d => Bindings.Any(b => b.VendorId == d.VendorId && b.ProductId == d.ProductId));

    public void BeginLearn(Action<PedalBinding> callback)
    {
        _learn = callback;
        _learnCandidates.Clear();
        _pressed.Clear();
    }

    public void CancelLearn()
    {
        _learn = null;
        _learnCandidates.Clear();
    }

    /// <summary>Sucht alle HID-Geräte und meldet deren Geräteklassen für Raw Input an.</summary>
    public void Rescan()
    {
        _devices.Clear();
        uint count = 0;
        var size = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
        if (GetRawInputDeviceList(null, ref count, size) != 0 || count == 0)
        {
            DevicesChanged?.Invoke();
            return;
        }
        var list = new RAWINPUTDEVICELIST[count];
        var got = GetRawInputDeviceList(list, ref count, size);
        if (got == uint.MaxValue)
        {
            DevicesChanged?.Invoke();
            return;
        }

        for (var i = 0; i < got; i++)
        {
            if (list[i].dwType != RIM_TYPEHID) continue;
            var info = QueryDevice(list[i].hDevice);
            if (info == null || !IsCandidate(info.UsagePage, info.Usage)) continue;
            _devices[info.Handle] = info;
            Register(info.UsagePage, info.Usage);
        }
        DevicesChanged?.Invoke();
    }

    /// <summary>Touchpads, Stifte und Sensoren liefern Unmengen an Berichten und sind nie Fußschalter.</summary>
    private static bool IsCandidate(int page, int usage) =>
        page != 0x0D && page != 0x20 && !(page == 0x01 && usage is 0x01 or 0x02 or 0x06 or 0x07);

    private void Register(int page, int usage)
    {
        if (!_registered.Add((page, usage))) return;
        var dev = new[]
        {
            new RAWINPUTDEVICE
            {
                usUsagePage = (ushort)page,
                usUsage = (ushort)usage,
                dwFlags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY,
                hwndTarget = _hwnd,
            },
        };
        if (!RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            _registered.Remove((page, usage));
    }

    private static HidDeviceInfo? QueryDevice(IntPtr handle)
    {
        var info = new RID_DEVICE_INFO_HID { cbSize = (uint)Marshal.SizeOf<RID_DEVICE_INFO_HID>() };
        var size = info.cbSize;
        if (GetRawInputDeviceInfo(handle, RIDI_DEVICEINFO, ref info, ref size) == uint.MaxValue) return null;
        if (info.dwType != RIM_TYPEHID) return null;

        var vid = (int)info.dwVendorId;
        var pid = (int)info.dwProductId;
        var name = ProductName(handle) ?? $"HID-Gerät {vid:X4}:{pid:X4}";
        return new HidDeviceInfo(handle, vid, pid, info.usUsagePage, info.usUsage, name);
    }

    private static string? ProductName(IntPtr handle)
    {
        try
        {
            uint chars = 0;
            GetRawInputDeviceName(handle, RIDI_DEVICENAME, null, ref chars);
            if (chars == 0) return null;
            var buf = new char[chars];
            if (GetRawInputDeviceName(handle, RIDI_DEVICENAME, buf, ref chars) == uint.MaxValue) return null;
            var path = new string(buf).TrimEnd('\0');

            using var file = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (file.IsInvalid) return null;
            var bytes = new byte[256];
            if (!HidD_GetProductString(file, bytes, bytes.Length)) return null;
            var s = System.Text.Encoding.Unicode.GetString(bytes);
            var end = s.IndexOf('\0');
            s = (end >= 0 ? s[..end] : s).Trim();
            return s.Length > 0 ? s : null;
        }
        catch
        {
            return null;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_INPUT:
                try
                {
                    OnInput(lParam);
                }
                catch
                {
                    // Fehlerhafte Berichte einzelner Geräte ignorieren.
                }
                break;
            case WM_INPUT_DEVICE_CHANGE:
            case WM_DEVICECHANGE:
                // Neu angeschlossene Geräte (auch neuer Geräteklassen) erkennen.
                _rescanTimer.Stop();
                _rescanTimer.Start();
                break;
        }
        return IntPtr.Zero;
    }

    private void OnInput(IntPtr hRawInput)
    {
        uint size = 0;
        GetRawInputData(hRawInput, RID_INPUT, null, ref size, (uint)_headerSize);
        if (size == 0) return;
        var buf = new byte[size];
        if (GetRawInputData(hRawInput, RID_INPUT, buf, ref size, (uint)_headerSize) == uint.MaxValue) return;

        var type = BitConverter.ToUInt32(buf, 0);
        if (type != RIM_TYPEHID) return;
        var hDevice = IntPtr.Size == 8 ? (IntPtr)BitConverter.ToInt64(buf, 8) : (IntPtr)BitConverter.ToInt32(buf, 8);

        var sizeHid = BitConverter.ToInt32(buf, _headerSize);
        var count = BitConverter.ToInt32(buf, _headerSize + 4);
        var offset = _headerSize + 8;
        if (sizeHid <= 0) return;

        if (!_devices.TryGetValue(hDevice, out var device))
        {
            device = QueryDevice(hDevice);
            if (device == null) return;
            _devices[hDevice] = device;
        }

        for (var i = 0; i < count && offset + sizeHid <= buf.Length; i++, offset += sizeHid)
            OnReport(device, buf.AsSpan(offset, sizeHid).ToArray());
    }

    private void OnReport(HidDeviceInfo device, byte[] report)
    {
        _lastReport.TryGetValue(device.Handle, out var previous);
        _lastReport[device.Handle] = report;

        if (_learn != null)
        {
            if (previous != null && previous.Length == report.Length && !IsNoisy(device.Handle))
                Learn(device, previous, report);
            return;
        }

        foreach (var b in Bindings)
        {
            if (b.VendorId != device.VendorId || b.ProductId != device.ProductId) continue;
            if (b.ByteIndex >= report.Length) continue;

            var down = (report[b.ByteIndex] & b.Mask) == b.Mask;
            if (down && Enabled && _pressed.Add(b))
            {
                ActionDown?.Invoke(b.Action);
            }
            else if (!down && _pressed.Remove(b))
            {
                ActionUp?.Invoke(b.Action);
            }
        }
    }

    /// <summary>Geräte, die ständig Berichte schicken (Gamepad-Achsen, Beleuchtung …), sind keine Pedale.</summary>
    private bool IsNoisy(IntPtr device)
    {
        var now = Environment.TickCount64;
        if (!_reportTimes.TryGetValue(device, out var times)) _reportTimes[device] = times = new Queue<long>();
        times.Enqueue(now);
        while (times.Count > 0 && now - times.Peek() > 1000) times.Dequeue();
        return times.Count > 30;
    }

    /// <summary>
    /// Ein Knopf gilt als erkannt, wenn dasselbe Bit erst umspringt und danach wieder zurück
    /// (Treten und Loslassen). Zufällige einmalige Änderungen anderer Geräte zählen so nicht.
    /// </summary>
    private void Learn(HidDeviceInfo device, byte[] previous, byte[] report)
    {
        if (!_learnCandidates.TryGetValue(device.Handle, out var candidates))
            _learnCandidates[device.Handle] = candidates = new HashSet<(int, int)>();

        for (var i = 0; i < report.Length; i++)
        {
            var diff = previous[i] ^ report[i];
            for (var bit = 0; bit < 8; bit++)
            {
                if ((diff & (1 << bit)) == 0) continue;
                if (candidates.Add((i, bit))) continue;

                var cb = _learn!;
                _learn = null;
                _learnCandidates.Clear();
                cb(new PedalBinding
                {
                    VendorId = device.VendorId,
                    ProductId = device.ProductId,
                    ByteIndex = i,
                    Mask = 1 << bit,
                    DeviceName = device.Name,
                });
                return;
            }
        }
    }

    public void Dispose()
    {
        _rescanTimer.Stop();
        _source.RemoveHook(WndProc);
    }
}
