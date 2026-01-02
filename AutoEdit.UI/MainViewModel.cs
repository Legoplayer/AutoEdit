using AutoEdit.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AutoEdit.UI
{
    /// <summary>
    /// Huvudvy-modell för AutoEdit-applikationen.
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        public ObservableCollection<ClipItem> Clips { get; } = [];

        [ObservableProperty] private ClipItem? selectedClip;
        
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
        [NotifyCanExecuteChangedFor(nameof(RenderCommand))]
        private string? musicPath;

        [ObservableProperty] private bool isBusy;
        [ObservableProperty] private double progress;
        [ObservableProperty] private string progressText = "Idle";
        [ObservableProperty] private string statusText = "Ready";
        [ObservableProperty] private string logLine = "";

        public ObservableCollection<string> Presets { get; } = new() { "Smooth", "Aggressive", "Cinematic" };
        [ObservableProperty] private string selectedPreset = "Cinematic";

        [ObservableProperty] private double aggressiveness = 60;
        [ObservableProperty] private double minClipSeconds = 0.6;
        [ObservableProperty] private double maxClipSeconds = 3.5;

        private CancellationTokenSource? _cts;
        private AudioAnalysisResult? _musicAnalysis;
        private List<TimelineEvent>? _timeline;

        [RelayCommand]
        private void ImportClips()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select video clips",
                Filter = "Video|*.mp4;*.mov;*.mkv;*.avi;*.webm|All|*.*",
                Multiselect = true
            };

            if (dlg.ShowDialog() != true) return;

            foreach (var path in dlg.FileNames)
            {
                Clips.Add(new ClipItem(path));
            }

            StatusText = $"Loaded {dlg.FileNames.Length} clips.";
            LogLine = StatusText;
            
            // Uppdatera knappstatus
            AnalyzeCommand.NotifyCanExecuteChanged();
            RenderCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void ImportMusic()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select music",
                Filter = "Audio|*.mp3;*.wav;*.flac;*.m4a|All|*.*",
                Multiselect = false
            };

            if (dlg.ShowDialog() != true) return;

            MusicPath = dlg.FileName;
            StatusText = "Music loaded.";
            LogLine = $"{Path.GetFileName(MusicPath)}";
        }

        /// <summary>
        /// Analyserar musik och videoklipp för att skapa en redigeringsplan.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanAnalyze))]
        private async Task AnalyzeAsync()
        {
            _cts = new CancellationTokenSource();
            IsBusy = true;
            Progress = 0;
            ProgressText = "Analyzing...";

            try
            {
                // 1) Ljudanalys
                var audioSvc = CreateAudioService();

                var prog = new Progress<(int percent, string message)>(p =>
                {
                    // Skala 0-35% för ljudanalysen
                    Progress = p.percent * 0.35;
                    StatusText = p.message;
                    LogLine = p.message;
                });

                _musicAnalysis = await audioSvc.AnalyzeAsync(MusicPath!, prog, _cts.Token);

                // 2) Videoanalys (för varje klipp)
                var videoSvc = CreateVideoService();
                int clipCount = Clips.Count;
                int currentClip = 0;
                var analyzedVideos = new List<VideoAnalysisResult>();

                foreach (var clip in Clips)
                {
                    currentClip++;
                    // Skala 35-90% för videoanalysen
                    double baseProg = 35 + ((currentClip - 1) / (double)clipCount) * 55;
                    
                    var clipProg = new Progress<(int percent, string message)>(p =>
                    {
                        Progress = baseProg + (p.percent * 0.55 / clipCount);
                        StatusText = $"Clip {currentClip}/{clipCount}: {p.message}";
                        if (p.percent % 20 == 0) // Logga inte för ofta
                            LogLine = StatusText;
                    });

                    // Vi vill inte krascha hela pipelinen om en fil är korrupt, kanske?
                    // Men för nu kastar vi exception om det felar.
                    clip.Analysis = await videoSvc.AnalyzeAsync(clip.Path, clipProg, _cts.Token);
                    analyzedVideos.Add(clip.Analysis);
                }

                // 3) Redigeringsbeslut
                Progress = 95;
                StatusText = "Building timeline...";
                
                var builder = new TimelineBuilder();
                _timeline = builder.Build(
                    _musicAnalysis, 
                    analyzedVideos, 
                    MinClipSeconds, 
                    MaxClipSeconds, 
                    Aggressiveness);

                Progress = 100;
                StatusText = $"Timeline ready. {_timeline.Count} cuts created.";
                LogLine = StatusText;
                ProgressText = "Ready to render";
            }
            catch (OperationCanceledException)
            {
                StatusText = "Canceled.";
                LogLine = StatusText;
                ProgressText = "Canceled";
            }
            catch (Exception ex)
            {
                StatusText = "Error during analysis.";
                LogLine = ex.Message;
                ProgressText = "Error";
            }
            finally
            {
                IsBusy = false;
                _cts = null;
                AnalyzeCommand.NotifyCanExecuteChanged();
                RenderCommand.NotifyCanExecuteChanged();
            }
        }

        private bool CanAnalyze() => !IsBusy && Clips.Count > 0 && !string.IsNullOrWhiteSpace(MusicPath);

        /// <summary>
        /// Renderar den färdiga videon med FFmpeg.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanRender))]
        private async Task RenderAsync()
        {
            if (_timeline == null || _timeline.Count == 0)
            {
                StatusText = "No timeline generated yet. Run Analyze first.";
                return;
            }

            var saveDlg = new SaveFileDialog
            {
                Title = "Save Video",
                Filter = "MP4 Video|*.mp4",
                FileName = "AutoEdit_Output.mp4"
            };

            if (saveDlg.ShowDialog() != true) return;
            string outputPath = saveDlg.FileName;

            _cts = new CancellationTokenSource();
            IsBusy = true;
            Progress = 0;
            ProgressText = "Rendering...";

            try
            {
                var renderer = CreateRenderingService();

                var prog = new Progress<(int percent, string message)>(p =>
                {
                    Progress = p.percent;
                    StatusText = p.message;
                    LogLine = p.message;
                });

                await renderer.RenderAsync(_timeline, MusicPath!, outputPath, prog, _cts.Token);

                StatusText = "Render complete.";
                LogLine = $"Saved to: {outputPath}";
                ProgressText = "Done";
                
                // Öppna mappen (valfritt)
                // System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{outputPath}\"");
            }
            catch (OperationCanceledException)
            {
                StatusText = "Canceled.";
                LogLine = StatusText;
                ProgressText = "Canceled";
            }
            catch (Exception ex)
            {
                StatusText = "Error during render.";
                LogLine = ex.Message;
                ProgressText = "Error";
            }
            finally
            {
                IsBusy = false;
                _cts = null;
                AnalyzeCommand.NotifyCanExecuteChanged();
                RenderCommand.NotifyCanExecuteChanged();
            }
        }

        private bool CanRender() => !IsBusy && _timeline != null && _timeline.Count > 0;

        [RelayCommand]
        private void Cancel() => _cts?.Cancel();

        // Simulerar ett steg i en operation med förloppsindikering
        private async Task StepAsync(string text, double from, double to, CancellationToken ct)
        {
            StatusText = text;
            LogLine = text;

            const int steps = 25;
            for (int i = 1; i <= steps; i++)
            {
                ct.ThrowIfCancellationRequested();
                // Linjär interpolation från 'from' till 'to'
                Progress = from + (to - from) * (i / (double)steps);
                await Task.Delay(50, ct);
            }
        }

        private AudioAnalysisService CreateAudioService()
        {
            string ffmpegExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg.exe");
            var runner = new FfmpegRunner(ffmpegExe);
            return new AudioAnalysisService(runner);
        }

        private VideoAnalysisService CreateVideoService()
        {
            string baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");
            string ffmpegExe = Path.Combine(baseDir, "ffmpeg.exe");
            string ffprobeExe = Path.Combine(baseDir, "ffprobe.exe");
            
            var ffmpeg = new FfmpegRunner(ffmpegExe);
            var ffprobe = new FfprobeRunner(ffprobeExe);
            
            return new VideoAnalysisService(ffmpeg, ffprobe);
        }

        private RenderingService CreateRenderingService()
        {
            string ffmpegExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg.exe");
            var runner = new FfmpegRunner(ffmpegExe);
            return new RenderingService(runner);
        }
    }

    public sealed class ClipItem
    {
        public string Path { get; }
        public string FileName => System.IO.Path.GetFileName(Path);
        public VideoAnalysisResult? Analysis { get; set; }
        
        public ClipItem(string path) => Path = path;
    }
}
