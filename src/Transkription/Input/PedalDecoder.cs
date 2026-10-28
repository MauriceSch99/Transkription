using Transkription.Core;
using Transkription.Settings;

namespace Transkription.Input;

/// <summary>Ein Gerät, von dem Fußschalter-Daten kommen (USB-HID oder serieller Anschluss).</summary>
/// <param name="InstanceKey">Eindeutig für das angeschlossene Gerät (Handle bzw. Anschlussname).</param>
public sealed record PedalDevice(string InstanceKey, int VendorId, int ProductId, string? Port, string Name);

/// <summary>
/// Wertet die Berichte von Fußschaltern aus: erkennt angelernte Knöpfe und meldet Treten und
/// Loslassen. Unabhängig von Windows, damit es getestet werden kann.
/// </summary>
public sealed class PedalDecoder
{
    private readonly Dictionary<string, byte[]> _lastReport = new();
    private readonly HashSet<PedalBinding> _pressed = new();
    /// <summary>Beim Anlernen: Bits, die sich schon einmal geändert haben, mit Zeitpunkt.</summary>
    private readonly Dictionary<string, Dictionary<(int Byte, int Bit), long>> _learnCandidates = new();
    private readonly Dictionary<string, Queue<long>> _reportTimes = new();
    private IReadOnlyList<PedalBinding> _bindings = Array.Empty<PedalBinding>();
    private Action<PedalBinding>? _learn;

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

    public bool IsLearning => _learn != null;

    public event Action<PlayerAction>? ActionDown;
    public event Action<PlayerAction>? ActionUp;

    /// <summary>Wird bei jedem Bericht eines Geräts aufgerufen (für die Anzeige „Pedal erkannt“).</summary>
    public event Action<PedalDevice>? ReportReceived;

    public static bool Matches(PedalBinding binding, PedalDevice device) =>
        binding.Port != null || device.Port != null
            ? string.Equals(binding.Port, device.Port, StringComparison.OrdinalIgnoreCase)
            : binding.VendorId == device.VendorId && binding.ProductId == device.ProductId;

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

    public void Process(PedalDevice device, byte[] report, long nowMs)
    {
        _lastReport.TryGetValue(device.InstanceKey, out var previous);
        _lastReport[device.InstanceKey] = report;
        ReportReceived?.Invoke(device);

        // Manche Geräte wiederholen ihren Zustand ständig; nur echte Änderungen zählen.
        var changed = previous == null || !previous.AsSpan().SequenceEqual(report);
        var noisy = IsNoisy(device.InstanceKey, nowMs, changed);
        if (_learn != null)
        {
            if (noisy) _learnCandidates.Remove(device.InstanceKey);
            else if (previous != null && previous.Length == report.Length) Learn(device, previous, report, nowMs);
            return;
        }

        foreach (var b in _bindings)
        {
            if (!Matches(b, device) || b.ByteIndex >= report.Length) continue;

            var down = (report[b.ByteIndex] & b.Mask) == b.Mask;
            if (down && Enabled && _pressed.Add(b))
                ActionDown?.Invoke(b.Action);
            else if (!down && _pressed.Remove(b))
                ActionUp?.Invoke(b.Action);
        }
    }

    /// <summary>Gerät wurde entfernt: gedrückte Knöpfe loslassen, damit nichts „hängen“ bleibt.</summary>
    public void DeviceRemoved(PedalDevice device)
    {
        _lastReport.Remove(device.InstanceKey);
        foreach (var b in _pressed.Where(b => Matches(b, device)).ToList())
        {
            _pressed.Remove(b);
            ActionUp?.Invoke(b.Action);
        }
    }

    /// <summary>
    /// Geräte, die ständig Berichte schicken (Gamepad-Achsen, Beleuchtung …), sind keine Pedale.
    /// Ein Pedal ändert seinen Zustand nur beim Treten und Loslassen, also höchstens einige Male pro Sekunde.
    /// </summary>
    private bool IsNoisy(string key, long now, bool changed)
    {
        if (!_reportTimes.TryGetValue(key, out var times)) _reportTimes[key] = times = new Queue<long>();
        if (changed) times.Enqueue(now);
        while (times.Count > 0 && now - times.Peek() > 1000) times.Dequeue();
        return times.Count > 20;
    }

    /// <summary>Kürzeste Zeit zwischen Treten und Loslassen, die ein Mensch schafft.</summary>
    private const long MinPressMs = 40;

    /// <summary>
    /// Ein Knopf gilt als erkannt, wenn dasselbe Bit erst umspringt und danach wieder zurück
    /// (Treten und Loslassen). Zufällige einmalige Änderungen anderer Geräte zählen so nicht.
    /// </summary>
    private void Learn(PedalDevice device, byte[] previous, byte[] report, long now)
    {
        if (!_learnCandidates.TryGetValue(device.InstanceKey, out var candidates))
            _learnCandidates[device.InstanceKey] = candidates = new Dictionary<(int, int), long>();

        for (var i = 0; i < report.Length; i++)
        {
            var diff = previous[i] ^ report[i];
            for (var bit = 0; bit < 8; bit++)
            {
                if ((diff & (1 << bit)) == 0) continue;
                if (!candidates.TryGetValue((i, bit), out var firstChange) || now - firstChange < MinPressMs)
                {
                    // Erste Änderung – oder so schnell zurück, dass es kein Fuß war (Prellen, Rauschen).
                    candidates[(i, bit)] = now;
                    continue;
                }

                var cb = _learn!;
                _learn = null;
                _learnCandidates.Clear();
                cb(new PedalBinding
                {
                    VendorId = device.VendorId,
                    ProductId = device.ProductId,
                    Port = device.Port,
                    ByteIndex = i,
                    Mask = 1 << bit,
                    DeviceName = device.Name,
                });
                return;
            }
        }
    }
}
