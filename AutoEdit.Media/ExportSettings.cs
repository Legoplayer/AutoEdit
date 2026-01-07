namespace AutoEdit.Media;

/// <summary>
/// Exportformat för video-rendering.
/// </summary>
public enum ExportFormat
{
    /// <summary>
    /// H.264 (libx264) - Standard CPU-rendering, bred kompatibilitet.
    /// </summary>
    H264,
    
    /// <summary>
    /// H.264 med NVIDIA NVENC - GPU-accelererad, mycket snabb.
    /// </summary>
    H264_NVENC,
    
    /// <summary>
    /// H.265/HEVC (libx265) - Bättre kompression, långsammare.
    /// </summary>
    H265,
    
    /// <summary>
    /// H.265/HEVC med NVIDIA NVENC - GPU-accelererad HEVC.
    /// </summary>
    HEVC_NVENC,
    
    /// <summary>
    /// DNxHR HQ - Professionellt redigeringsformat (Avid-kompatibelt).
    /// </summary>
    DNxHR_HQ,
    
    /// <summary>
    /// DNxHR SQ - Professionellt format, standard kvalitet.
    /// </summary>
    DNxHR_SQ,
    
    /// <summary>
    /// DNxHR LB - Professionellt format, låg bandbredd (proxy).
    /// </summary>
    DNxHR_LB
}

/// <summary>
/// Inställningar för video-export.
/// </summary>
public sealed class ExportSettings
{
    /// <summary>
    /// Exportformat (codec).
    /// </summary>
    public ExportFormat Format { get; init; } = ExportFormat.H264;
    
    /// <summary>
    /// Bildfrekvens (frames per sekund).
    /// </summary>
    public double Fps { get; init; } = 30;
    
    /// <summary>
    /// Video-bitrate i Mbps (för lossy-format).
    /// Ignoreras för DNxHR.
    /// </summary>
    public double BitrateMbps { get; init; } = 20;
    
    /// <summary>
    /// Ljudbitrate i kbps.
    /// </summary>
    public int AudioBitrateKbps { get; init; } = 320;
    
    /// <summary>
    /// Output-bredd i pixlar (0 = auto baserat på höjd).
    /// </summary>
    public int Width { get; init; } = 1920;
    
    /// <summary>
    /// Output-höjd i pixlar.
    /// </summary>
    public int Height { get; init; } = 1080;
    
    /// <summary>
    /// Hämtar filändelse baserat på format.
    /// </summary>
    public string FileExtension => Format switch
    {
        ExportFormat.DNxHR_HQ or ExportFormat.DNxHR_SQ or ExportFormat.DNxHR_LB => ".mov",
        _ => ".mp4"
    };
    
    /// <summary>
    /// Hämtar FFmpeg video encoder-argument.
    /// </summary>
    public string GetVideoEncoderArgs()
    {
        int bitrateKbps = (int)(BitrateMbps * 1000);
        
        return Format switch
        {
            ExportFormat.H264 => $"-c:v libx264 -preset fast -b:v {bitrateKbps}k -maxrate {bitrateKbps * 1.5:F0}k -bufsize {bitrateKbps * 2}k",
            
            ExportFormat.H264_NVENC => $"-c:v h264_nvenc -preset p3 -b:v {bitrateKbps}k -maxrate {bitrateKbps * 1.5:F0}k -bufsize {bitrateKbps * 2}k -rc vbr",
            
            ExportFormat.H265 => $"-c:v libx265 -preset fast -b:v {bitrateKbps}k -maxrate {bitrateKbps * 1.5:F0}k -bufsize {bitrateKbps * 2}k -tag:v hvc1",
            
            ExportFormat.HEVC_NVENC => $"-c:v hevc_nvenc -preset p3 -b:v {bitrateKbps}k -maxrate {bitrateKbps * 1.5:F0}k -bufsize {bitrateKbps * 2}k -rc vbr -tag:v hvc1",
            
            ExportFormat.DNxHR_HQ => "-c:v dnxhd -profile:v dnxhr_hq",
            ExportFormat.DNxHR_SQ => "-c:v dnxhd -profile:v dnxhr_sq",
            ExportFormat.DNxHR_LB => "-c:v dnxhd -profile:v dnxhr_lb",
            
            _ => "-c:v libx264 -preset medium -crf 18"
        };
    }
    
