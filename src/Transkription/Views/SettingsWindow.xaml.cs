using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Transkription.Core;
using Transkription.Input;
using Transkription.Settings;

namespace Transkription.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly HotkeyMatcher _keys;
    private readonly PedalDecoder _pedals;
    private readonly FootPedal? _pedal;
    private readonly Action<string?> _selectSerialPort;
    private readonly List<KeyRow> _rows;
    private readonly ObservableCollection<PedalRow> _pedalRows = new();
    private KeyRow? _capturingKey;
    private PedalRow? _learningPedal;

    public SettingsWindow(AppSettings settings, HotkeyMatcher keys, PedalDecoder pedals, FootPedal? pedal,
        Action<string?> selectSerialPort)
    {
        InitializeComponent();
        _settings = settings;
        _keys = keys;
        _pedals = pedals;
        _pedal = pedal;
        _selectSerialPort = selectSerialPort;

        _rows = PlayerActions.All.Select(a => new KeyRow(a)
        {
            Key = settings.KeyBindings.FirstOrDefault(b => b.Action == a),
        }).ToList();
        KeyRows.ItemsSource = _rows;

        // Ohne gespeicherte Pedale vier Pedale mit der üblichen Belegung vorschlagen.
        var savedPedals = settings.PedalBindings.Count > 0
            ? settings.PedalBindings
            : AppSettings.PedalPreset(AppSettings.PedalLayout.Hold);
        foreach (var b in savedPedals)
            _pedalRows.Add(new PedalRow { Action = b.Action, Signal = b.IsLearned ? b : null });
        Renumber();
        PedalRows.ItemsSource = _pedalRows;
        BackstepBox.Text = Format(settings.BackstepSeconds);
        SkipBox.Text = Format(settings.SkipSeconds);
        WindBox.Text = Format(settings.WindSpeed);
        SpeedStepBox.Text = settings.SpeedStepPercent.ToString(CultureInfo.CurrentCulture);
        RememberBox.IsChecked = settings.RememberPositions;

        HotkeysBox.IsChecked = settings.HotkeysEnabled;
        TrayBox.IsChecked = settings.MinimizeToTray;
        TopmostBox.IsChecked = settings.AlwaysOnTop;
        AutostartBox.IsChecked = Autostart.IsEnabled;

        if (_pedal == null)
        {
            DeviceList.Items.Add("Der Zugriff auf USB-Eingabegeräte ist nicht verfügbar.");
        }
        else
        {
            _pedal.DevicesChanged += FillDevices;
            FillDevices();
        }

        SerialBox.Items.Add("Keiner");
        foreach (var port in SerialPedal.PortNames()) SerialBox.Items.Add(port);
        if (settings.SerialPort.Length > 0 && !SerialBox.Items.Contains(settings.SerialPort))
            SerialBox.Items.Add(settings.SerialPort);
        SerialBox.SelectedItem = settings.SerialPort.Length > 0 ? settings.SerialPort : "Keiner";
        SerialBox.SelectionChanged += (_, _) => _selectSerialPort(SelectedSerialPort);

        _pedals.ReportReceived += OnPedalReport;
        Closed += (_, _) =>
        {
            _keys.CancelCapture();
            _pedals.CancelLearn();
            _pedals.ReportReceived -= OnPedalReport;
            if (_pedal != null) _pedal.DevicesChanged -= FillDevices;
        };
    }

    public AppSettings Result => _settings;

    private string SelectedSerialPort => SerialBox.SelectedItem as string is { } p && p != "Keiner" ? p : "";

    /// <summary>Zeigt an, von welchem Gerät gerade Signale kommen – hilft beim Finden des Pedals.</summary>
    private void OnPedalReport(PedalDevice device) =>
        LastSignalText.Text = $"Letztes Signal: {device.Name}  ({DateTime.Now:HH:mm:ss})";

    private static string Format(double v) => v.ToString("0.##", CultureInfo.CurrentCulture);

    private void FillDevices()
    {
        DeviceList.Items.Clear();
        var devices = _pedal!.Devices.OrderBy(d => d.Name).ToList();
        if (devices.Count == 0) DeviceList.Items.Add("Keine Geräte gefunden.");
        foreach (var d in devices) DeviceList.Items.Add(d.Description);
    }

    private void Rescan_Click(object sender, RoutedEventArgs e) => _pedal?.Rescan();

    // ---------- Tasten ----------

    private static KeyRow RowOf(object sender) => (KeyRow)((FrameworkElement)sender).DataContext;

    private void CaptureKey_Click(object sender, RoutedEventArgs e)
    {
        var row = RowOf(sender);
        var wasCapturing = ReferenceEquals(_capturingKey, row);
        StopCapturing();
        if (wasCapturing) return;

        _capturingKey = row;
        row.CapturingKey = true;
        _keys.BeginCapture(binding => Dispatcher.BeginInvoke(() =>
        {
            row.CapturingKey = false;
            _capturingKey = null;
            if (binding == null) return; // Esc
            binding.Action = row.Action;

            // Dieselbe Taste darf nur eine Aktion auslösen.
            foreach (var other in _rows.Where(r => r != row && r.Key != null && SameKey(r.Key, binding)))
                other.Key = null;
            row.Key = binding;
        }));
    }

    private static bool SameKey(KeyBinding a, KeyBinding b) =>
        a.VirtualKey == b.VirtualKey && a.Ctrl == b.Ctrl && a.Alt == b.Alt && a.Shift == b.Shift && a.Win == b.Win;

    private void ClearKey_Click(object sender, RoutedEventArgs e)
    {
        StopCapturing();
        RowOf(sender).Key = null;
    }

    private void ResetKeys_Click(object sender, RoutedEventArgs e)
    {
        StopCapturing();
        var defaults = AppSettings.DefaultKeyBindings();
        foreach (var row in _rows) row.Key = defaults.FirstOrDefault(b => b.Action == row.Action);
    }

    // ---------- Fußschalter ----------

    private static PedalRow PedalOf(object sender) => (PedalRow)((FrameworkElement)sender).DataContext;

    private void Renumber()
    {
        for (var i = 0; i < _pedalRows.Count; i++) _pedalRows[i].Number = i + 1;
    }

    private void LearnPedal_Click(object sender, RoutedEventArgs e)
    {
        var row = PedalOf(sender);
        var wasLearning = ReferenceEquals(_learningPedal, row);
        StopCapturing();
        if (wasLearning) return;

        _learningPedal = row;
        row.Learning = true;
        _pedals.BeginLearn(binding =>
        {
            row.Learning = false;
            _learningPedal = null;
            // Ein Pedal-Signal gehört immer nur zu einem Pedal.
            foreach (var other in _pedalRows.Where(r => r != row && r.Signal != null && SamePedal(r.Signal, binding)))
                other.Signal = null;
            row.Signal = binding;
        });
    }

    private static bool SamePedal(PedalBinding a, PedalBinding b) =>
        a.VendorId == b.VendorId && a.ProductId == b.ProductId && a.Port == b.Port &&
        a.ByteIndex == b.ByteIndex && a.Mask == b.Mask;

    private void RemovePedal_Click(object sender, RoutedEventArgs e)
    {
        StopCapturing();
        _pedalRows.Remove(PedalOf(sender));
        Renumber();
    }

    private void AddPedal_Click(object sender, RoutedEventArgs e)
    {
        StopCapturing();
        _pedalRows.Add(new PedalRow { Action = PlayerAction.None });
        Renumber();
    }

    private void PresetHold_Click(object sender, RoutedEventArgs e) => ApplyPreset(AppSettings.PedalLayout.Hold);

    private void PresetToggle_Click(object sender, RoutedEventArgs e) => ApplyPreset(AppSettings.PedalLayout.Toggle);

    /// <summary>Setzt nur die Funktionen; bereits angelernte Signale bleiben erhalten.</summary>
    private void ApplyPreset(AppSettings.PedalLayout layout)
    {
        StopCapturing();
        var actions = AppSettings.PresetActions(layout);
        while (_pedalRows.Count < actions.Length) _pedalRows.Add(new PedalRow());
        for (var i = 0; i < _pedalRows.Count; i++)
            _pedalRows[i].Action = i < actions.Length ? actions[i] : PlayerAction.None;
        Renumber();
    }

    private void StopCapturing()
    {
        _keys.CancelCapture();
        _pedals.CancelLearn();
        foreach (var r in _rows) r.CapturingKey = false;
        foreach (var r in _pedalRows) r.Learning = false;
        _learningPedal = null;
        _capturingKey = null;
    }

    // ---------- Übernehmen ----------

    private bool TryRead(TextBox box, double min, double max, string label, out double value)
    {
        var text = box.Text.Trim();
        if ((double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
             double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) &&
            value >= min && value <= max)
            return true;

        MessageBox.Show(this, $"„{label}“ muss eine Zahl zwischen {Format(min)} und {Format(max)} sein.",
            "Einstellungen", MessageBoxButton.OK, MessageBoxImage.Warning);
        box.Focus();
        box.SelectAll();
        return false;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!TryRead(BackstepBox, 0, 30, "Automatischer Rücksprung", out var backstep)) return;
        if (!TryRead(SkipBox, 0.5, 60, "Sprungweite", out var skip)) return;
        if (!TryRead(WindBox, 1.5, 30, "Geschwindigkeit für schnelles Spulen", out var wind)) return;
        if (!TryRead(SpeedStepBox, 1, 100, "Schrittweite", out var step)) return;

        _settings.BackstepSeconds = backstep;
        _settings.SkipSeconds = skip;
        _settings.WindSpeed = wind;
        _settings.SpeedStepPercent = (int)Math.Round(step);
        _settings.RememberPositions = RememberBox.IsChecked == true;
        _settings.HotkeysEnabled = HotkeysBox.IsChecked == true;
        _settings.MinimizeToTray = TrayBox.IsChecked == true;
        _settings.AlwaysOnTop = TopmostBox.IsChecked == true;
        _settings.SerialPort = SelectedSerialPort;
        _settings.KeyBindings = _rows.Where(r => r.Key != null).Select(r => r.Key!).ToList();
        _settings.PedalBindings = _pedalRows.Select(r => r.ToBinding()).ToList();

        try
        {
            if (AutostartBox.IsChecked == true != Autostart.IsEnabled)
                Autostart.Set(AutostartBox.IsChecked == true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Autostart konnte nicht geändert werden: " + ex.Message, "Einstellungen",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        DialogResult = true;
    }

    /// <summary>Eine Zeile der Tastenbelegung.</summary>
    private sealed class KeyRow : Observable
    {
        private KeyBinding? _key;
        private bool _capturingKey;

        public KeyRow(PlayerAction action) => Action = action;

        public PlayerAction Action { get; }
        public string Name => PlayerActions.DisplayName(Action);

        public KeyBinding? Key
        {
            get => _key;
            set
            {
                _key = value;
                Changed(nameof(KeyText));
            }
        }

        public bool CapturingKey
        {
            get => _capturingKey;
            set
            {
                _capturingKey = value;
                Changed(nameof(KeyButtonText));
                Changed(nameof(KeyText));
            }
        }

        public string KeyText => CapturingKey ? "Jetzt Taste drücken (Esc = Abbruch)…" : (Key != null ? KeyNames.Describe(Key) : null) ?? "–";
        public string KeyButtonText => CapturingKey ? "Abbrechen" : "Taste ändern";
    }

    public sealed record FunctionOption(PlayerAction Action, string Name);

    /// <summary>Ein Pedal mit frei wählbarer Funktion.</summary>
    private sealed class PedalRow : Observable
    {
        public static readonly IReadOnlyList<FunctionOption> AllOptions =
            PlayerActions.All.Prepend(PlayerAction.None)
                .Select(a => new FunctionOption(a, PlayerActions.DisplayName(a))).ToList();

        private int _number;
        private PlayerAction _action;
        private PedalBinding? _signal;
        private bool _learning;

        public IReadOnlyList<FunctionOption> Options => AllOptions;

        public int Number
        {
            get => _number;
            set
            {
                _number = value;
                Changed(nameof(Title));
            }
        }

        public string Title => $"Pedal {Number}";

        public PlayerAction Action
        {
            get => _action;
            set
            {
                _action = value;
                Changed();
            }
        }

        public PedalBinding? Signal
        {
            get => _signal;
            set
            {
                _signal = value;
                Changed(nameof(SignalText));
            }
        }

        public bool Learning
        {
            get => _learning;
            set
            {
                _learning = value;
                Changed(nameof(SignalText));
                Changed(nameof(LearnButtonText));
            }
        }

        public string SignalText => Learning ? "Pedal treten und loslassen (ggf. zweimal)…" : Signal?.Describe() ?? "noch nicht angelernt";
        public string LearnButtonText => Learning ? "Abbrechen" : "Anlernen";

        public PedalBinding ToBinding() => new()
        {
            Action = Action,
            VendorId = Signal?.VendorId ?? 0,
            ProductId = Signal?.ProductId ?? 0,
            Port = Signal?.Port,
            ByteIndex = Signal?.ByteIndex ?? 0,
            Mask = Signal?.Mask ?? 0,
            DeviceName = Signal?.DeviceName ?? "",
        };
    }

    private abstract class Observable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void Changed([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
