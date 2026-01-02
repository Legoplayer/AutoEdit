using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AutoEdit.Media;

public sealed class AudioAnalysisService
{
    private readonly FfmpegRunner _ffmpeg;

    public AudioAnalysisService(FfmpegRunner ffmpeg)
    {
        _ffmpeg = ffmpeg;
    }

    /// <summary>
    /// Analyserar musik: onset-kurva, BPM, beat-tider.
    /// </summary>
    public async Task<AudioAnalysisResult> AnalyzeAsync(
        string inputAudioPath,
        IProgress<(int percent, string message)>? progress,
        CancellationToken ct)
    {
        if (!File.Exists(inputAudioPath))
            throw new FileNotFoundException("Ljudfilen hittades inte.", inputAudioPath);

        progress?.Report((0, "Konverterar ljud till WAV (mono/44.1k)..."));

        // 1) Konvertera till standard WAV (mono, 44100 Hz) så analys blir stabil
        string tempWav = Path.Combine(Path.GetTempPath(), $"autoedit_{Guid.NewGuid():N}.wav");

        // -vn: ingen video, -ac 1: mono, -ar 44100: sample rate, -f wav: wav
        string args = $"-y -i \"{inputAudioPath}\" -vn -ac 1 -ar 44100 -f wav \"{tempWav}\"";
        await _ffmpeg.RunAsync(args, ct);

        try
        {
            progress?.Report((10, "Läser PCM-samples..."));

            // 2) Läs samples
            using var reader = new WaveFileReader(tempWav);
            var sampleProvider = reader.ToSampleProvider(); // float samples -1..1

            int sampleRate = sampleProvider.WaveFormat.SampleRate;

            // Analys-parametrar (bra startvärden)
            int windowSize = 1024;
            int hopSize = 512;

            // 3) Bygg onset envelope (energi-derivata, rectifierad, smoothad)
            progress?.Report((20, "Beräknar onset-kurva..."));

            var onset = BuildOnsetEnvelope(sampleProvider, sampleRate, windowSize, hopSize, ct);

            // 4) BPM-estimat (autocorrelation på onset-kurvan)
            progress?.Report((55, "Skattar BPM..."));

            double bpm = EstimateBpm(onset, sampleRate, hopSize, minBpm: 70, maxBpm: 190);
            double beatPeriod = 60.0 / bpm;

            // 5) Beat tracking: gå igenom tid med period, “snappa” till lokala peaks
            progress?.Report((75, "Plockar ut beats..."));

            double durationSeconds = reader.TotalTime.TotalSeconds;
            var beats = TrackBeats(onset, sampleRate, hopSize, durationSeconds, beatPeriod);

            progress?.Report((100, $"Klar. BPM≈{bpm:0.0}, beats={beats.Length}"));

            return new AudioAnalysisResult
            {
                SourcePath = inputAudioPath,
                DurationSeconds = durationSeconds,
                SampleRate = sampleRate,
                HopSize = hopSize,
                Bpm = bpm,
                BeatPeriodSeconds = beatPeriod,
                OnsetEnvelope = onset,
                BeatTimes = beats
            };
        }
        finally
        {
            try { File.Delete(tempWav); } catch { /* ignore */ }
        }
    }

    private static float[] BuildOnsetEnvelope(
        ISampleProvider sampleProvider,
        int sampleRate,
        int windowSize,
        int hopSize,
        CancellationToken ct)
    {
        // Läs hela filen i block (streaming)
        // Vi beräknar RMS per window och tar diff mot föregående RMS.
        var window = new float[windowSize];
        var hop = new float[hopSize];

        float prevRms = 0f;
        var envelope = new List<float>(capacity: 1024);

        // Första: fyll initialt fönster
        int read = sampleProvider.Read(window, 0, window.Length);
        if (read == 0) return Array.Empty<float>();

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            float rms = Rms(window, read);
            float diff = rms - prevRms;

            // Half-wave rectify (bara ökningar)
            float onset = diff > 0 ? diff : 0;
            envelope.Add(onset);

            prevRms = rms;

            // Skjut fram med hopSize: flytta window och läs nya hop samples
            Array.Copy(window, hopSize, window, 0, windowSize - hopSize);

            int hopRead = sampleProvider.Read(hop, 0, hopSize);
            if (hopRead <= 0) break;

            Array.Copy(hop, 0, window, windowSize - hopSize, hopRead);

            // om hopRead < hopSize: fyll resten med 0 och avsluta efter detta varv
            if (hopRead < hopSize)
            {
                Array.Clear(window, windowSize - hopSize + hopRead, hopSize - hopRead);
                read = windowSize; // window är “full”
                // nästa loop kommer att läsa 0 och breaka
            }
            read = windowSize;
        }

        // Smooth: moving average (liten)
        var smoothed = MovingAverage(envelope.ToArray(), 6);

