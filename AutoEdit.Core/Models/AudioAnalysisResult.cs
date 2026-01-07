namespace AutoEdit.Core;

public sealed class AudioAnalysisResult
{
    public required string SourcePath { get; init; }
    public required double DurationSeconds { get; init; }

    public required int SampleRate { get; init; }
    public required int HopSize { get; init; } // samples per hop

    public required double Bpm { get; init; }
    public required double BeatPeriodSeconds { get; init; }

    // onset/energy-kurva (1 värde per hop)
    public required float[] OnsetEnvelope { get; init; }

    // beat-tider i sekunder
    public required double[] BeatTimes { get; init; }
}
