namespace Transkription.Core;

public enum PlayerAction
{
    Play,
    Stop,
    Rewind,
    FastForward,
    SpeedDown,
    SpeedUp,
    SpeedNormal,
    JumpToStart,
    InsertTimestamp,
}

public enum InputSource
{
    Keyboard,
    Pedal,
    Ui,
}

public static class PlayerActions
{
    public static readonly PlayerAction[] All = Enum.GetValues<PlayerAction>();

    public static string DisplayName(PlayerAction action) => action switch
    {
        PlayerAction.Play => "Wiedergabe / Pause",
        PlayerAction.Stop => "Stopp",
        PlayerAction.Rewind => "Zurückspulen",
        PlayerAction.FastForward => "Vorspulen",
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
