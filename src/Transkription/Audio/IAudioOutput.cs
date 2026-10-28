using NAudio.Wave;

namespace Transkription.Audio;

/// <summary>
/// Audioausgabe (unter Windows <see cref="WaveOutOutput"/>, in Tests eine Attrappe).
/// Eine Ausgabe wird für mehrere Wiedergaben (Play bis Pause) wiederverwendet.
/// </summary>
public interface IAudioOutput : IDisposable
{
    /// <summary>Beginnt eine neue Wiedergabe mit <paramref name="session"/> als Quelle.</summary>
    void Start(IWaveProvider session);

    /// <summary>Hält sofort an und verwirft gepufferte Daten. Kehrt erst zurück, wenn nichts mehr gelesen wird.</summary>
    void Stop();

    /// <summary>Anzahl der Bytes, die seit <see cref="Start"/> tatsächlich hörbar ausgegeben wurden.</summary>
    long PlayedBytes { get; }

    /// <summary>
    /// Eine Wiedergabe ist von selbst zu Ende gegangen (Quelle erschöpft) oder mit einem Fehler
    /// abgebrochen. Übergeben wird die Quelle der betroffenen Wiedergabe.
    /// </summary>
    event Action<IWaveProvider, Exception?>? PlaybackStopped;
}
