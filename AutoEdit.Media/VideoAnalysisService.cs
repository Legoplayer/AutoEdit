using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AutoEdit.Media;

public sealed class VideoAnalysisService
{
    private readonly FfmpegRunner _ffmpeg;
    private readonly FfprobeRunner _ffprobe;

    public VideoAnalysisService(FfmpegRunner ffmpeg, FfprobeRunner ffprobe)
    {
        _ffmpeg = ffmpeg;
        _ffprobe = ffprobe;
    }

    public async Task<VideoAnalysisResult> AnalyzeAsync(
        string videoPath,
        IProgress<(int percent, string message)>? progress,
        CancellationToken ct)
    {
        if (!File.Exists(videoPath))
            throw new FileNotFoundException("Videofilen hittades inte.", videoPath);

        // 1. Hämta metadata (längd, fps) med ffprobe
        progress?.Report((10, $"Hämtar metadata för {Path.GetFileName(videoPath)}..."));
        
        // Hämta duration
        string durationArgs = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"";
        string durationStr = await _ffprobe.RunAsync(durationArgs, ct);
        
        if (!double.TryParse(durationStr.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double duration))
            duration = 0.0;

        // Hämta framerate (avg_frame_rate, t.ex. "30000/1001" eller "25/1")
        string fpsArgs = $"-v error -select_streams v:0 -show_entries stream=r_frame_rate -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"";
        string fpsStr = await _ffprobe.RunAsync(fpsArgs, ct);
        double fps = ParseFps(fpsStr.Trim());

        // 2. Detektera scenbyten med ffmpeg
        progress?.Report((30, "Detekterar scener (kan ta tid)..."));

        // Filter: select='gt(scene,0.3)' väljer frames med scenförändring > 30%.
        // showinfo skriver info om varje vald frame till stderr (inklusive pts_time).
        // -f null - betyder kasta bort video-datat, vi vill bara ha loggen.
        // Vi analyserar max var 5:e frame för prestanda om vi vill, men här kör vi allt för noggrannhet.
        // För snabbare analys: lägg till "-r 10" före input för att analysera på nedsamplad fps.
        
        string sceneArgs = $"-i \"{videoPath}\" -vf \"select='gt(scene,0.3)',showinfo\" -f null -";
        
        // Här får vi all stderr som en stor sträng. För väldigt långa filmer vore streaming bättre, 
        // men för klipp (< några min) funkar detta.
        string logOutput = await _ffmpeg.RunGetOutputAsync(sceneArgs, ct);

        var sceneChanges = ParseSceneChanges(logOutput);

        progress?.Report((100, $"Klar. {sceneChanges.Count} scener hittade."));

        return new VideoAnalysisResult
        {
            FilePath = videoPath,
            DurationSeconds = duration,
            FrameRate = fps,
            SceneChanges = sceneChanges
        };
    }

    private static double ParseFps(string fpsString)
    {
        if (string.IsNullOrWhiteSpace(fpsString)) return 25.0; // Default

        var parts = fpsString.Split('/');
        if (parts.Length == 2)
        {
            if (double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double num) &&
                double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double den) &&
                den != 0)
            {
                return num / den;
            }
        }
        else if (double.TryParse(fpsString, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
        {
            return val;
        }

        return 25.0;
    }

    private static List<double> ParseSceneChanges(string logOutput)
    {
        var result = new List<double>();
        
        // showinfo output ser ut ungefär:
        // [Parsed_showinfo_1 @ ...] n:   0 pts:    128 pts_time:0.041667 ...
        
        // Regex för att hitta pts_time
        var regex = new Regex(@"pts_time:([\d\.]+)", RegexOptions.Compiled);
        
        foreach (Match m in regex.Matches(logOutput))
        {
            if (m.Success && m.Groups.Count > 1)
            {
                if (double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double time))
                {
                    result.Add(time);
                }
            }
        }
        
        return result;
    }
}
