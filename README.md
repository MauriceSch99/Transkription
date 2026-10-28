# Transkription

Ein Windows-Abspielprogramm für Transkriptionen, angelehnt an **Express Scribe**.
Audiodateien (MP3, WAV, WMA, M4A, AAC) lassen sich mit einem **USB-Fußschalter** oder
**Tastenkürzeln** abspielen, anhalten, zurück- und vorspulen. Das funktioniert auch, **während in
Word oder einem anderen Programm geschrieben wird**: Das Programm läuft im Hintergrund bzw. im
Infobereich neben der Uhr weiter.

## Funktionen

- **Fußschalter-Steuerung** für USB-Fußschalter (z. B. Philips LFH/ACC 2310/2320/2330, Infinity
  IN-USB-1/2/3, Olympus RS-27/28/31) und für **ältere serielle Fußschalter** mit 9-poligem Stecker
  (z. B. Philips LFH 2210, auch über USB-Seriell-Adapter). Die Pedale werden einmalig *angelernt*,
  bis zu vier Pedale sind frei belegbar. Fußschalter, die sich als Tastatur ausgeben, werden als
  Taste belegt.
- **Systemweite Tastenkürzel**, die auch funktionieren, wenn ein anderes Fenster aktiv ist.
  Standardbelegung wie bei Express Scribe:

  | Taste | Aktion                   |
  |-------|--------------------------|
  | F9    | Wiedergabe / Pause       |
  | F4    | Stopp                    |
  | F7    | Zurückspulen             |
  | F8    | Vorspulen                |
  | F10   | Normale Geschwindigkeit  |
  | F11   | Langsamer                |
  | F12   | Schneller                |

- **Wiedergabe-Pedal halten**: Die Wiedergabe läuft, solange das Pedal getreten wird (alternativ
  schaltet jeder Tritt um).
- **Automatischer Rücksprung** beim Anhalten (Standard 1 Sekunde), damit der Anschluss nicht
  verloren geht.
- **Spulen**: Kurzes Antippen springt 2 Sekunden. Gedrückt halten spult mit vierfacher
  Geschwindigkeit. Danach geht die Wiedergabe automatisch weiter, wenn sie vorher lief.
- **Geschwindigkeit** zwischen 30 % und 300 % ohne Änderung der Tonhöhe, **Lautstärke** einstellbar.
- **MP3** wird mit einem eingebauten Decoder abgespielt, unabhängig von Windows Media Player oder
  installierten Codecs. Auch Diktate mit niedriger oder variabler Bitrate (8–48 kHz, Mono/Stereo)
  werden sauber und genau gesprungen.
- **Dateiliste**, in der für jede Datei die Position gemerkt wird.
- **Zeitmarke eintippen** (frei belegbar): schreibt z. B. `[00:12:34]` in das aktive Programm.
- Läuft auf Wunsch im **Infobereich** weiter und kann **mit Windows starten**.

## Installation

Es ist keine Installation nötig: `Transkription.exe` herunterladen und starten.

- Unter *Actions → Windows-Build → neuester Lauf → Artifacts* liegt die `.exe` zum Download.
- Bei Versionen (Tags wie `v1.0.0`) liegt sie zusätzlich unter *Releases*.

Beim ersten Start kann Windows SmartScreen warnen, weil das Programm nicht signiert ist. In dem Fall
*Weitere Informationen → Trotzdem ausführen* wählen.

Voraussetzung: Windows 10 oder 11 (64 Bit). MP3 und WAV funktionieren immer. Für M4A, WMA und AAC
wird Windows Media Foundation verwendet (bei Windows-„N“-Editionen das *Media Feature Pack*
nachinstallieren).

## Fußschalter einrichten

1. Fußschalter anschließen und Transkription starten. Läuft noch ein anderes Diktatprogramm
   (z. B. Philips SpeechExec oder Express Scribe), dieses vorher beenden.
2. **Nur bei seriellem Stecker:** *Einstellungen → Geräte* öffnen und den COM-Anschluss wählen.
   USB-Fußschalter brauchen hier keine Einstellung.
3. *Einstellungen → Tasten & Fußschalter* öffnen.
4. Bei „Zurückspulen“ auf **Anlernen** klicken, das linke Pedal treten und loslassen (falls nötig
   ein zweites Mal). Danach dasselbe für „Wiedergabe / Pause“ (Mitte) und „Vorspulen“ (rechts).
   Das vierte Pedal kann z. B. mit „Stopp“, „Zum Anfang“ oder „Zeitmarke eintippen“ belegt werden.
5. Mit **OK** speichern.

Im Reiter *Geräte* sind alle erkannten USB-Eingabegeräte aufgelistet (Philips-Geräte haben meist
die VID 0911). Beim Treten eines Pedals zeigt „Letztes Signal“, von welchem Gerät es kommt.
Erscheint der Fußschalter nicht, gibt er sich vermutlich als Tastatur aus. Dann bei der Aktion
**Taste ändern** klicken und das Pedal treten.

> Ein Programm, das „als Administrator“ läuft, lässt sich von Windows aus Sicherheitsgründen nicht
> fernsteuern. Schreibt man dort, muss Transkription ebenfalls als Administrator gestartet werden.

## Tests

Die von Windows unabhängigen Teile werden automatisch getestet, auch bei jedem Build auf GitHub.
Dazu gehören MP3-Wiedergabe, Sprunggenauigkeit, Tempo, Dateiende, Bedienlogik (Pedal halten,
Rücksprung, Spulen) sowie das Anlernen und Auswerten von Fußschaltern. Die Testdateien in
`tests/Transkription.Tests/TestData` enthalten pro Sekunde einen anderen Ton, so lässt sich genau
nachmessen, was zu hören ist. Erzeugt werden sie mit `tests/scripts/make-testdata.sh` (ffmpeg).

```bash
dotnet test tests/Transkription.Tests
```

## Selbst bauen

Voraussetzung ist das [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet publish src/Transkription/Transkription.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

Die fertige Datei liegt danach unter `publish/Transkription.exe`. Zum Entwickeln kann
`Transkription.sln` in Visual Studio 2022 geöffnet werden.

## Aufbau

| Datei | Inhalt |
|-------|--------|
| `Core/PlayerController.cs` | Wiedergabelogik: Halten, Rücksprung, Spulen, Geschwindigkeit |
| `Audio/AudioEngine.cs` | MP3-Wiedergabe (NAudio, NLayer), Tempo ohne Tonhöhenänderung (SoundTouch) |
| `Input/KeyboardHook.cs`, `Input/HotkeyMatcher.cs` | Systemweite Tastenkürzel (Drücken und Loslassen) |
| `Input/FootPedal.cs` | USB-Fußschalter über Windows Raw Input |
| `Input/SerialPedal.cs` | Serielle Fußschalter (Leitungen CTS, DSR, RI, DCD) |
| `Input/PedalDecoder.cs` | Anlernen und Auswerten der Pedale |
| `Settings/AppSettings.cs` | Einstellungen, gespeichert unter `%APPDATA%\Transkription` |
| `MainWindow.xaml` | Hauptfenster mit Dateiliste und Laufwerkssteuerung |
| `Views/SettingsWindow.xaml` | Belegung von Tasten und Pedalen, Wiedergabe-Optionen |

## Verwendete Bibliotheken

[NAudio](https://github.com/naudio/NAudio) (MIT), [NLayer](https://github.com/naudio/NLayer) (MIT)
und [SoundTouch.Net](https://github.com/owoudenberg/soundtouch.net) (LGPL-2.1).