    /// <summary>
    /// Hämtar FFmpeg video filter-sträng.
    /// </summary>
    public string GetVideoFilterArgs()
    {
        string fpsFilter = $"fps={Fps.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        
        // DNxHR kräver specifika pixelformat
        string pixFmt = Format switch
        {
            ExportFormat.DNxHR_HQ or ExportFormat.DNxHR_SQ or ExportFormat.DNxHR_LB => ",format=yuv422p10le",
            _ => ""
        };
        
        return $"scale={Width}:{Height}:force_original_aspect_ratio=decrease,pad={Width}:{Height}:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1,{fpsFilter}{pixFmt}";
    }
    
    /// <summary>
    /// Hämtar FFmpeg ljud encoder-argument.
    /// </summary>
    public string GetAudioEncoderArgs()
    {
        // DNxHR i MOV-container fungerar bra med PCM
        if (Format is ExportFormat.DNxHR_HQ or ExportFormat.DNxHR_SQ or ExportFormat.DNxHR_LB)
        {
            return "-c:a pcm_s16le";
        }
        
        return $"-c:a aac -b:a {AudioBitrateKbps}k";
    }

    /// <summary>
    /// Aktiverar hårdvaruacceleration för decode om det stöds.
    /// </summary>
    public string GetDecoderArgs() =>
        Format is ExportFormat.H264_NVENC or ExportFormat.HEVC_NVENC
            ? "-hwaccel auto"
            : string.Empty;

    /// <summary>
    /// Muxer-flaggor för snabbare uppspelning efter export.
    /// </summary>
    public string GetMuxerArgs() =>
        Format switch
        {
            ExportFormat.DNxHR_HQ or ExportFormat.DNxHR_SQ or ExportFormat.DNxHR_LB => string.Empty,
            _ => "-movflags +faststart"
        };
    
    /// <summary>
    /// Preset-namn för UI.
    /// </summary>
    public static readonly Dictionary<ExportFormat, string> FormatNames = new()
    {
        { ExportFormat.H264, "H.264 (CPU)" },
        { ExportFormat.H264_NVENC, "H.264 NVENC (GPU)" },
        { ExportFormat.H265, "H.265/HEVC (CPU)" },
        { ExportFormat.HEVC_NVENC, "HEVC NVENC (GPU)" },
        { ExportFormat.DNxHR_HQ, "DNxHR HQ (Lossless Edit)" },
        { ExportFormat.DNxHR_SQ, "DNxHR SQ (Standard)" },
        { ExportFormat.DNxHR_LB, "DNxHR LB (Proxy)" }
    };
    
    /// <summary>
    /// Preset-beskrivningar för UI.
    /// </summary>
    public static readonly Dictionary<ExportFormat, string> FormatDescriptions = new()
    {
        { ExportFormat.H264, "Bred kompatibilitet, bra kvalitet. ~100 MB/min vid 20 Mbps." },
        { ExportFormat.H264_NVENC, "Snabb GPU-rendering med NVIDIA. Kräver NVIDIA-grafikkort." },
        { ExportFormat.H265, "50% mindre filer än H.264, långsammare encoding." },
        { ExportFormat.HEVC_NVENC, "Snabb HEVC med NVIDIA GPU. Kräver GTX 1000+ eller nyare." },
        { ExportFormat.DNxHR_HQ, "Professionellt format för vidare redigering. ~400 MB/min." },
        { ExportFormat.DNxHR_SQ, "DNxHR standard kvalitet. ~200 MB/min." },
        { ExportFormat.DNxHR_LB, "DNxHR proxy-kvalitet för snabb redigering. ~40 MB/min." }
    };
    
    /// <summary>
    /// Standard-presets för vanliga användningsfall.
    /// </summary>
    public static ExportSettings QuickExport => new()
    {
        Format = ExportFormat.H264_NVENC,
        Fps = 30,
        BitrateMbps = 15,
        AudioBitrateKbps = 192
    };
    
    public static ExportSettings HighQuality => new()
    {
        Format = ExportFormat.H264,
        Fps = 60,
        BitrateMbps = 50,
        AudioBitrateKbps = 320
    };
    
    public static ExportSettings EditingMaster => new()
    {
        Format = ExportFormat.DNxHR_HQ,
        Fps = 30,
        BitrateMbps = 0, // Ignoreras för DNxHR
        AudioBitrateKbps = 0 // PCM används
    };
}
