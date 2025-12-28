using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace AutoEdit.UI
{
    /// <summary>
    /// Huvudvy-modell för AutoEdit-applikationen.
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        public ObservableCollection<ClipItem> Clips { get; } = new();

        [ObservableProperty] private ClipItem? selectedClip;
        [ObservableProperty] private string? musicPath;

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
                // TODO: ersätt med services i AutoEdit.Media/Core
                await StepAsync("Analyzing music...", 0, 35, _cts.Token);
                await StepAsync("Analyzing clips...", 35, 80, _cts.Token);
                await StepAsync("Building timeline...", 80, 100, _cts.Token);

                StatusText = "Analysis complete.";
                LogLine = StatusText;
                ProgressText = "Done";
            }
            catch (OperationCanceledException)
            {
                StatusText = "Canceled.";
                LogLine = StatusText;
                ProgressText = "Canceled";
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
            _cts = new CancellationTokenSource();
            IsBusy = true;
            Progress = 0;
            ProgressText = "Rendering...";

            try
            {
                // TODO: här kör du FFmpeg render
                await StepAsync("Rendering video...", 0, 100, _cts.Token);

                StatusText = "Render complete.";
                LogLine = StatusText;
                ProgressText = "Done";
            }
            catch (OperationCanceledException)
            {
                StatusText = "Canceled.";
                LogLine = StatusText;
                ProgressText = "Canceled";
            }
            finally
            {
                IsBusy = false;
                _cts = null;
                AnalyzeCommand.NotifyCanExecuteChanged();
                RenderCommand.NotifyCanExecuteChanged();
            }
        }

        private bool CanRender() => !IsBusy && Clips.Count > 0 && !string.IsNullOrWhiteSpace(MusicPath);

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
    }

    public sealed class ClipItem
    {
        public string Path { get; }
        public string FileName => System.IO.Path.GetFileName(Path);
        
        public ClipItem(string path) => Path = path;
    }
}
