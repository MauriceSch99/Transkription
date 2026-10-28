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
    private readonly KeyboardHook _keys;
    private readonly FootPedal? _pedal;
    private readonly List<ActionRow> _rows;
    private ActionRow? _capturingKey;
    private ActionRow? _learningPedal;

    public SettingsWindow(AppSettings settings, KeyboardHook keys, FootPedal? pedal)
    {
        InitializeComponent();
        _settings = settings;
        _keys = keys;
        _pedal = pedal;

        _rows = PlayerActions.All.Select(a => new ActionRow(a)
        {
            Key = settings.KeyBindings.FirstOrDefault(b => b.Action == a),
            Pedal = settings.PedalBindings.FirstOrDefault(b => b.Action == a),
        }).ToList();
        Rows.ItemsSource = _rows;

        PedalModeBox.SelectedIndex = settings.PedalPlayMode == PlayMode.Hold ? 0 : 1;
        KeyModeBox.SelectedIndex = settings.KeyboardPlayMode == PlayMode.Hold ? 0 : 1;
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

        Closed += (_, _) =>
        {
            _keys.CancelCapture();
            _pedal?.CancelLearn();
            if (_pedal != null) _pedal.DevicesChanged -= FillDevices;
        };
    }

    public AppSettings Result => _settings;

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

    private static ActionRow RowOf(object sender) => (ActionRow)((FrameworkElement)sender).DataContext;

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

    private void LearnPedal_Click(object sender, RoutedEventArgs e)
    {
        if (_pedal == null) return;
        var row = RowOf(sender);
        var wasLearning = ReferenceEquals(_learningPedal, row);
        StopCapturing();
        if (wasLearning) return;

        _learningPedal = row;
        row.LearningPedal = true;
        _pedal.BeginLearn(binding =>
        {
            row.LearningPedal = false;
            _learningPedal = null;
            binding.Action = row.Action;
            foreach (var other in _rows.Where(r => r != row && r.Pedal != null && SamePedal(r.Pedal, binding)))
                other.Pedal = null;
            row.Pedal = binding;
        });
    }

    private static bool SamePedal(PedalBinding a, PedalBinding b) =>
        a.VendorId == b.VendorId && a.ProductId == b.ProductId && a.ByteIndex == b.ByteIndex && a.Mask == b.Mask;

    private void ClearPedal_Click(object sender, RoutedEventArgs e)
    {
        StopCapturing();
        RowOf(sender).Pedal = null;
    }

    private void StopCapturing()
    {
        _keys.CancelCapture();
        _pedal?.CancelLearn();
        foreach (var r in _rows)
        {
            r.CapturingKey = false;
            r.LearningPedal = false;
        }
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
        if (!TryRead(SkipBox, 0, 60, "Sprung bei kurzem Antippen", out var skip)) return;
        if (!TryRead(WindBox, 1, 30, "Spulgeschwindigkeit", out var wind)) return;
        if (!TryRead(SpeedStepBox, 1, 100, "Schrittweite", out var step)) return;

        _settings.BackstepSeconds = backstep;
        _settings.SkipSeconds = skip;
        _settings.WindSpeed = wind;
        _settings.SpeedStepPercent = (int)Math.Round(step);
        _settings.PedalPlayMode = PedalModeBox.SelectedIndex == 0 ? PlayMode.Hold : PlayMode.Toggle;
        _settings.KeyboardPlayMode = KeyModeBox.SelectedIndex == 0 ? PlayMode.Hold : PlayMode.Toggle;
        _settings.RememberPositions = RememberBox.IsChecked == true;
        _settings.HotkeysEnabled = HotkeysBox.IsChecked == true;
        _settings.MinimizeToTray = TrayBox.IsChecked == true;
        _settings.AlwaysOnTop = TopmostBox.IsChecked == true;
        _settings.KeyBindings = _rows.Where(r => r.Key != null).Select(r => r.Key!).ToList();
        _settings.PedalBindings = _rows.Where(r => r.Pedal != null).Select(r => r.Pedal!).ToList();

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

    /// <summary>Eine Zeile der Belegungstabelle.</summary>
    private sealed class ActionRow : INotifyPropertyChanged
    {
        private KeyBinding? _key;
        private PedalBinding? _pedal;
        private bool _capturingKey;
        private bool _learningPedal;

        public ActionRow(PlayerAction action) => Action = action;

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

        public PedalBinding? Pedal
        {
            get => _pedal;
            set
            {
                _pedal = value;
                Changed(nameof(PedalText));
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

        public bool LearningPedal
        {
            get => _learningPedal;
            set
            {
                _learningPedal = value;
                Changed(nameof(PedalButtonText));
                Changed(nameof(PedalText));
            }
        }

        public string KeyText => CapturingKey ? "Jetzt Taste drücken (Esc = Abbruch)…" : Key?.Describe() ?? "–";
        public string PedalText => LearningPedal ? "Pedal treten und loslassen (ggf. zweimal)…" : Pedal?.Describe() ?? "–";
        public string KeyButtonText => CapturingKey ? "Abbrechen" : "Taste ändern";
        public string PedalButtonText => LearningPedal ? "Abbrechen" : "Anlernen";

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Changed([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
