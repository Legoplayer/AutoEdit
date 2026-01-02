using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoEdit.Media;

public sealed class FfprobeRunner
{
    private readonly string _ffprobePath;

    public FfprobeRunner(string ffprobePath)
    {
        if (string.IsNullOrWhiteSpace(ffprobePath) || !File.Exists(ffprobePath))
            throw new FileNotFoundException("ffprobe.exe hittades inte.", ffprobePath);

        _ffprobePath = ffprobePath;
    }

    /// <summary>
    /// Kör ffprobe med givna argument och returnerar stdout som sträng.
    /// </summary>
    public async Task<string> RunAsync(string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffprobePath,
            Arguments = args,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.Start();

        // Läs stdout (där ffprobe skriver JSON/data)
        var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
        // Läs stderr för felmeddelanden
        var stderrTask = p.StandardError.ReadToEndAsync(ct);

        await p.WaitForExitAsync(ct);

        string output = await stdoutTask;
        string error = await stderrTask;

        if (p.ExitCode != 0)
            throw new InvalidOperationException($"FFprobe misslyckades (ExitCode={p.ExitCode}).\n{error}");

        return output;
    }
}
