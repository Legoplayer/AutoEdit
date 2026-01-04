using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace AutoEdit.UI
{
    public partial class MainWindow : Window
    {
        private MainViewModel? _viewModel;
        private bool _isPlaying = false;
        
        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
            
            // Koppla MediaElement callback
            _viewModel.MediaElementCallback = HandleMediaElementAction;
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
                    }
                    else
                    {
                        PreviewPlayer.Play();
                        _isPlaying = true;
                    }
                    break;
                    
                case MainViewModel.MediaElementAction.Pause:
                    PreviewPlayer.Pause();
                    _isPlaying = false;
                    break;
                    
                case MainViewModel.MediaElementAction.Stop:
                    PreviewPlayer.Stop();
                    _isPlaying = false;
                    break;
            }
        }
        
        private void PreviewPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            // Auto-play när video laddas
            PreviewPlayer.Play();
            _isPlaying = true;
        }
        
        private void PreviewPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            // Loop video
            PreviewPlayer.Position = TimeSpan.Zero;
            PreviewPlayer.Play();
        }
    }
}