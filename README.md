# Transkription

Ein Windows-Abspielprogramm für Transkriptionen, angelehnt an **Express Scribe**.
Audiodateien (MP3, WAV, WMA, M4A, AAC) lassen sich mit einem **USB-Fußschalter** oder
**Tastenkürzeln** abspielen, anhalten, zurück- und vorspulen. Das funktioniert auch, **während in
Word oder einem anderen Programm geschrieben wird**: Das Programm läuft im Hintergrund bzw. im
Infobereich neben der Uhr weiter.

## Funktionen

- **Fußschalter-Steuerung** für übliche USB-Fußschalter (z. B. Infinity IN-USB-1/2/3, Olympus
  RS-27/28/31, Philips ACC2310/2320). Die Pedale werden einmalig *angelernt*. Fußschalter, die sich
  als Tastatur ausgeben, werden einfach als Taste belegt.
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
- **Geschwindigkeit** zwischen 30 % und 300 %, **Lautstärke** einstellbar.
- **Dateiliste**, in der für jede Datei die Position gemerkt wird.
- **Zeitmarke eintippen** (frei belegbar): schreibt z. B. `[00:12:34]` in das aktive Programm.
- Läuft auf Wunsch im **Infobereich** weiter und kann **mit Windows starten**.

## Installation

Es ist keine Installation nötig: `Transkription.exe` herunterladen und starten.

- Unter *Actions → Windows-Build → neuester Lauf → Artifacts* liegt die `.exe` zum Download.
- Bei Versionen (Tags wie `v1.0.0`) liegt sie zusätzlich unter *Releases*.

Beim ersten Start kann Windows SmartScreen warnen, weil das Programm nicht signiert ist. In dem Fall
*Weitere Informationen → Trotzdem ausführen* wählen.

Voraussetzung: Windows 10 oder 11 (64 Bit). Bei Windows-„N“-Editionen muss das *Media Feature
Pack* installiert sein.

## Fußschalter einrichten

1. Fußschalter anschließen und Transkription starten.
2. *Einstellungen → Tasten & Fußschalter* öffnen.
3. Bei „Zurückspulen“ auf **Anlernen** klicken, das linke Pedal treten und loslassen (falls nötig
   ein zweites Mal). Danach dasselbe für „Wiedergabe / Pause“ (Mitte) und „Vorspulen“ (rechts).
4. Mit **OK** speichern.

Im Reiter *Geräte* sind alle erkannten USB-Eingabegeräte aufgelistet. Erscheint der Fußschalter
dort nicht, gibt er sich vermutlich als Tastatur aus. Dann bei der Aktion **Taste ändern** klicken
und das Pedal treten.

> Ein Programm, das „als Administrator“ läuft, lässt sich von Windows aus Sicherheitsgründen nicht
> fernsteuern. Schreibt man dort, muss Transkription ebenfalls als Administrator gestartet werden.

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
| `Input/KeyboardHook.cs` | Systemweiter Tastatur-Hook (Drücken und Loslassen) |
| `Input/FootPedal.cs` | USB-Fußschalter über Windows Raw Input, inklusive Anlernen |
| `Settings/AppSettings.cs` | Einstellungen, gespeichert unter `%APPDATA%\Transkription` |
| `MainWindow.xaml` | Hauptfenster mit Dateiliste und Laufwerkssteuerung |
| `Views/SettingsWindow.xaml` | Belegung von Tasten und Pedalen, Wiedergabe-Optionen |
