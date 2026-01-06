using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace AutoEdit.UI
{
    public partial class MainWindow : Window
    {
        private MainViewModel? _viewModel;
        private bool _isPlaying = false;
        private bool _isDraggingSlider = false;
        private DispatcherTimer _positionTimer;
        private TimeSpan _totalDuration;
        private double? _pendingSeekSeconds;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            // Koppla MediaElement callback
            _viewModel.MediaElementCallback = HandleMediaElementAction;

            // Koppla SeekTo callback för timeline preview
            _viewModel.SeekToCallback = SeekToPosition;

            // Timer för att uppdatera seek slider position
            _positionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _positionTimer.Tick += PositionTimer_Tick;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.P || Keyboard.Modifiers != ModifierKeys.None)
                return;

            if (Keyboard.FocusedElement is TextBox)
                return;

            if (_viewModel?.SelectedClip == null || PreviewPlayer.Source == null)
                return;

            var previewPath = PreviewPlayer.Source.LocalPath;
            if (!string.Equals(previewPath, _viewModel.SelectedClip.Path, StringComparison.OrdinalIgnoreCase))
                return;

            double seconds = PreviewPlayer.Position.TotalSeconds;
            if (_totalDuration.TotalSeconds > 0)
            {
                seconds = Math.Min(seconds, _totalDuration.TotalSeconds);
            }

            _viewModel.TryAddBookmarkAtSeconds(seconds);
            e.Handled = true;
        }

        // Title bar handlers
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void SeekToPosition(string filePath, double startSeconds, double durationSeconds)
        {
            var sameSource = PreviewPlayer.Source != null &&
                string.Equals(PreviewPlayer.Source.LocalPath, filePath, StringComparison.OrdinalIgnoreCase);

            if (sameSource && PreviewPlayer.NaturalDuration.HasTimeSpan)
            {
                SeekPreviewToSeconds(startSeconds);
                _pendingSeekSeconds = null;
                EnsurePreviewPlayback();
                return;
            }

            // Ladda videon och seekar till rätt position
            _pendingSeekSeconds = startSeconds;
            PreviewPlayer.SetCurrentValue(MediaElement.SourceProperty, new Uri(filePath));
            PreviewPlayer.Play();
            _isPlaying = true;
        }

        private void SeekPreviewToSeconds(double startSeconds)
        {
            double seekSeconds = Math.Max(0, startSeconds);
            if (_totalDuration.TotalSeconds > 0)
            {
                seekSeconds = Math.Min(seekSeconds, _totalDuration.TotalSeconds);
            }

            PreviewPlayer.Position = TimeSpan.FromSeconds(seekSeconds);
            if (!_isDraggingSlider)
            {
                SeekSlider.Value = seekSeconds;
            }
            UpdateTimeDisplay();
        }

        private void EnsurePreviewPlayback()
        {
            PreviewPlayer.Play();
            _isPlaying = true;
            _positionTimer.Start();
        }

        private void HandleMediaElementAction(MainViewModel.MediaElementAction action)
        {
            switch (action)
            {
                case MainViewModel.MediaElementAction.Play:
                    if (_isPlaying)
                    {
                        PreviewPlayer.Pause();
                        _isPlaying = false;
                        _positionTimer.Stop();
                    }
                    else
                    {
                        PreviewPlayer.Play();
                        _isPlaying = true;
                        _positionTimer.Start();
                    }
                    break;

                case MainViewModel.MediaElementAction.Pause:
                    PreviewPlayer.Pause();
                    _isPlaying = false;
                    _positionTimer.Stop();
                    break;

                case MainViewModel.MediaElementAction.Stop:
                    PreviewPlayer.Stop();
                    _isPlaying = false;
                    _positionTimer.Stop();
                    SeekSlider.Value = 0;
                    CurrentTimeText.Text = "0:00";
                    break;
            }
        }

        private void PositionTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isDraggingSlider && PreviewPlayer.NaturalDuration.HasTimeSpan)
            {
                SeekSlider.Value = PreviewPlayer.Position.TotalSeconds;
                UpdateTimeDisplay();
            }
        }

        private void UpdateTimeDisplay()
        {
            var current = PreviewPlayer.Position;
            CurrentTimeText.Text = FormatTime(current);
            TotalTimeText.Text = FormatTime(_totalDuration);
        }

        private string FormatTime(TimeSpan time)
        {
            if (time.TotalHours >= 1)
                return time.ToString(@"h\:mm\:ss");
            return time.ToString(@"m\:ss");
        }

        private void PreviewPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            // Spara total längd och starta timer
            _totalDuration = PreviewPlayer.NaturalDuration.HasTimeSpan
                ? PreviewPlayer.NaturalDuration.TimeSpan
                : TimeSpan.Zero;

            SeekSlider.Maximum = _totalDuration.TotalSeconds;

            if (_pendingSeekSeconds.HasValue)
            {
                SeekPreviewToSeconds(_pendingSeekSeconds.Value);
                _pendingSeekSeconds = null;
            }

            UpdateTimeDisplay();

            // Auto-play när video laddas
            EnsurePreviewPlayback();
        }

        private void PreviewPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            // Stoppa istället för att loopa
            _isPlaying = false;
            _positionTimer.Stop();
            PreviewPlayer.Position = TimeSpan.Zero;
            SeekSlider.Value = 0;
            UpdateTimeDisplay();
        }

        private void SeekSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider = true;
            if (_isPlaying)
            {
                PreviewPlayer.Pause();
            }
        }

        private void SeekSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider = false;
            PreviewPlayer.Position = TimeSpan.FromSeconds(SeekSlider.Value);
            if (_isPlaying)
            {
                PreviewPlayer.Play();
            }
            UpdateTimeDisplay();
        }

        private DateTime _lastSeekTime = DateTime.MinValue;

        private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isDraggingSlider)
            {
                // Visa tid medan man drar
                CurrentTimeText.Text = FormatTime(TimeSpan.FromSeconds(SeekSlider.Value));

                // Live Scrubbing med throttling (max 30fps = 33ms)
                if ((DateTime.Now - _lastSeekTime).TotalMilliseconds > 33)
                {
                    PreviewPlayer.Position = TimeSpan.FromSeconds(SeekSlider.Value);
                    _lastSeekTime = DateTime.Now;
                }
            }
        }

        // ===== DRAG & DROP HANDLERS =====

        private void DropZone_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;

                // Visuell feedback - gör border tjockare/ljusare
                if (sender is System.Windows.Controls.Border border)
                {
                    border.BorderThickness = new Thickness(3);
                    border.Opacity = 1.0;
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void DropZone_DragLeave(object sender, DragEventArgs e)
        {
            // Återställ visuell feedback
            if (sender is System.Windows.Controls.Border border)
            {
                border.BorderThickness = new Thickness(2);
                border.Opacity = 1.0;
            }
            e.Handled = true;
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            // Återställ visuell feedback
            if (sender is System.Windows.Controls.Border border)
            {
                border.BorderThickness = new Thickness(2);
                border.Opacity = 1.0;
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];

                if (files != null && files.Length > 0 && _viewModel != null)
                {
                    _viewModel.AddFiles(files);
                }
            }
            e.Handled = true;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
