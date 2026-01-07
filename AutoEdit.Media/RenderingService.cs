using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoEdit.Media;

public sealed class RenderingService
{
    private readonly FfmpegRunner _ffmpeg;

    public RenderingService(FfmpegRunner ffmpeg)
    {
        _ffmpeg = ffmpeg;
    }

    /// <summary>
    /// Renderar video med standardinställningar (H.264, 30fps, 20Mbps).
    /// </summary>
    public Task RenderAsync(
        List<TimelineEvent> timeline,
        string? musicPath,
        string outputPath,
        IProgress<(int percent, string message)>? progress,
        CancellationToken ct)
    {
        return RenderAsync(timeline, musicPath, outputPath, new ExportSettings(), progress, ct);
    }

    /// <summary>
    /// Renderar video med anpassade exportinställningar.
    /// </summary>
    public async Task RenderAsync(
        List<TimelineEvent> timeline,
        string? musicPath,
        string outputPath,
        ExportSettings settings,
        IProgress<(int percent, string message)>? progress,
        CancellationToken ct)
    {
        if (timeline.Count == 0) return;

        // Skapa en temporär mapp för alla delklipp
        string tempDir = Path.Combine(Path.GetTempPath(), $"autoedit_render_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var segmentFiles = new List<string>();
        
        // Bestäm segment-filändelse baserat på format
        string segmentExt = settings.Format switch
        {
            ExportFormat.DNxHR_HQ or ExportFormat.DNxHR_SQ or ExportFormat.DNxHR_LB => ".mov",
            _ => ".mp4"
        };

        try
        {
            // Hämta encoder-inställningar från ExportSettings
            string videoEncoder = settings.GetVideoEncoderArgs();
            string videoFilter = settings.GetVideoFilterArgs();
            string decoderArgs = settings.GetDecoderArgs();
            
            int totalSegments = timeline.Count;
            string formatName = ExportSettings.FormatNames.GetValueOrDefault(settings.Format, "Video");
            
            for (int i = 0; i < totalSegments; i++)
            {
                ct.ThrowIfCancellationRequested();

                var evt = timeline[i];
                string segmentName = $"seg_{i:0000}{segmentExt}";
                string segmentPath = Path.Combine(tempDir, segmentName);
                segmentFiles.Add(segmentPath);

                int pct = (int)((double)i / totalSegments * 85);
                progress?.Report((pct, $"[{formatName}] Segment {i + 1}/{totalSegments}..."));

                // Bygg FFmpeg-argument
                string args = $"-y {decoderArgs} -ss {evt.SourceStart.ToString(CultureInfo.InvariantCulture)} " +
                              $"-t {evt.Duration.ToString(CultureInfo.InvariantCulture)} " +
                              $"-i \"{evt.SourceFilePath}\" " +
                              $"-vf \"{videoFilter}\" " +
                              $"{videoEncoder} -an " +
                              $"\"{segmentPath}\"";

                await _ffmpeg.RunAsync(args, ct);
            }

            // 2. Skapa concat-lista
            progress?.Report((88, "Concatenating segments..."));
            
            string listPath = Path.Combine(tempDir, "concat_list.txt");
            var sb = new StringBuilder();
            foreach (var f in segmentFiles)
            {
                // FFmpeg concat kräver forward slashes på Windows
                string ffmpegPath = f.Replace('\\', '/');
                sb.AppendLine($"file '{ffmpegPath}'");
            }
            await File.WriteAllTextAsync(listPath, sb.ToString(), ct);

             // 3. Slå ihop allt och lägg på musik (om tillgänglig)
            progress?.Report((92, "Finalizing..."));

            string finalVideoCodec = "-c:v copy";
            string finalArgs;
                       if (!string.IsNullOrWhiteSpace(musicPath) && File.Exists(musicPath))
            {
                // Med musik: mappa video från concat + ljud från musikfil
                string audioEncoder = settings.GetAudioEncoderArgs();
                finalArgs = $"-y -f concat -safe 0 -i \"{listPath.Replace('\\', '/')}\" " +
                            $"-i \"{musicPath}\" " +
                            $"-map 0:v -map 1:a " +
                            $"{finalVideoCodec} {audioEncoder} " +
                            $"{settings.GetMuxerArgs()} " +
                            $"-shortest " +
                            $"\"{outputPath}\"";
            }
            else
            {
                // Utan musik: behåll originalljud från videoklippen eller tyst
                // Vi kör utan audio mapping för enklast möjliga output
                finalArgs = $"-y -f concat -safe 0 -i \"{listPath.Replace('\\', '/')}\" " +
                            $"{finalVideoCodec} -an " +
                            $"{settings.GetMuxerArgs()} " +
                            $"\"{outputPath}\"";
            }

            await _ffmpeg.RunAsync(finalArgs, ct);

            progress?.Report((100, $"Done! Exported as {formatName}"));
        }
        finally
        {
            // Städa upp
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch 
            { 
                // Ignorera fel vid städning
            }
        }
    }
}
