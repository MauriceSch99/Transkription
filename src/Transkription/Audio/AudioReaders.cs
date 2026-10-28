using NAudio.Wave;
using NLayer.NAudioSupport;

namespace Transkription.Audio;

public static class AudioReaders
{
    /// <summary>
    /// Wird für Formate verwendet, die nicht direkt gelesen werden können (z. B. M4A, WMA).
    /// Unter Windows ist das der Media-Foundation-Leser.
    /// </summary>
    public static Func<string, WaveStream>? Fallback { get; set; }

    /// <summary>Öffnet eine Audiodatei. Kann bei großen MP3-Dateien etwas dauern (Inhaltsverzeichnis wird aufgebaut).</summary>
    public static WaveStream Open(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            switch (ext)
            {
                case ".mp3":
                    // MP3 wird mit einem eigenen Decoder gelesen, damit keine Windows-Codecs nötig sind.
                    return new Mp3FileReaderBase(path, wf => new Mp3FrameDecompressor(wf));
                case ".wav":
                    var wav = new WaveFileReader(path);
                    if (wav.WaveFormat.Encoding is WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat or WaveFormatEncoding.Extensible)
                        return wav;
                    wav.Dispose();
                    break;
            }
        }
        catch when (Fallback != null)
        {
            // Mit dem Systemleser erneut versuchen.
        }

        if (Fallback == null) throw new NotSupportedException($"Das Dateiformat „{ext}“ wird nicht unterstützt.");
        return Fallback(path);
    }
}
