# FFmpeg Binaries

Denna mapp ska innehålla `ffmpeg.exe` och `ffprobe.exe` för Windows.

## Hämta FFmpeg

1. Gå till https://www.gyan.dev/ffmpeg/builds/
2. Ladda ner "ffmpeg-release-essentials.zip" (eller senaste versionen)
3. Extrahera zip-filen
4. Kopiera `ffmpeg.exe` och `ffprobe.exe` från `bin`-mappen till denna mapp (`AutoEdit.UI/ffmpeg/`)

Alternativt via winget (om installerat):
```powershell
winget install ffmpeg
# Sedan kopiera från C:\ffmpeg\bin\ till denna mapp
```

## Verifiering

Efter att ha lagt till filerna, verifiera att de finns:
- `AutoEdit.UI/ffmpeg/ffmpeg.exe`
- `AutoEdit.UI/ffmpeg/ffprobe.exe`

Dessa filer kommer automatiskt kopieras till build output vid kompilering.
