using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JoinGameAfk.Enums;
using JoinGameAfk.MVP.Controller;

namespace JoinGameAfk.View
{
    public partial class PhaseProgressionPage : Page
    {
        public event Action<ClientPhase>? PhaseChanged;

        private static readonly SolidColorBrush StartBrush = new((Color)ColorConverter.ConvertFromString("#2E7D32"));
        private static readonly SolidColorBrush StopBrush = new((Color)ColorConverter.ConvertFromString("#C62828"));
        private static readonly SolidColorBrush ConnectedFg = new((Color)ColorConverter.ConvertFromString("#4ADE80"));
        private static readonly SolidColorBrush OfflineFg = new((Color)ColorConverter.ConvertFromString("#FCA5A5"));

        private PhaseController? _phaseController;
        private ClientPhase _currentPhase;
        private bool _isClientConnected;
        private bool _isWatcherRunning;

        public PhaseProgressionPage()
        {
            InitializeComponent();
            SetWatcherState(false);
            SetClientConnection(false);
            UpdatePhase(ClientPhase.Unknown);
        }

        internal void SetController(PhaseController controller)
        {
            _phaseController = controller;
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_phaseController == null) return;

            if (_phaseController.IsRunning)
                _phaseController.Stop();
            else
                _phaseController.Start();
        }

        public void UpdatePhase(ClientPhase phase)
        {
            Dispatcher.Invoke(() =>
            {
                _currentPhase = phase;
                StatusText.Text = GetStatusLine(phase);
                PhaseChanged?.Invoke(phase);
            });
        }

        public void SetWatcherState(bool isRunning)
        {
            Dispatcher.Invoke(() =>
            {
                _isWatcherRunning = isRunning;
                ToggleButton.Content = isRunning ? "Stop" : "Start";
                ToggleButton.Background = isRunning ? StopBrush : StartBrush;
                StatusText.Text = GetStatusLine(_currentPhase);
            });
        }

        public void SetClientConnection(bool isConnected)
        {
            Dispatcher.Invoke(() =>
            {
                _isClientConnected = isConnected;
                ConnectionText.Text = isConnected ? "Client connected" : "Client offline";
                ConnectionText.Foreground = isConnected ? ConnectedFg : OfflineFg;
                StatusText.Text = GetStatusLine(_currentPhase);
            });
        }

        public void ShowAction(string action)
        {
        }

        public void WriteLine(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
                LogTextBox.ScrollToEnd();
            });
        }

        public void WriteErrorLine(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] ERROR: {message}\n");
                LogTextBox.ScrollToEnd();
            });
        }

        private string GetStatusLine(ClientPhase phase)
        {
            return phase switch
            {
                ClientPhase.Lobby => "Lobby",
                ClientPhase.Matchmaking => "In Queue",
                ClientPhase.ReadyCheck => "Ready Check",
                ClientPhase.ChampSelect => "Champion Select",
                ClientPhase.Planning => "Planning",
                ClientPhase.InGame => "In Game",
                _ when _isWatcherRunning && !_isClientConnected => "Waiting for client…",
                _ when _isWatcherRunning => "Watching",
                _ => "Stopped"
            };
        }
    }
}