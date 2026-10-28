using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
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
/// Die Auswertung (Anlernen, Treten/Loslassen) übernimmt <see cref="PedalDecoder"/>.
/// </summary>
public sealed class FootPedal : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly HashSet<(int Page, int Usage)> _registered = new();
    private readonly Dictionary<IntPtr, HidDeviceInfo> _devices = new();
    private readonly DispatcherTimer _rescanTimer;
    private readonly int _headerSize = Marshal.SizeOf<RAWINPUTHEADER>();

    private readonly PedalDecoder _decoder;

    public event Action? DevicesChanged;

    public FootPedal(IntPtr hwnd, PedalDecoder decoder)
    {
        _hwnd = hwnd;
        _decoder = decoder;
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
        _devices.Values.Any(d => _decoder.Bindings.Any(b => PedalDecoder.Matches(b, ToPedal(d))));

    private static PedalDevice ToPedal(HidDeviceInfo d) =>
        new(d.Handle.ToString(), d.VendorId, d.ProductId, null, d.Name);

    /// <summary>Sucht alle HID-Geräte und meldet deren Geräteklassen für Raw Input an.</summary>
    public void Rescan()
    {
        var before = _devices.Values.ToList();
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
        foreach (var gone in before.Where(d => !_devices.ContainsKey(d.Handle)))
            _decoder.DeviceRemoved(ToPedal(gone));
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
            _decoder.Process(ToPedal(device), buf.AsSpan(offset, sizeHid).ToArray(), Environment.TickCount64);
    }

    public void Dispose()
    {
        _rescanTimer.Stop();
        _source.RemoveHook(WndProc);
    }
}
