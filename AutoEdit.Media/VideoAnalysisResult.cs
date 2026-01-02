using System.Collections.Generic;

namespace AutoEdit.Media;

public sealed class VideoAnalysisResult
{
    public required string FilePath { get; init; }
    
    /// <summary>
    /// Videons längd i sekunder.
    /// </summary>
    public double DurationSeconds { get; init; }

    /// <summary>
    /// Bildfrekvens (FPS).
    /// </summary>
    public double FrameRate { get; init; }

    /// <summary>
    /// Tidsstämplar (i sekunder) där scenbyten upptäcktes.
    /// </summary>
    public List<double> SceneChanges { get; init; } = new();
}
