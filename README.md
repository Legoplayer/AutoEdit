# AutoEdit

AutoEdit är en WPF-applikation för automatisk videoredigering som synkroniserar klipp mot musikens rytm (beats) och/eller visuella klippunkter (scenbyten + PotPlayer-bokmärken).

## Snabbstart (Windows)

- Öppna `AutoEdit.slnx` i Visual Studio.
- Sätt `AutoEdit.UI` som startup-projekt och kör.

Applikationen levererar med `ffmpeg.exe`/`ffprobe.exe` via `AutoEdit.UI/ffmpeg` och kopierar dessa till output-katalogen vid build.

## Användning (översikt)

1. Importera videoklipp (drag & drop eller via dialog).
2. (Valfritt) Importera musik och välj `UseMusic`.
3. Kör `Analyze` för ljud-/videoanalys och automatisk tidslinje.
4. Kör `Render` och välj exportformat.

## Dokumentation

- `docs/SYSTEMDOKUMENTATION.md` – genomgående beskrivning av konstruktion, flöden och designval.
- `docs/ARCHITECTURE.md` – teknisk arkitektur + algoritmdesign (djupdykning).
- `docs/FUNCTIONS.md` – funktions-/API-referens per fil.