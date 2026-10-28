using System.Text.Json;
using System.Text.Json.Serialization;
using Transkription.Core;

namespace Transkription.Settings;

/// <summary>Wie die Wiedergabe-Taste bzw. das Wiedergabe-Pedal reagiert.</summary>
public enum PlayMode
{
    /// <summary>Wiedergabe nur solange gedrückt gehalten wird (Standard bei Fußschaltern).</summary>
    Hold,
    /// <summary>Jeder Druck schaltet zwischen Wiedergabe und Pause um.</summary>
    Toggle,
}

/// <summary>Tastenkombination, die eine Aktion global (auch im Hintergrund) auslöst.</summary>
public sealed class KeyBinding
{
    public PlayerAction Action { get; set; }
    public int VirtualKey { get; set; }
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool Win { get; set; }
}

/// <summary>
/// Zuordnung eines Fußpedal-Knopfes zu einer Aktion. Ein Knopf wird über das Gerät
/// (Hersteller-/Produkt-ID bei USB bzw. COM-Anschluss bei seriellen Pedalen) und ein Bit im
/// Bericht identifiziert.
/// </summary>
public sealed class PedalBinding
{
    public PlayerAction Action { get; set; }
    public int VendorId { get; set; }
    public int ProductId { get; set; }

    /// <summary>COM-Anschluss bei seriellen Fußschaltern, sonst null.</summary>
    public string? Port { get; set; }

    public int ByteIndex { get; set; }
    public int Mask { get; set; }
    public string DeviceName { get; set; } = "";

    public string Describe()
    {
        var bit = 0;
        while (bit < 8 && (Mask & (1 << bit)) == 0) bit++;
        if (Port != null)
        {
            var line = Mask switch { 0x10 => "CTS", 0x20 => "DSR", 0x40 => "RI", 0x80 => "DCD", _ => $"Bit {bit}" };
            return $"Serieller Fußschalter {Port} – Leitung {line}";
        }
        var name = string.IsNullOrWhiteSpace(DeviceName) ? $"Gerät {VendorId:X4}:{ProductId:X4}" : DeviceName;
        return $"{name} – Knopf {ByteIndex * 8 + bit + 1}";
    }
}

public sealed class FileEntry
{
    public string Path { get; set; } = "";
    public double PositionSeconds { get; set; }
    public double? DurationSeconds { get; set; }
}

public sealed class AppSettings
{
    /// <summary>Sekunden, die beim Anhalten automatisch zurückgesprungen wird.</summary>
    public double BackstepSeconds { get; set; } = 1.0;

    /// <summary>Sekunden, die bei kurzem Antippen von Rück-/Vorspulen gesprungen wird.</summary>
    public double SkipSeconds { get; set; } = 2.0;

    /// <summary>Spulgeschwindigkeit beim Gedrückthalten (Vielfaches der Echtzeit).</summary>
    public double WindSpeed { get; set; } = 4.0;

    public int SpeedPercent { get; set; } = 100;
    public int SpeedStepPercent { get; set; } = 10;
    public double Volume { get; set; } = 0.8;

    public PlayMode KeyboardPlayMode { get; set; } = PlayMode.Toggle;
    public PlayMode PedalPlayMode { get; set; } = PlayMode.Hold;

    public bool HotkeysEnabled { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool AlwaysOnTop { get; set; }
    public bool RememberPositions { get; set; } = true;
    public bool TrayHintShown { get; set; }

    /// <summary>COM-Anschluss eines seriellen Fußschalters (leer = keiner).</summary>
    public string SerialPort { get; set; } = "";

    public List<KeyBinding> KeyBindings { get; set; } = DefaultKeyBindings();
    public List<PedalBinding> PedalBindings { get; set; } = new();

    public List<FileEntry> Files { get; set; } = new();
    public string? LastFile { get; set; }

    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }

    /// <summary>Belegung angelehnt an Express Scribe (F4 Stopp, F7 Rücklauf, F8 Vorlauf, F9 Wiedergabe).</summary>
    public static List<KeyBinding> DefaultKeyBindings() => new()
    {
        new() { Action = PlayerAction.Play, VirtualKey = 0x78 },        // F9
        new() { Action = PlayerAction.Stop, VirtualKey = 0x73 },        // F4
        new() { Action = PlayerAction.Rewind, VirtualKey = 0x76 },      // F7
        new() { Action = PlayerAction.FastForward, VirtualKey = 0x77 }, // F8
        new() { Action = PlayerAction.SpeedNormal, VirtualKey = 0x79 }, // F10
        new() { Action = PlayerAction.SpeedDown, VirtualKey = 0x7A },   // F11
        new() { Action = PlayerAction.SpeedUp, VirtualKey = 0x7B },     // F12
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Folder =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Transkription");

    private static string FilePath => System.IO.Path.Combine(Folder, "einstellungen.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
        }
        catch
        {
            // Beschädigte Einstellungen: mit Standardwerten weitermachen.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch
        {
            // Speichern ist nicht kritisch.
        }
    }

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;
}
