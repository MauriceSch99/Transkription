using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Transkription.Core;
using Transkription.Input;
using Transkription.Settings;
using Transkription.Views;

namespace Transkription;

public partial class MainWindow : Window
{
    public static readonly string[] AudioExtensions = { ".mp3", ".wav", ".wma", ".m4a", ".aac", ".mp4" };

    private AppSettings _settings;
    private readonly PlayerController _player;
    private readonly KeyboardHook _keys = new();
    private readonly ObservableCollection<FileItem> _files = new();
    private readonly DispatcherTimer _uiTimer;
    private readonly DispatcherTimer _lastInputTimer;
    private FootPedal? _pedal;
    private TrayIcon? _tray;
    private FileItem? _current;
    private bool _hookInstalled;
    private bool _updatingUi;
    private bool _wasPlaying;
    private bool _exiting;
    private bool _settingsOpen;

    public MainWindow(AppSettings settings)
    {
        _settings = settings;
        _updatingUi = true; // Regler lösen beim Aufbau ValueChanged aus
        InitializeComponent();
        _updatingUi = false;

        _player = new PlayerController(settings);
        _player.MediaOpened += OnMediaOpened;
        _player.MediaFailed += OnMediaFailed;
        _player.StateChanged += UpdateUi;

        FileList.ItemsSource = _files;

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _uiTimer.Tick += (_, _) => UpdateUi();
        _lastInputTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _lastInputTimer.Tick += (_, _) =>
        {
            _lastInputTimer.Stop();
            LastInput.Text = "";
        };

        RewindButton.PreviewMouseLeftButtonDown += (_, _) => _player.ActionDown(PlayerAction.Rewind, InputSource.Ui);
        RewindButton.PreviewMouseLeftButtonUp += (_, _) => _player.ActionUp(PlayerAction.Rewind, InputSource.Ui);
        RewindButton.LostMouseCapture += (_, _) => _player.ActionUp(PlayerAction.Rewind, InputSource.Ui);
        ForwardButton.PreviewMouseLeftButtonDown += (_, _) => _player.ActionDown(PlayerAction.FastForward, InputSource.Ui);
        ForwardButton.PreviewMouseLeftButtonUp += (_, _) => _player.ActionUp(PlayerAction.FastForward, InputSource.Ui);
        ForwardButton.LostMouseCapture += (_, _) => _player.ActionUp(PlayerAction.FastForward, InputSource.Ui);

        Drop += OnDrop;
        StateChanged += OnWindowStateChanged;
        Closing += OnClosing;
    }

    /// <summary>Wird von <see cref="App"/> nach dem Erzeugen aufgerufen.</summary>
    public void Initialize(bool showWindow, IReadOnlyList<string> openFiles)
    {
        RestoreWindowBounds();
        Topmost = _settings.AlwaysOnTop;

        // Das Fenster-Handle wird für den Fußschalter benötigt, auch wenn das Fenster verborgen bleibt.
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        try
        {
            _pedal = new FootPedal(hwnd);
            _pedal.ActionDown += a => OnInput(a, InputSource.Pedal, down: true);
            _pedal.ActionUp += a => OnInput(a, InputSource.Pedal, down: false);
            _pedal.DevicesChanged += UpdateStatus;
        }
        catch (Exception ex)
        {
            PedalStatus.Text = "Fußschalter nicht verfügbar: " + ex.Message;
        }

        _hookInstalled = _keys.Install();
        _keys.ActionDown += a => OnInput(a, InputSource.Keyboard, down: true);
        _keys.ActionUp += a => OnInput(a, InputSource.Keyboard, down: false);

        _tray = new TrayIcon();
        _tray.OpenRequested += ShowFromTray;
        _tray.PlayPauseRequested += () => _player.TogglePlay();
        _tray.SettingsRequested += () =>
        {
            ShowFromTray();
            OpenSettings();
        };
        _tray.ExitRequested += ExitApplication;

        ApplySettings();

        foreach (var entry in _settings.Files)
            _files.Add(new FileItem(entry));

        var added = AddFiles(openFiles);
        var start = added.FirstOrDefault()
                    ?? _files.FirstOrDefault(f => string.Equals(f.Entry.Path, _settings.LastFile, StringComparison.OrdinalIgnoreCase));
        if (start != null) FileList.SelectedItem = start;

        _uiTimer.Start();
        UpdateUi();

        if (showWindow) Show();
    }

    // ---------- Eingaben von Tastatur und Fußschalter ----------

