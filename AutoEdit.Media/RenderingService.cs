using System;
using System.Collections.Generic;
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

    public async Task RenderAsync(
        List<TimelineEvent> timeline,
        string musicPath,
        string outputPath,
        IProgress<(int percent, string message)>? progress,
        CancellationToken ct)
    {
        if (timeline.Count == 0) return;

        // Skapa en temporär mapp för alla delklipp
        string tempDir = Path.Combine(Path.GetTempPath(), $"autoedit_render_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var segmentFiles = new List<string>();

        try
        {
            // 1. Rendera varje segment till ett temporärt format (normaliserar formatet)
            // Vi siktar på 1920x1080, 30fps, ingen ljud på klippen (vi lägger på musik sen)
            
            int totalSegments = timeline.Count;
            
            for (int i = 0; i < totalSegments; i++)
            {
                ct.ThrowIfCancellationRequested();

                var evt = timeline[i];
                string segmentName = $"seg_{i:0000}.mp4";
                string segmentPath = Path.Combine(tempDir, segmentName);
                segmentFiles.Add(segmentPath);

                progress?.Report(((int)((double)i / totalSegments * 90), $"Rendering segment {i + 1}/{totalSegments}..."));

                // Filter för att normalisera video:
                // - scale: skala så det ryms i 1920x1080 (behåll aspect ratio)
                // - pad: fyll ut med svart till 1920x1080 (centrerat)
                // - setsar: sätt pixel aspect ratio till 1:1
                // - fps: tvinga 30 fps
                string vf = "scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1,fps=30";
                
                // -ss före -i för snabb sökning (kanske inte frame-perfect men snabbt)
                // -an: inget ljud från klippet
                // -preset ultrafast: snabb rendering (vi kan ändra till medium om vi vill ha mindre filer/bättre kvalitet)
                string args = $"-y -ss {evt.SourceStart.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                              $"-t {evt.Duration.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                              $"-i \"{evt.SourceFilePath}\" " +
                              $"-vf \"{vf}\" " +
                              $"-c:v libx264 -preset ultrafast -crf 23 -an " +
                              $"\"{segmentPath}\"";

                await _ffmpeg.RunAsync(args, ct);
            }

            // 2. Skapa concat-lista
            string listPath = Path.Combine(tempDir, "concat_list.txt");
            var sb = new StringBuilder();
            foreach (var f in segmentFiles)
            {
                sb.AppendLine($"file '{f}'");
            }
            await File.WriteAllTextAsync(listPath, sb.ToString(), ct);

            // 3. Slå ihop allt och lägg på musik
            progress?.Report((95, "Finalizing video..."));

            // -shortest: sluta när kortaste strömmen (video eller ljud) tar slut
            // (Eftersom vi byggde tidslinjen efter musiken borde de matcha bra, 
            // men om musiken är längre klipper vi videon när clipsen är slut, eller tvärtom)
            
            string finalArgs = $"-y -f concat -safe 0 -i \"{listPath}\" " +
                               $"-i \"{musicPath}\" " +
                               $"-map 0:v -map 1:a " +
                               $"-c:v copy -c:a aac -b:a 192k " +
                               $"-shortest " +
                               $"\"{outputPath}\"";

            await _ffmpeg.RunAsync(finalArgs, ct);

            progress?.Report((100, "Done!"));
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