        // Normalisera till [0..1] (underlättar thresholds)
        NormalizeInPlace(smoothed);

        return smoothed;
    }

    private static float Rms(float[] buffer, int length)
    {
        double sum = 0;
        for (int i = 0; i < length; i++)
        {
            double v = buffer[i];
            sum += v * v;
        }
        return (float)Math.Sqrt(sum / Math.Max(1, length));
    }

    private static float[] MovingAverage(float[] x, int radius)
    {
        if (x.Length == 0) return x;
        float[] y = new float[x.Length];

        int w = radius * 2 + 1;
        // double acc = 0; // Removed unused variable

        // init
        // for (int i = 0; i < Math.Min(x.Length, w); i++) acc += x[i]; // Removed unused loop

        for (int i = 0; i < x.Length; i++)
        {
            int start = i - radius;
            int end = i + radius;

            // justera fönsterkanter
            if (start < 0 || end >= x.Length)
            {
                double local = 0;
                int count = 0;
                for (int j = Math.Max(0, start); j <= Math.Min(x.Length - 1, end); j++)
                {
                    local += x[j];
                    count++;
                }
                y[i] = (float)(local / Math.Max(1, count));
            }
            else
            {
                // snabb väg (valfri) – här håller vi det enkelt:
                double local = 0;
                for (int j = start; j <= end; j++) local += x[j];
                y[i] = (float)(local / w);
            }
        }

        return y;
    }

    private static void NormalizeInPlace(float[] x)
    {
        if (x.Length == 0) return;
        float max = 0;
        for (int i = 0; i < x.Length; i++) max = Math.Max(max, x[i]);
        if (max <= 1e-9f) return;
        for (int i = 0; i < x.Length; i++) x[i] /= max;
    }

    private static double EstimateBpm(float[] onset, int sampleRate, int hopSize, int minBpm, int maxBpm)
    {
        if (onset.Length < 10) return 120;

        // Autocorrelation på onset envelope
        int minLag = (int)Math.Round((60.0 / maxBpm) * (sampleRate / (double)hopSize));
        int maxLag = (int)Math.Round((60.0 / minBpm) * (sampleRate / (double)hopSize));

        minLag = Math.Max(1, minLag);
        maxLag = Math.Min(onset.Length - 1, maxLag);

        double bestScore = double.NegativeInfinity;
        int bestLag = (minLag + maxLag) / 2;

        // Liten “whitening”: subtrahera medel
        double mean = onset.Average(v => (double)v);

        for (int lag = minLag; lag <= maxLag; lag++)
        {
            double score = 0;
            for (int i = 0; i < onset.Length - lag; i++)
            {
                double a = onset[i] - mean;
                double b = onset[i + lag] - mean;
                score += a * b;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestLag = lag;
            }
        }

        double bpm = 60.0 * sampleRate / (hopSize * (double)bestLag);

        // Vanligt i musik: tempo-halvering/dubbling. Vi “foldar” till ett rimligt intervall.
        while (bpm < minBpm) bpm *= 2;
        while (bpm > maxBpm) bpm /= 2;

        return bpm;
    }

    private static double[] TrackBeats(float[] onset, int sampleRate, int hopSize, double durationSeconds, double beatPeriodSeconds)
    {
        if (onset.Length == 0) return Array.Empty<double>();

        double hopSeconds = hopSize / (double)sampleRate;

        // Vi vill hitta en rimlig start: ta största peak i första ~5 sek
        int searchFirst = (int)Math.Min(onset.Length - 1, Math.Round(5.0 / hopSeconds));
        int startIndex = ArgMax(onset, 0, searchFirst);
        double t = startIndex * hopSeconds;

        // Snap-fönster runt varje beat (sekunder)
        double snapWindow = 0.12; // 120 ms brukar funka okej
        int snapRadius = (int)Math.Round(snapWindow / hopSeconds);

        var beats = new List<double>(capacity: (int)(durationSeconds / beatPeriodSeconds) + 8);

        while (t < durationSeconds)
        {
            int expectedIndex = (int)Math.Round(t / hopSeconds);

            int a = Math.Max(0, expectedIndex - snapRadius);
            int b = Math.Min(onset.Length - 1, expectedIndex + snapRadius);

            int snapped = ArgMax(onset, a, b);
            double snappedTime = snapped * hopSeconds;

            // Enkel “safety”: undvik beats som är för nära varandra
            if (beats.Count == 0 || snappedTime - beats[^1] > beatPeriodSeconds * 0.5)
                beats.Add(snappedTime);

            t += beatPeriodSeconds;
        }

        return beats.ToArray();
    }

    private static int ArgMax(float[] x, int start, int endInclusive)
    {
        int best = start;
        float bestVal = float.NegativeInfinity;

        for (int i = start; i <= endInclusive; i++)
        {
            if (x[i] > bestVal)
            {
                bestVal = x[i];
                best = i;
            }
        }
        return best;
    }
}
