namespace AutoEdit.Media;

public sealed class TimelineEvent
{
    /// <summary>
    /// Sökväg till källfilen (video).
    /// </summary>
    public required string SourceFilePath { get; init; }

    /// <summary>
    /// Starttid i källfilen (sekunder).
    /// </summary>
    public required double SourceStart { get; init; }

    /// <summary>
    /// Längd på klippet (sekunder).
    /// </summary>
    public required double Duration { get; init; }

    /// <summary>
    /// Starttid i den färdiga tidslinjen (sekunder).
    /// </summary>
    public required double TimelineStart { get; init; }
}
