using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoEdit.Media;

public sealed class FfmpegRunner
{
    private readonly string _ffmpegPath;

    public FfmpegRunner(string ffmpegPath)
    {
        if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
            throw new FileNotFoundException("ffmpeg.exe hittades inte.", ffmpegPath);

        _ffmpegPath = ffmpegPath;
    }

    /// <summary>
    /// Kör FFmpeg och kastar fel om ExitCode != 0.
    /// </summary>
    public async Task RunAsync(string args, CancellationToken ct)
    {
        await RunGetOutputAsync(args, ct);
    }

    /// <summary>
    /// Kör FFmpeg och returnerar stderr (där FFmpeg loggar det mesta).
    /// </summary>
    public async Task<string> RunGetOutputAsync(string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = args,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8 // Viktigt för att läsa loggar korrekt
        };

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.Start();

        // Läs stdout asynkront (för att undvika blockering, även om vi ignorerar den här)
        var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
        
        // Läs stderr (där FFmpeg skriver info)
        var stderrTask = p.StandardError.ReadToEndAsync(ct);

        await p.WaitForExitAsync(ct);

        string stderr = await stderrTask;
        // invänta stdout också för att vara prydlig
        await stdoutTask; 

        if (p.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg misslyckades (ExitCode={p.ExitCode}).\n{stderr}");

        return stderr;
    }
}