    private void OnInput(PlayerAction action, InputSource source, bool down)
    {
        // Die Hooks müssen sofort zurückkehren, daher die eigentliche Arbeit verzögert ausführen.
        Dispatcher.BeginInvoke(() =>
        {
            if (down)
            {
                ShowLastInput(action, source);
                if (action == PlayerAction.InsertTimestamp)
                {
                    if (_player.HasMedia) TextSender.Type($"[{TimeFormat.Short(_player.Position)}] ");
                    return;
                }
                _player.ActionDown(action, source);
            }
            else
            {
                _player.ActionUp(action, source);
            }
        });
    }

    private void ShowLastInput(PlayerAction action, InputSource source)
    {
        var via = source == InputSource.Pedal ? "Fußschalter" : "Taste";
        LastInput.Text = $"{via}: {PlayerActions.DisplayName(action)}";
        _lastInputTimer.Stop();
        _lastInputTimer.Start();
    }

    // ---------- Dateiliste ----------

    private List<FileItem> AddFiles(IEnumerable<string> paths)
    {
        var added = new List<FileItem>();
        foreach (var raw in paths)
        {
            string path;
            try
            {
                path = Path.GetFullPath(raw);
            }
            catch
            {
                continue;
            }
            if (!File.Exists(path)) continue;

            var existing = _files.FirstOrDefault(f => string.Equals(f.Entry.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                added.Add(existing);
                continue;
            }
            var entry = new FileEntry { Path = path };
            _settings.Files.Add(entry);
            var item = new FileItem(entry);
            _files.Add(item);
            added.Add(item);
        }
        if (added.Count > 0) _settings.Save();
        return added;
    }

    /// <summary>Dateien, die von einer zweiten Programminstanz übergeben wurden.</summary>
    public void OpenFromOtherInstance(IReadOnlyList<string> files)
    {
        ShowFromTray();
        var added = AddFiles(files);
        if (added.Count > 0) FileList.SelectedItem = added[0];
    }

    private void LoadItem(FileItem item)
    {
        if (ReferenceEquals(item, _current)) return;
        RememberPosition();
        if (_current != null) _current.IsCurrent = false;

        _current = item;
        item.IsCurrent = true;
        _settings.LastFile = item.Entry.Path;

        if (!File.Exists(item.Entry.Path))
        {
            _player.Unload();
            _current = null;
            item.IsCurrent = false;
            MessageBox.Show(this, $"Die Datei wurde nicht gefunden:\n{item.Entry.Path}", "Transkription",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var start = _settings.RememberPositions ? TimeSpan.FromSeconds(item.Entry.PositionSeconds) : TimeSpan.Zero;
        _player.Load(item.Entry.Path, start);
        CurrentFileText.Text = item.Name;
        UpdateUi();
    }

    private void RememberPosition()
    {
        if (_current == null || !_player.HasMedia) return;
        _current.SetPosition(_settings.RememberPositions ? _player.Position : TimeSpan.Zero);
    }

    private void OnMediaOpened()
    {
        if (_current != null && _player.Duration > TimeSpan.Zero) _current.SetDuration(_player.Duration);
        _settings.Save();
    }

    private void OnMediaFailed(string message)
    {
        var name = _current?.Name ?? "Datei";
        if (_current != null) _current.IsCurrent = false;
        _current = null;
        MessageBox.Show(this,
            $"„{name}“ konnte nicht abgespielt werden.\n\n{message}\n\n" +
            "Hinweis: Für die Wiedergabe wird das Windows Media Feature Pack benötigt " +
            "(bei Windows-„N“-Editionen nachinstallieren).",
            "Transkription", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void RemoveSelected()
    {
        var selected = FileList.SelectedItems.Cast<FileItem>().ToList();
        if (selected.Count == 0) return;
        foreach (var item in selected)
        {
            if (ReferenceEquals(item, _current))
            {
                _player.Unload();
                _current = null;
                CurrentFileText.Text = "Keine Datei geladen";
            }
            _files.Remove(item);
            _settings.Files.Remove(item.Entry);
        }
        _settings.Save();
    }

    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FileList.SelectedItems.Count == 1 && FileList.SelectedItem is FileItem item)
            LoadItem(item);
    }

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileList.SelectedItem is FileItem item && ReferenceEquals(item, _current))
            _player.TogglePlay();
    }

    private void FileList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            RemoveSelected();
            e.Handled = true;
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] dropped) return;
        var files = new List<string>();
        foreach (var p in dropped)
        {
            if (Directory.Exists(p))
                files.AddRange(Directory.EnumerateFiles(p).Where(IsAudioFile).OrderBy(f => f));
            else if (IsAudioFile(p))
                files.Add(p);
        }
        var added = AddFiles(files);
        if (added.Count > 0) FileList.SelectedItem = added[0];
    }

    private static bool IsAudioFile(string path) =>
        AudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    // ---------- Anzeige ----------

    private void UpdateUi()
    {
        _updatingUi = true;
        try
        {
            var pos = _player.Position;
            var dur = _player.Duration;
            PositionText.Text = TimeFormat.Long(pos);
            DurationText.Text = "/ " + TimeFormat.Short(dur);
            PositionSlider.Maximum = Math.Max(1, dur.TotalSeconds);
            PositionSlider.Value = Math.Min(pos.TotalSeconds, PositionSlider.Maximum);
            PositionSlider.IsEnabled = _player.HasMedia;

            PlayButton.Content = _player.IsPlaying ? "" : "";
            SpeedSlider.Value = _settings.SpeedPercent;
            SpeedText.Text = $"{_settings.SpeedPercent} %";
            VolumeSlider.Value = _settings.Volume;
            VolumeText.Text = $"{Math.Round(_settings.Volume * 100)} %";

            StateText.Text = !_player.HasMedia
                ? (_player.CurrentPath != null ? "Wird geladen…" : "")
                : _player.IsWinding ? "Spulen…"
                : _player.IsPlaying ? "Wiedergabe"
                : "Angehalten";

            if (_wasPlaying && !_player.IsPlaying && !_player.IsWinding)
            {
                // Beim Anhalten Position merken, damit sie auch nach einem Absturz erhalten bleibt.
                RememberPosition();
                _settings.Save();
            }
            _wasPlaying = _player.IsPlaying;

            var title = _current != null ? $"{_current.Name} – Transkription" : "Transkription";
            if (Title != title)
            {
                Title = title;
                _tray?.SetText(title);
            }
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private void UpdateStatus()
    {
        if (!_hookInstalled)
            HotkeyStatus.Text = "Tastenkürzel konnten nicht aktiviert werden";
        else
            HotkeyStatus.Text = _settings.HotkeysEnabled ? "Tastenkürzel aktiv (auch im Hintergrund)" : "Tastenkürzel ausgeschaltet";

        if (_pedal == null) return;
        if (_settings.PedalBindings.Count == 0)
            PedalStatus.Text = "Fußschalter: nicht eingerichtet (Einstellungen → Anlernen)";
        else if (_pedal.HasBoundDevice)
            PedalStatus.Text = _settings.HotkeysEnabled ? "Fußschalter: verbunden" : "Fußschalter: ausgeschaltet";
        else
            PedalStatus.Text = "Fußschalter: nicht angeschlossen";
    }

    private void ApplySettings()
    {
        _player.Settings = _settings;
        _keys.Bindings = _settings.KeyBindings;
        _keys.Enabled = _settings.HotkeysEnabled && !_settingsOpen;
        if (_pedal != null)
        {
            _pedal.Bindings = _settings.PedalBindings;
            _pedal.Enabled = _settings.HotkeysEnabled && !_settingsOpen;
        }
        HotkeysMenu.IsChecked = _settings.HotkeysEnabled;
        TopmostMenu.IsChecked = _settings.AlwaysOnTop;
        Topmost = _settings.AlwaysOnTop;
        _player.SetVolume(_settings.Volume);
        UpdateStatus();
        UpdateUi();
    }

    // ---------- Laufwerkssteuerung ----------

    private void Play_Click(object sender, RoutedEventArgs e) => _player.TogglePlay();
    private void Stop_Click(object sender, RoutedEventArgs e) => _player.Stop();
    private void Start_Click(object sender, RoutedEventArgs e) => _player.Seek(TimeSpan.Zero);
    private void SpeedDown_Click(object sender, RoutedEventArgs e) => _player.ActionDown(PlayerAction.SpeedDown, InputSource.Ui);
    private void SpeedUp_Click(object sender, RoutedEventArgs e) => _player.ActionDown(PlayerAction.SpeedUp, InputSource.Ui);
    private void SpeedNormal_Click(object sender, RoutedEventArgs e) => _player.SetSpeed(100);

    private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_updatingUi) _player.Seek(TimeSpan.FromSeconds(e.NewValue));
    }

    private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_updatingUi) _player.SetSpeed((int)Math.Round(e.NewValue));
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _player.SetVolume(e.NewValue);
        VolumeText.Text = $"{Math.Round(e.NewValue * 100)} %";
    }

    // ---------- Menü ----------

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Audiodateien hinzufügen",
            Multiselect = true,
            Filter = "Audiodateien|" + string.Join(";", AudioExtensions.Select(x => "*" + x)) + "|Alle Dateien|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        var added = AddFiles(dlg.FileNames);
        if (added.Count > 0) FileList.SelectedItem = added[0];
    }

    private void RemoveFiles_Click(object sender, RoutedEventArgs e) => RemoveSelected();

    private void ClearList_Click(object sender, RoutedEventArgs e)
    {
        if (_files.Count == 0) return;
        if (MessageBox.Show(this, "Alle Einträge aus der Liste entfernen? Die Dateien selbst werden nicht gelöscht.",
                "Transkription", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        FileList.SelectAll();
        RemoveSelected();
    }

    private void ShowInExplorer_Click(object sender, RoutedEventArgs e)
    {
        var item = FileList.SelectedItem as FileItem ?? _current;
        if (item == null || !File.Exists(item.Entry.Path)) return;
        Process.Start("explorer.exe", $"/select,\"{item.Entry.Path}\"");
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => ExitApplication();

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void HotkeysMenu_Click(object sender, RoutedEventArgs e)
    {
        _settings.HotkeysEnabled = HotkeysMenu.IsChecked;
        _settings.Save();
        ApplySettings();
    }

    private void TopmostMenu_Click(object sender, RoutedEventArgs e)
    {
        _settings.AlwaysOnTop = TopmostMenu.IsChecked;
        _settings.Save();
        ApplySettings();
    }

    private void Help_Click(object sender, RoutedEventArgs e) => new HelpWindow(_settings) { Owner = this }.ShowDialog();

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "";
        MessageBox.Show(this,
            $"Transkription {version}\n\nAbspielprogramm für Transkriptionen mit Steuerung per Fußschalter " +
            "und Tastenkürzeln – auch während in einem anderen Programm geschrieben wird.\n\n" +
            $"Einstellungen: {AppSettings.Folder}",
            "Über Transkription", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenSettings()
    {
        if (_settingsOpen) return;
        _settingsOpen = true;
        ApplySettings(); // Tasten und Pedal während des Anlernens nicht auslösen

        try
        {
            var dlg = new SettingsWindow(_settings.Clone(), _keys, _pedal) { Owner = IsVisible ? this : null };
            if (dlg.ShowDialog() == true)
            {
                var updated = dlg.Result;
                // Datei- und Fensterdaten können sich währenddessen geändert haben: aus dem laufenden Stand übernehmen.
                updated.Files = _settings.Files;
                updated.LastFile = _settings.LastFile;
                updated.SpeedPercent = _settings.SpeedPercent;
                updated.Volume = _settings.Volume;
                updated.TrayHintShown = _settings.TrayHintShown;
                _settings = updated;
                _settings.Save();
            }
        }
        finally
        {
            _settingsOpen = false;
            ApplySettings();
        }
    }

    // ---------- Fenster, Infobereich, Beenden ----------

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _settings.MinimizeToTray)
        {
            Hide();
            WindowState = WindowState.Normal;
            ShowTrayHint();
        }
    }

    private void ShowTrayHint()
    {
        if (_settings.TrayHintShown) return;
        _settings.TrayHintShown = true;
        _settings.Save();
        _tray?.ShowBalloon("Transkription läuft weiter",
            "Tasten und Fußschalter funktionieren auch im Hintergrund. Zum Öffnen auf das Symbol klicken, " +
            "zum Beenden Rechtsklick → Beenden.");
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_exiting && _settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            ShowTrayHint();
            return;
        }
        ShutdownApp();
    }

    public void ExitApplication()
    {
        _exiting = true;
        if (IsVisible) Close();
        else ShutdownApp();
    }

    private bool _shutDown;

    private void ShutdownApp()
    {
        if (_shutDown) return;
        _shutDown = true;
        _uiTimer.Stop();
        RememberPosition();
        SaveBounds();
        _settings.Save();
        _player.Unload();
        _keys.Dispose();
        _pedal?.Dispose();
        _tray?.Dispose();
        Application.Current.Shutdown();
    }

    private void RestoreWindowBounds()
    {
        if (_settings.WindowWidth is not { } w || _settings.WindowHeight is not { } h) return;
        if (_settings.WindowLeft is not { } l || _settings.WindowTop is not { } t) return;
        // Nur übernehmen, wenn das Fenster noch auf einem sichtbaren Bildschirmbereich liegt.
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!screen.IntersectsWith(new Rect(l, t, w, h))) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = l;
        Top = t;
        Width = Math.Max(MinWidth, w);
        Height = Math.Max(MinHeight, h);
    }

    private void SaveBounds()
    {
        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (b.IsEmpty || double.IsNaN(b.Left)) return;
        _settings.WindowLeft = b.Left;
        _settings.WindowTop = b.Top;
        _settings.WindowWidth = b.Width;
        _settings.WindowHeight = b.Height;
    }
}
