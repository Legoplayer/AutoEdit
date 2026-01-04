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

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasPreview))]
        [NotifyPropertyChangedFor(nameof(PreviewSource))]
        private ClipItem? selectedClip;

        public bool HasPreview => SelectedClip != null;
        public Uri? PreviewSource => SelectedClip != null ? new Uri(SelectedClip.Path) : null;

        // För MediaElement kontroll från code-behind
        public Action<MediaElementAction>? MediaElementCallback { get; set; }

        public enum MediaElementAction
        {
            Play,
            Pause,
            Stop
        }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
        [NotifyCanExecuteChangedFor(nameof(RenderCommand))]
        private string? musicPath;

        [ObservableProperty] private bool isBusy;
        [ObservableProperty] private double progress;
        [ObservableProperty] private string progressText = "Idle";
        [ObservableProperty] private string statusText = "Ready";
        [ObservableProperty] private string logLine = "";

        // Timeline editor
        public ObservableCollection<TimelineEventViewModel> TimelineEvents { get; } = [];
        [ObservableProperty] private bool showTimelineEditor = false;
        [ObservableProperty] private TimelineEventViewModel? selectedTimelineEvent;

        // In/Out points (sekunder från början av färdig video)
        [ObservableProperty] private double inPoint = 0;
        [ObservableProperty] private double outPoint = 0;
        [ObservableProperty] private double totalTimelineDuration = 0;

        public ObservableCollection<string> Presets { get; } = new() { "Smooth", "Aggressive", "Cinematic" };
        [ObservableProperty] private string selectedPreset = "Cinematic";

        [ObservableProperty] private double aggressiveness = 60;
        [ObservableProperty] private double minClipSeconds = 0.6;
        [ObservableProperty] private double maxClipSeconds = 3.5;

        // Scenigenkänning - lägre värde = fler scener detekteras
        [ObservableProperty] private double sceneThreshold = 0.3;

        // Ljudskiftnings-detektion
        [ObservableProperty] private bool detectAudioChanges = false;
        [ObservableProperty] private double audioSensitivity = 0.5; // 0.1-1.0

        // Rendering alternativ
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
        private bool useMusic = true;

        [ObservableProperty] private bool includeOriginalAudioVersion = false;

        private CancellationTokenSource? _cts;
        private AudioAnalysisResult? _musicAnalysis;
        private List<TimelineEvent>? _timeline;

        [RelayCommand]
        private void PlayPause()
        {
            MediaElementCallback?.Invoke(MediaElementAction.Play);
        }

        [RelayCommand]
        private void StopPreview()
        {
            MediaElementCallback?.Invoke(MediaElementAction.Stop);
        }

        [RelayCommand]
        private void RemoveTimelineEvent(TimelineEventViewModel evt)
        {
            if (evt != null && TimelineEvents.Contains(evt))
            {
                TimelineEvents.Remove(evt);
                RecalculateTimeline();
            }
        }

        [RelayCommand]
        private void DuplicateTimelineEvent(TimelineEventViewModel evt)
        {
            if (evt != null)
            {
                var index = TimelineEvents.IndexOf(evt);
                var duplicate = new TimelineEventViewModel(new TimelineEvent
                {
                    SourceFilePath = evt.SourceFilePath,
                    SourceStart = evt.SourceStart,
                    Duration = evt.Duration,
                    TimelineStart = 0 // Will be recalculated
                });
                TimelineEvents.Insert(index + 1, duplicate);
                RecalculateTimeline();
            }
        }

        [RelayCommand]
        private void MoveTimelineEventUp(TimelineEventViewModel evt)
        {
            if (evt != null)
            {
                var index = TimelineEvents.IndexOf(evt);
                if (index > 0)
                {
                    TimelineEvents.Move(index, index - 1);
                    RecalculateTimeline();
                }
            }
        }

        [RelayCommand]
        private void MoveTimelineEventDown(TimelineEventViewModel evt)
        {
            if (evt != null)
            {
                var index = TimelineEvents.IndexOf(evt);
                if (index < TimelineEvents.Count - 1)
                {
                    TimelineEvents.Move(index, index + 1);
                    RecalculateTimeline();
                }
            }
        }

        private void RecalculateTimeline()
        {
            double currentTime = 0;
            foreach (var evt in TimelineEvents)
            {
                evt.TimelineStart = currentTime;
                currentTime += evt.Duration;
            }

            TotalTimelineDuration = currentTime;

            // Justera Out point om den är längre än nya längden
            if (OutPoint > TotalTimelineDuration)
                OutPoint = TotalTimelineDuration;
        }

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
                var clip = new ClipItem(path);

                // Försök hitta PotPlayer bookmarks
                var bookmarks = PotPlayerBookmarkParser.ParseBookmarkFile(path);
                if (bookmarks.Count > 0)
                {
                    clip.PotPlayerBookmarks = bookmarks;
                    clip.HasBookmarks = true;
                }

                Clips.Add(clip);
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
                // 1) Ljudanalys (endast om musik används)
                if (UseMusic && !string.IsNullOrWhiteSpace(MusicPath))
                {
                    var audioSvc = CreateAudioService();

                    var prog = new Progress<(int percent, string message)>(p =>
                    {
                        // Skala 0-35% för ljudanalysen
                        Progress = p.percent * 0.35;
                        StatusText = p.message;
                        LogLine = p.message;
                    });

                    _musicAnalysis = await audioSvc.AnalyzeAsync(MusicPath!, prog, _cts.Token);
                }
                else
                {
                    // Skapa en dummy-analys baserad på total videolängd
                    _musicAnalysis = null;
                    Progress = 35;
                }

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

                    // Använd nya parametrar för ljudskiftnings-detektion
                    clip.Analysis = await videoSvc.AnalyzeAsync(
                        clip.Path,
                        SceneThreshold,
                        DetectAudioChanges,
                        AudioSensitivity,
                        clipProg,
                        _cts.Token);

                    // Om PotPlayer bookmarks finns, lägg till dem som extra intressepunkter
                    if (clip.HasBookmarks && clip.PotPlayerBookmarks != null)
                    {
                        var allScenes = new List<double>(clip.Analysis.SceneChanges);
                        allScenes.AddRange(clip.PotPlayerBookmarks);
                        allScenes.Sort();

                        // Skapa ny VideoAnalysisResult med de kombinerade scenerna
                        clip.Analysis = new VideoAnalysisResult
                        {
                            FilePath = clip.Analysis.FilePath,
                            DurationSeconds = clip.Analysis.DurationSeconds,
                            FrameRate = clip.Analysis.FrameRate,
                            SceneChanges = allScenes.Distinct().ToList(),
                            AudioChanges = clip.Analysis.AudioChanges
                        };
                    }

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
                    Aggressiveness,
                    UseMusic);

                Progress = 100;
                StatusText = $"Timeline ready. {_timeline.Count} cuts created.";
                LogLine = StatusText;
                ProgressText = "Ready to render";

                // Populera timeline editor
                TimelineEvents.Clear();
                foreach (var evt in _timeline)
                {
                    TimelineEvents.Add(new TimelineEventViewModel(evt));
                }

                // Sätt In/Out till hela längden som default
                TotalTimelineDuration = _timeline.Sum(e => e.Duration);
                InPoint = 0;
                OutPoint = TotalTimelineDuration;

                // Visa timeline editor
                ShowTimelineEditor = true;
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

        private bool CanAnalyze() => !IsBusy && Clips.Count > 0 && (!UseMusic || !string.IsNullOrWhiteSpace(MusicPath));

        /// <summary>
        /// Renderar den färdiga videon med FFmpeg.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanRender))]
        private async Task RenderAsync()
        {
            if (TimelineEvents.Count == 0)
            {
                StatusText = "No timeline generated yet. Run Analyze first.";
                return;
            }

            // Bygg timeline från editor-events och applicera In/Out punkter
            var renderTimeline = BuildRenderTimeline();

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

                await renderer.RenderAsync(renderTimeline, UseMusic ? MusicPath : null, outputPath, IncludeOriginalAudioVersion, prog, _cts.Token);

                StatusText = "Render complete.";
                string savedFiles = IncludeOriginalAudioVersion && UseMusic
                    ? $"Saved to: {outputPath} (+ original audio version)"
                    : $"Saved to: {outputPath}";
                LogLine = savedFiles;
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

        private bool CanRender() => !IsBusy && ShowTimelineEditor && TimelineEvents.Count > 0;

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

        private List<TimelineEvent> BuildRenderTimeline()
        {
            var timeline = new List<TimelineEvent>();
            double currentTime = 0;

            foreach (var evt in TimelineEvents)
            {
                // Kolla om detta event ligger inom In/Out range
                double eventEnd = currentTime + evt.Duration;

                if (eventEnd <= InPoint)
                {
                    // Event är helt före In-punkten, skippa
                    currentTime += evt.Duration;
                    continue;
                }

                if (currentTime >= OutPoint)
                {
                    // Event är helt efter Out-punkten, sluta
                    break;
                }

                // Event överlappar In/Out range, klipp om nödvändigt
                double adjustedStart = evt.SourceStart;
                double adjustedDuration = evt.Duration;
                double adjustedTimelineStart = currentTime;

                if (currentTime < InPoint)
                {
                    // Event börjar före In-punkten, klipp början
                    double trimAmount = InPoint - currentTime;
                    adjustedStart += trimAmount;
                    adjustedDuration -= trimAmount;
                    adjustedTimelineStart = InPoint;
                }

                if (eventEnd > OutPoint)
                {
                    // Event slutar efter Out-punkten, klipp slutet
                    double trimAmount = eventEnd - OutPoint;
                    adjustedDuration -= trimAmount;
                }

                timeline.Add(new TimelineEvent
                {
                    SourceFilePath = evt.SourceFilePath,
                    SourceStart = adjustedStart,
                    Duration = adjustedDuration,
                    TimelineStart = adjustedTimelineStart - InPoint // Normalisera till 0
                });

                currentTime += evt.Duration;
            }

            return timeline;
        }
    }

    public partial class TimelineEventViewModel : ObservableObject
    {
        public TimelineEventViewModel(TimelineEvent evt)
        {
            SourceFilePath = evt.SourceFilePath;
            SourceStart = evt.SourceStart;
            Duration = evt.Duration;
            TimelineStart = evt.TimelineStart;
        }

        public string SourceFilePath { get; }
        public string FileName => System.IO.Path.GetFileName(SourceFilePath);

        [ObservableProperty] private double sourceStart;
        [ObservableProperty] private double duration;
        [ObservableProperty] private double timelineStart;

        public string DisplayText => $"{FileName} ({SourceStart:F1}s - {SourceStart + Duration:F1}s) = {Duration:F1}s";
        public string TimelinePosition => $"@ {TimelineStart:F1}s";
    }

    public sealed class ClipItem
    {
        public string Path { get; }
        public string FileName => System.IO.Path.GetFileName(Path);
        public VideoAnalysisResult? Analysis { get; set; }
        public List<double>? PotPlayerBookmarks { get; set; }
        public bool HasBookmarks { get; set; }

        public ClipItem(string path) => Path = path;
    }
}
