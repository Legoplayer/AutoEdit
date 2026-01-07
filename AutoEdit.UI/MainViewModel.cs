using AutoEdit.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Globalization;
using System.Linq;

namespace AutoEdit.UI
{
    /// <summary>
    /// Huvudvy-modell för AutoEdit-applikationen.
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        public ObservableCollection<ClipItem> Clips { get; } = [];
        public ObservableCollection<TimelineSegment> TimelineSegments { get; } = [];

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasClips))]
        [NotifyPropertyChangedFor(nameof(HasSelectedClip))]
        private ClipItem? selectedClip;

        public bool HasClips => Clips.Count > 0;
        public bool HasSelectedClip => SelectedClip != null;
        public bool HasTimeline => TimelineSegments.Count > 0;

        partial void OnSelectedClipChanged(ClipItem? value)
        {
            if (value != null)
            {
                PreviewSource = new Uri(value.Path);
                HasPreview = true;
                NewBookmarkTime = "";
            }
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
        [ObservableProperty] private string newBookmarkTime = "";


        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
        private bool useMusic = true;

        // ===== EXPORT SETTINGS =====
        public ObservableCollection<ExportFormatItem> ExportFormats { get; } =
        [
            new(ExportFormat.H264, "H.264 (CPU)", "Bred kompatibilitet, bra kvalitet"),
            new(ExportFormat.H264_NVENC, "H.264 NVENC (GPU)", "Snabb GPU-rendering, kräver NVIDIA"),
            new(ExportFormat.H265, "H.265/HEVC (CPU)", "50% mindre filer, långsammare"),
            new(ExportFormat.HEVC_NVENC, "HEVC NVENC (GPU)", "Snabb HEVC, kräver GTX 1000+"),
            new(ExportFormat.DNxHR_HQ, "DNxHR HQ (Lossless)", "Professionellt, ~400 MB/min"),
            new(ExportFormat.DNxHR_SQ, "DNxHR SQ (Standard)", "Professionellt, ~200 MB/min"),
            new(ExportFormat.DNxHR_LB, "DNxHR LB (Proxy)", "Snabb redigering, ~40 MB/min"),
        ];

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowBitrateSettings))]
        [NotifyPropertyChangedFor(nameof(ExportFileExtension))]
        private ExportFormatItem selectedExportFormat;

        public bool ShowBitrateSettings => SelectedExportFormat?.Format is not (
            ExportFormat.DNxHR_HQ or ExportFormat.DNxHR_SQ or ExportFormat.DNxHR_LB);

        public string ExportFileExtension => SelectedExportFormat?.Format switch
        {
            ExportFormat.DNxHR_HQ or ExportFormat.DNxHR_SQ or ExportFormat.DNxHR_LB => ".mov",
            _ => ".mp4"
        };

        public ObservableCollection<FpsOption> FpsOptions { get; } =
        [
            new(24, "24 fps (Film)"),
            new(25, "25 fps (PAL)"),
            new(30, "30 fps (Standard)"),
            new(50, "50 fps (PAL HFR)"),
            new(60, "60 fps (Smooth)"),
        ];

        [ObservableProperty] private FpsOption selectedFps;

        public ObservableCollection<BitrateOption> BitrateOptions { get; } =
        [
            new(8, "8 Mbps (Web/Mobile)"),
            new(15, "15 Mbps (YouTube 1080p)"),
            new(20, "20 Mbps (Standard)"),
            new(35, "35 Mbps (High Quality)"),
            new(50, "50 Mbps (Very High)"),
            new(80, "80 Mbps (Master)"),
            new(100, "100 Mbps (Archive)"),
        ];

        [ObservableProperty] private BitrateOption selectedBitrate;

        [ObservableProperty] private bool showAdvancedExportSettings = false;

        [RelayCommand]
        private void ToggleAdvancedExportSettings() => ShowAdvancedExportSettings = !ShowAdvancedExportSettings;

        [ObservableProperty] private bool includeOriginalAudioVersion = false;

        // Preview player
        [ObservableProperty] private Uri? previewSource;
        [ObservableProperty] private bool hasPreview = false;

        // Rendered output video
        [ObservableProperty] private Uri? renderedVideoSource;
        [ObservableProperty] private bool hasRenderedVideo = false;
        [ObservableProperty] private string? renderedVideoPath;

        // MediaElement callbacks
        public Action<MediaElementAction>? MediaElementCallback { get; set; }
        public Action<string, double, double>? SeekToCallback { get; set; }

        [ObservableProperty] private TimelineSegment? selectedTimelineSegment;

        public enum MediaElementAction
        {
            Play,
            Pause,
            Stop
        }

        public ObservableCollection<ThemeOption> Themes { get; } =
        [
            new("Nebula", AppTheme.Nebula),
            new("Solstice", AppTheme.Solstice),
            new("Graphite", AppTheme.Graphite)
        ];

        [ObservableProperty] private ThemeOption? selectedTheme;

        partial void OnSelectedThemeChanged(ThemeOption? value)
        {
            if (value == null)
                return;

            ThemeManager.ApplyTheme(value.Theme);
        }

        [ObservableProperty] private double aggressiveness = 60;
        [ObservableProperty] private double minClipSeconds = 0.6;
        [ObservableProperty] private double maxClipSeconds = 3.5;

        private CancellationTokenSource? _cts;
        private AudioAnalysisResult? _musicAnalysis;
        private List<TimelineEvent>? _timeline;

        public MainViewModel()
        {
            // Sätt standardvärden för export
            selectedExportFormat = ExportFormats[0]; // H.264
            selectedFps = FpsOptions[2]; // 30 fps
            selectedBitrate = BitrateOptions[2]; // 20 Mbps
            SelectedTheme = Themes[0];

            // Uppdatera HasClips när samlingen ändras
            Clips.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasClips));
            TimelineSegments.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasTimeline));
        }

        [RelayCommand]
        private void RemoveClip(ClipItem? clip)
        {
            if (clip != null && Clips.Contains(clip))
            {
                Clips.Remove(clip);
                StatusText = $"Removed {clip.FileName}";
                LogLine = StatusText;

                // Uppdatera knappstatus
                AnalyzeCommand.NotifyCanExecuteChanged();
                RenderCommand.NotifyCanExecuteChanged();
            }
        }

        [RelayCommand]
        private void ClearAllClips()
        {
            int count = Clips.Count;
            Clips.Clear();
            StatusText = $"Removed {count} clips";
            LogLine = StatusText;

            // Uppdatera knappstatus
            AnalyzeCommand.NotifyCanExecuteChanged();
            RenderCommand.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// Lägg till filer (används av drag & drop).
        /// </summary>
        public void AddFiles(string[] filePaths)
        {
            var videoExtensions = new[] { ".mp4", ".mov", ".mkv", ".avi", ".webm", ".wmv", ".flv", ".m4v" };
            int addedCount = 0;

            foreach (var path in filePaths)
            {
                string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                if (videoExtensions.Contains(ext) && File.Exists(path))
                {
                    // Kontrollera att filen inte redan finns i listan
                    if (!Clips.Any(c => c.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                    {
                        Clips.Add(new ClipItem(path));
                        addedCount++;
                    }
                }
            }

            if (addedCount > 0)
            {
                StatusText = $"Added {addedCount} clip(s)";
                LogLine = StatusText;

                // Uppdatera knappstatus
                AnalyzeCommand.NotifyCanExecuteChanged();
                RenderCommand.NotifyCanExecuteChanged();
            }
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

        [RelayCommand]
        private void AddBookmark()
        {
            if (SelectedClip == null)
            {
                StatusText = "Select a clip to add bookmarks.";
                LogLine = StatusText;
                return;
            }

            if (!TryParseBookmarkInput(NewBookmarkTime, out double seconds))
            {
                StatusText = "Enter time as seconds or mm:ss.";
                LogLine = StatusText;
                return;
            }

            if (TryAddBookmarkAtSeconds(seconds))
            {
                NewBookmarkTime = "";
            }
        }

        public bool TryAddBookmarkAtSeconds(double seconds)
        {
            if (SelectedClip == null)
            {
                StatusText = "Select a clip to add bookmarks.";
                LogLine = StatusText;
                return false;
            }

            if (seconds < 0.0)
            {
                StatusText = "Bookmark time must be positive.";
                LogLine = StatusText;
                return false;
            }

            if (SelectedClip.Analysis?.DurationSeconds > 0 &&
                seconds > SelectedClip.Analysis.DurationSeconds)
            {
                StatusText = "Bookmark exceeds clip length.";
                LogLine = StatusText;
                return false;
            }

            var bookmarks = SelectedClip.PotPlayerBookmarks != null
                ? new List<double>(SelectedClip.PotPlayerBookmarks)
                : new List<double>();

            if (bookmarks.Any(b => Math.Abs(b - seconds) < 0.05))
            {
                StatusText = "Bookmark already exists near that position.";
                LogLine = StatusText;
                return false;
            }

            bookmarks.Add(seconds);
            bookmarks.Sort();

            SelectedClip.PotPlayerBookmarks = bookmarks;
            if (SelectedClip.Analysis != null)
            {
                SelectedClip.Analysis.Bookmarks = bookmarks;
            }

            StatusText = $"Added bookmark at {seconds:F2}s.";
            LogLine = StatusText;
            return true;
        }

        [RelayCommand]
        private void JumpToBookmark(double seconds)
        {
            if (SelectedClip == null)
                return;

            PreviewSource = new Uri(SelectedClip.Path);
            HasPreview = true;
            SeekToCallback?.Invoke(SelectedClip.Path, seconds, SelectedClip.Analysis?.DurationSeconds ?? 0);

            StatusText = $"Previewing bookmark at {seconds:F2}s.";
            LogLine = StatusText;
        }

        private static bool TryParseBookmarkInput(string? input, out double seconds)
        {
            seconds = 0;
            if (string.IsNullOrWhiteSpace(input)) return false;

            var trimmed = input.Trim();

            // Direct seconds (allows decimals)
            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
                return true;

            // Support mm:ss or hh:mm:ss formats
            if (TimeSpan.TryParse(trimmed, CultureInfo.InvariantCulture, out var ts))
            {
                seconds = ts.TotalSeconds;
                return true;
            }

            return false;
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
                // 1) Ljudanalys (endast om UseMusic är aktiverat)
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

                    _musicAnalysis = await audioSvc.AnalyzeAsync(MusicPath, prog, _cts.Token);
                }
                else
                {
                    _musicAnalysis = null;
                    Progress = 35;
                    StatusText = "Skipping music analysis (no music selected)";
                    LogLine = StatusText;
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

                    // Vi vill inte krascha hela pipelinen om en fil är korrupt, kanske?
                    // Men för nu kastar vi exception om det felar.
                    var analysisResult = await videoSvc.AnalyzeAsync(clip.Path, clipProg, _cts.Token);

                    // Lägg till PotPlayer-bokmärken om de finns
                    if (clip.HasBookmarks && clip.PotPlayerBookmarks != null)
                    {
                        // Skapa ny result med bokmärken
                        clip.Analysis = new VideoAnalysisResult
                        {
                            FilePath = analysisResult.FilePath,
                            DurationSeconds = analysisResult.DurationSeconds,
                            FrameRate = analysisResult.FrameRate,
                            SceneChanges = analysisResult.SceneChanges,
                            Bookmarks = clip.PotPlayerBookmarks
                        };

                        LogLine = $"  → Found {clip.PotPlayerBookmarks.Count} PotPlayer bookmarks!";
                    }
                    else
                    {
                        clip.Analysis = analysisResult;
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
                    Aggressiveness);

                RefreshTimelineSegmentsFromModel();

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

        private bool CanAnalyze() => !IsBusy && Clips.Count > 0 && (!UseMusic || !string.IsNullOrWhiteSpace(MusicPath));

        /// <summary>
        /// Renderar den färdiga videon med FFmpeg.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanRender))]
        private async Task RenderAsync()
        {
            var timelineToRender = _timeline;

            if (timelineToRender == null || timelineToRender.Count == 0)
            {
                StatusText = "No timeline generated yet. Run Analyze first.";
                return;
            }

            // Bestäm filändelse baserat på valt format
            string ext = ExportFileExtension;
            string filter = ext == ".mov" ? "QuickTime MOV|*.mov" : "MP4 Video|*.mp4";

            var saveDlg = new SaveFileDialog
            {
                Title = "Save Video",
                Filter = filter,
                FileName = $"AutoEdit_Output{ext}"
            };

            if (saveDlg.ShowDialog() != true) return;
            string outputPath = saveDlg.FileName;

            _cts = new CancellationTokenSource();
            IsBusy = true;
            Progress = 0;
            ProgressText = $"Rendering ({SelectedExportFormat?.Name})...";

            try
            {
                var renderer = CreateRenderingService();

                // Bygg ExportSettings från UI-val
                var exportSettings = new ExportSettings
                {
                    Format = SelectedExportFormat?.Format ?? ExportFormat.H264,
                    Fps = SelectedFps?.Fps ?? 30,
                    BitrateMbps = SelectedBitrate?.Mbps ?? 20,
                    AudioBitrateKbps = 320,
                    Width = 1920,
                    Height = 1080
                };

                var prog = new Progress<(int percent, string message)>(p =>
                {
                    Progress = p.percent;
                    StatusText = p.message;
                    LogLine = p.message;
                });

                string? musicToUse = UseMusic ? MusicPath : null;
                await renderer.RenderAsync(timelineToRender, musicToUse, outputPath, exportSettings, prog, _cts.Token);

                StatusText = "Render complete.";
                LogLine = $"Saved to: {outputPath}";
                ProgressText = "Done";

                // Ladda den renderade videon för preview
                RenderedVideoPath = outputPath;
                RenderedVideoSource = new Uri(outputPath);
                HasRenderedVideo = true;
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

        [RelayCommand]
        private void ViewRenderedVideo()
        {
            if (!string.IsNullOrEmpty(RenderedVideoPath))
            {
                // Ladda videon i preview-spelaren
                PreviewSource = new Uri(RenderedVideoPath);
                HasPreview = true;

                // Trigga play
                MediaElementCallback?.Invoke(MediaElementAction.Play);
            }
        }

        [RelayCommand]
        private void PreviewTimelineSegment(TimelineSegment? segment)
        {
            if (segment == null) return;

            PreviewSource = new Uri(segment.SourceFilePath);
            HasPreview = true;
            SeekToCallback?.Invoke(segment.SourceFilePath, segment.SourceStart, segment.Duration);

            StatusText = $"Previewing segment #{segment.Order}";
            LogLine = StatusText;
        }

        [RelayCommand]
        private void MoveTimelineSegmentUp(TimelineSegment? segment)
        {
            if (segment == null || IsBusy) return;

            int index = TimelineSegments.IndexOf(segment);
            if (index <= 0) return;

            TimelineSegments.Move(index, index - 1);
            RecalculateTimelineFromSegments();

            StatusText = $"Moved {segment.SourceFileName} earlier in timeline.";
            LogLine = StatusText;
        }

        [RelayCommand]
        private void MoveTimelineSegmentDown(TimelineSegment? segment)
        {
            if (segment == null || IsBusy) return;

            int index = TimelineSegments.IndexOf(segment);
            if (index < 0 || index >= TimelineSegments.Count - 1) return;

            TimelineSegments.Move(index, index + 1);
            RecalculateTimelineFromSegments();

            StatusText = $"Moved {segment.SourceFileName} later in timeline.";
            LogLine = StatusText;
        }

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

        private void RecalculateTimelineFromSegments()
        {
            double currentTimeline = 0;
            int order = 1;

            foreach (var segment in TimelineSegments)
            {
                segment.Order = order++;
                segment.TimelineStart = currentTimeline;
                currentTimeline += segment.Duration;
            }

            _timeline = TimelineSegments
                .Select(seg => new TimelineEvent
                {
                    SourceFilePath = seg.SourceFilePath,
                    SourceStart = seg.SourceStart,
                    Duration = seg.Duration,
                    TimelineStart = seg.TimelineStart
                })
                .ToList();

            OnPropertyChanged(nameof(HasTimeline));
            RenderCommand.NotifyCanExecuteChanged();
        }

        private void RefreshTimelineSegmentsFromModel()
        {
            TimelineSegments.Clear();

            if (_timeline == null || _timeline.Count == 0)
            {
                OnPropertyChanged(nameof(HasTimeline));
                return;
            }

            double currentStart = 0;
            int order = 1;
            foreach (var evt in _timeline.OrderBy(t => t.TimelineStart))
            {
                var segment = new TimelineSegment(
                    order++,
                    evt.SourceFilePath,
                    evt.SourceStart,
                    evt.Duration,
                    evt.TimelineStart > 0 ? evt.TimelineStart : currentStart);

                currentStart = segment.TimelineStart + segment.Duration;
                TimelineSegments.Add(segment);
            }

            OnPropertyChanged(nameof(HasTimeline));
            RenderCommand.NotifyCanExecuteChanged();
        }

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

    public partial class TimelineSegment : ObservableObject
    {
        public TimelineSegment(int order, string sourceFilePath, double sourceStart, double duration, double timelineStart)
        {
            Order = order;
            SourceFilePath = sourceFilePath;
            SourceStart = sourceStart;
            Duration = duration;
            TimelineStart = timelineStart;
        }

        public string SourceFilePath { get; }
        public string SourceFileName => Path.GetFileName(SourceFilePath);

        [ObservableProperty] private int order;
        [ObservableProperty] private double sourceStart;
        [ObservableProperty] private double duration;
        [ObservableProperty] private double timelineStart;

        public string TimelineLabel => $"{Order}. {SourceFileName}";
        public string DetailLabel => $"{FormatTime(TimelineStart)} → {FormatTime(TimelineStart + Duration)} ({Duration:F1}s)";

        partial void OnOrderChanged(int value) => OnPropertyChanged(nameof(TimelineLabel));
        partial void OnDurationChanged(double value) => OnPropertyChanged(nameof(DetailLabel));
        partial void OnTimelineStartChanged(double value) => OnPropertyChanged(nameof(DetailLabel));

        private static string FormatTime(double seconds)
        {
            var safeSeconds = Math.Max(0, seconds);
            string format = safeSeconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss";
            return TimeSpan.FromSeconds(safeSeconds).ToString(format);
        }
    }

    public partial class ClipItem : ObservableObject
    {
        public string Path { get; }
        public string FileName => System.IO.Path.GetFileName(Path);
        public VideoAnalysisResult? Analysis { get; set; }

        [ObservableProperty] private List<double>? potPlayerBookmarks;
        [ObservableProperty] private bool hasBookmarks;

        public ClipItem(string path)
        {
            Path = path;
            LoadPotPlayerBookmarks();
        }

        private void LoadPotPlayerBookmarks()
        {
            // Sök efter .pbf-fil med samma namn som videon
            string dir = System.IO.Path.GetDirectoryName(Path) ?? "";
            string nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(Path);
            string pbfPath = System.IO.Path.Combine(dir, nameWithoutExt + ".pbf");

            if (File.Exists(pbfPath))
            {
                PotPlayerBookmarks = PotPlayerBookmarkParser.ParseBookmarks(pbfPath);
                HasBookmarks = PotPlayerBookmarks != null && PotPlayerBookmarks.Count > 0;
            }
            else
            {
                PotPlayerBookmarks = null;
                HasBookmarks = false;
            }
        }

        partial void OnPotPlayerBookmarksChanged(List<double>? value)
        {
            HasBookmarks = value != null && value.Count > 0;
        }
    }

    public sealed class ThemeOption
    {
        public ThemeOption(string name, AppTheme theme)
        {
            Name = name;
            Theme = theme;
        }

        public string Name { get; }
        public AppTheme Theme { get; }
    }

    // ===== EXPORT SETTINGS HELPER CLASSES =====

    public record ExportFormatItem(ExportFormat Format, string Name, string Description)
    {
        public override string ToString() => Name;
    }

    public record FpsOption(double Fps, string Name)
    {
        public override string ToString() => Name;
    }

    public record BitrateOption(double Mbps, string Name)
    {
        public override string ToString() => Name;
    }
}
