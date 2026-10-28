using System.Text.Json;
using System.Text.Json.Serialization;
using Transkription.Core;

namespace Transkription.Settings;

/// <summary>Nur noch zum Übernehmen der Einstellungen aus Version 1.0.</summary>
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

    /// <summary>False, solange für dieses Pedal noch kein Signal angelernt wurde.</summary>
    [JsonIgnore]
    public bool IsLearned => Mask != 0;

    public string Describe()
    {
        if (!IsLearned) return "noch nicht angelernt";
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

    /// <summary>Sprungweite von „Kurz zurück-/vorspringen“ in Sekunden.</summary>
    public double SkipSeconds { get; set; } = 3.0;

    /// <summary>Geschwindigkeit von „Schnell zurück-/vorspulen“ (Vielfaches der Echtzeit).</summary>
    public double WindSpeed { get; set; } = 4.0;

    public int SpeedPercent { get; set; } = 100;
    public int SpeedStepPercent { get; set; } = 10;
    public double Volume { get; set; } = 0.8;

    // Version 1.0: Halten/Umschalten war eine gemeinsame Einstellung statt eigener Funktionen.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PlayMode? KeyboardPlayMode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PlayMode? PedalPlayMode { get; set; }

    public bool HotkeysEnabled { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool AlwaysOnTop { get; set; }
    public bool RememberPositions { get; set; } = true;
    public bool TrayHintShown { get; set; }

    /// <summary>COM-Anschluss eines seriellen Fußschalters (leer = keiner).</summary>
    public string SerialPort { get; set; } = "";

    public List<KeyBinding> KeyBindings { get; set; } = DefaultKeyBindings();
    /// <summary>
    /// Die Pedale in ihrer Reihenfolge (Pedal 1, 2, …). Jedes Pedal hat eine frei wählbare
    /// Funktion; das Signal wird angelernt (bis dahin <see cref="PedalBinding.IsLearned"/> = false).
    /// </summary>
    public List<PedalBinding> PedalBindings { get; set; } = PedalPreset(PedalLayout.Hold);

    public List<FileEntry> Files { get; set; } = new();
    public string? LastFile { get; set; }

    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }

    /// <summary>Belegung angelehnt an Express Scribe (F4 Stopp, F7 Rücklauf, F8 Vorlauf, F9 Wiedergabe).</summary>
    public static List<KeyBinding> DefaultKeyBindings() => new()
    {
        new() { Action = PlayerAction.PlayToggle, VirtualKey = 0x78 },  // F9
        new() { Action = PlayerAction.Stop, VirtualKey = 0x73 },        // F4
        new() { Action = PlayerAction.RewindHold, VirtualKey = 0x76 },  // F7
        new() { Action = PlayerAction.ForwardHold, VirtualKey = 0x77 }, // F8
        new() { Action = PlayerAction.SpeedNormal, VirtualKey = 0x79 }, // F10
        new() { Action = PlayerAction.SpeedDown, VirtualKey = 0x7A },   // F11
        new() { Action = PlayerAction.SpeedUp, VirtualKey = 0x7B },     // F12
    };

    public enum PedalLayout
    {
        /// <summary>Wie bei Diktier-Fußschaltern üblich: Funktionen wirken, solange getreten wird.</summary>
        Hold,
        /// <summary>Abspielen/Anhalten per Tritt, kurze Sprünge vor und zurück.</summary>
        Toggle,
    }

    /// <summary>Funktionen der vier Pedale für die Voreinstellungen (Signale bleiben unberührt).</summary>
    public static PlayerAction[] PresetActions(PedalLayout layout) => layout switch
    {
        PedalLayout.Toggle => new[] { PlayerAction.JumpBack, PlayerAction.PlayToggle, PlayerAction.JumpForward, PlayerAction.Stop },
        _ => new[] { PlayerAction.RewindHold, PlayerAction.PlayHold, PlayerAction.ForwardHold, PlayerAction.JumpBack },
    };

    public static List<PedalBinding> PedalPreset(PedalLayout layout) =>
        PresetActions(layout).Select(a => new PedalBinding { Action = a }).ToList();

    /// <summary>Wandelt Einstellungen aus Version 1.0 um.</summary>
    private void Migrate()
    {
        PlayerAction Convert(PlayerAction a, PlayMode? mode, PlayMode fallback) => a switch
        {
            PlayerAction.Play => (mode ?? fallback) == PlayMode.Hold ? PlayerAction.PlayHold : PlayerAction.PlayToggle,
            PlayerAction.Rewind => PlayerAction.RewindHold,
            PlayerAction.FastForward => PlayerAction.ForwardHold,
            _ => a,
        };
        foreach (var k in KeyBindings) k.Action = Convert(k.Action, KeyboardPlayMode, PlayMode.Toggle);
        foreach (var p in PedalBindings) p.Action = Convert(p.Action, PedalPlayMode, PlayMode.Hold);
        KeyboardPlayMode = null;
        PedalPlayMode = null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Folder =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Transkription");

    private static string FilePath => System.IO.Path.Combine(Folder, "einstellungen.json");

    public static AppSettings FromJson(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        settings.Migrate();
        return settings;
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return FromJson(File.ReadAllText(FilePath));
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
