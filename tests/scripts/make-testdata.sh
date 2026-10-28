#!/usr/bin/env bash
# Erzeugt die MP3-Testdateien. Jede Sekunde k enthält einen Sinuston mit 300 + 100·k Hz,
# so lässt sich an der Tonhöhe ablesen, an welcher Stelle der Datei man sich befindet.
set -euo pipefail
out="$(dirname "$0")/../Transkription.Tests/TestData"
mkdir -p "$out"
tone="aevalsrc=0.5*sin(2*PI*(300+100*floor(t))*t):s=44100:d=20"
common=(-hide_banner -loglevel error -y -f lavfi -i "$tone" -metadata title="Testdiktat" -id3v2_version 3)
ffmpeg "${common[@]}" -ac 2 -ar 44100 -c:a libmp3lame -b:a 128k "$out/tones-44k-stereo-cbr.mp3"
ffmpeg "${common[@]}" -ac 1 -ar 22050 -c:a libmp3lame -q:a 5 "$out/tones-22k-mono-vbr.mp3"
ffmpeg "${common[@]}" -ac 1 -ar 16000 -c:a libmp3lame -b:a 32k "$out/tones-16k-mono.mp3"
ffmpeg "${common[@]}" -ac 1 -ar 8000 -c:a libmp3lame -b:a 16k "$out/tones-8k-mono.mp3"
