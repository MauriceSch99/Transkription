namespace Transkription.Core;

/// <summary>Funktionen, die einer Taste oder einem Pedal zugeordnet werden können.</summary>
public enum PlayerAction
{
    /// <summary>Pedal/Taste ohne Funktion.</summary>
    None,

    /// <summary>Spielt, solange gedrückt; beim Loslassen anhalten (mit Rücksprung).</summary>
    PlayHold,

    /// <summary>Jeder Druck schaltet zwischen Abspielen und Anhalten um.</summary>
    PlayToggle,

    Stop,

    /// <summary>Spult mit erhöhter Geschwindigkeit zurück, solange gedrückt.</summary>
    RewindHold,

    /// <summary>Spult mit erhöhter Geschwindigkeit vor, solange gedrückt.</summary>
    ForwardHold,

    /// <summary>Springt einmal kurz zurück.</summary>
    JumpBack,

    /// <summary>Springt einmal kurz vor.</summary>
    JumpForward,

    SpeedDown,
    SpeedUp,
    SpeedNormal,
    JumpToStart,
    InsertTimestamp,

    // Bezeichnungen aus Version 1.0; werden beim Laden der Einstellungen umgewandelt.
    Play,
    Rewind,
    FastForward,
}

public enum InputSource
{
    Keyboard,
    Pedal,
    Ui,
}

public static class PlayerActions
{
    /// <summary>Alle Funktionen, die in den Einstellungen gewählt werden können (ohne „keine“).</summary>
    public static readonly PlayerAction[] All =
    {
        PlayerAction.PlayHold,
        PlayerAction.PlayToggle,
        PlayerAction.Stop,
        PlayerAction.RewindHold,
        PlayerAction.ForwardHold,
        PlayerAction.JumpBack,
        PlayerAction.JumpForward,
        PlayerAction.SpeedDown,
        PlayerAction.SpeedUp,
        PlayerAction.SpeedNormal,
        PlayerAction.JumpToStart,
        PlayerAction.InsertTimestamp,
    };

    public static string DisplayName(PlayerAction action) => action switch
    {
        PlayerAction.None => "Keine Funktion",
        PlayerAction.PlayHold => "Abspielen, solange gedrückt",
        PlayerAction.PlayToggle => "Abspielen / Anhalten",
        PlayerAction.Stop => "Anhalten",
        PlayerAction.RewindHold => "Schnell zurückspulen, solange gedrückt",
        PlayerAction.ForwardHold => "Schnell vorspulen, solange gedrückt",
        PlayerAction.JumpBack => "Kurz zurückspringen",
        PlayerAction.JumpForward => "Kurz vorspringen",
        PlayerAction.SpeedDown => "Langsamer",
        PlayerAction.SpeedUp => "Schneller",
        PlayerAction.SpeedNormal => "Normale Geschwindigkeit",
        PlayerAction.JumpToStart => "Zum Anfang",
        PlayerAction.InsertTimestamp => "Zeitmarke eintippen",
        _ => action.ToString(),
    };
}

public static class TimeFormat
{
    public static string Long(TimeSpan t) =>
        $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 100}";

    public static string Short(TimeSpan t) =>
        $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
}
