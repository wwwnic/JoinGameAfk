using System.Windows;
using System.Windows.Input;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;

namespace JoinGameAfk.Presentation.View.Overlays
{
    public partial class QueueMicroOverlayWindow : Window
    {
        private const double BaseWindowSize = 56;
        private const double BaseIndicatorSize = 42;

        private readonly OverlaySettings _settings;
        private DashboardStatus _lastDashboardStatus = new();
        private ClientPhase _currentPhase = ClientPhase.Unknown;
        private bool _isWatcherRunning;
        private bool _isClientConnected;
        private string _readyCheckResponse = string.Empty;

        public event Action<double, double>? PositionChangedByUser;

        public QueueMicroOverlayWindow(OverlaySettings settings)
        {
            _settings = settings;
            _settings.NormalizeOptions();
            InitializeComponent();
            ApplySettings();
            RefreshStatusDisplay();
        }

        public void SetWatcherState(bool isRunning)
        {
            Dispatch(() =>
            {
                _isWatcherRunning = isRunning;
                RefreshStatusDisplay();
            });
        }

        public void SetClientConnection(bool isConnected)
        {
            Dispatch(() =>
            {
                _isClientConnected = isConnected;
                RefreshStatusDisplay();
            });
        }

        public void UpdatePhase(ClientPhase phase)
        {
            Dispatch(() =>
            {
                _currentPhase = phase;
                RefreshStatusDisplay();
            });
        }

        public void UpdateDashboardStatus(DashboardStatus status)
        {
            Dispatch(() =>
            {
                _lastDashboardStatus = status;
                _readyCheckResponse = status.ReadyCheckResponse;
                RefreshStatusDisplay();
            });
        }

        public void ApplySettings()
        {
            Topmost = _settings.QueueMicroOverlayTopmostEnabled;
            double scale = OverlaySettings.NormalizeQueueMicroOverlayScalePercent(_settings.QueueMicroOverlayScalePercent) / 100d;
            Width = Math.Round(BaseWindowSize * scale);
            Height = Math.Round(BaseWindowSize * scale);
            MinWidth = Width;
            MinHeight = Height;
            MaxWidth = Width;
            MaxHeight = Height;
            MicroPhaseIndicator.Width = Math.Round(BaseIndicatorSize * scale);
            MicroPhaseIndicator.Height = Math.Round(BaseIndicatorSize * scale);
        }

        private void RefreshStatusDisplay()
        {
            MicroPhaseIndicator.Update(
                _currentPhase,
                _isWatcherRunning,
                _isClientConnected,
                GetPhaseIndicatorState(),
                _lastDashboardStatus.ReadyCheckAutoAcceptDelayMilliseconds,
                _lastDashboardStatus.ReadyCheckAutoAcceptTimeLeftMilliseconds,
                _lastDashboardStatus.ReadyCheckAutoAcceptObservedAtUtc,
                _lastDashboardStatus.AllConfiguredOptionsUnavailable);
        }

        private string GetPhaseIndicatorState()
        {
            return _currentPhase == ClientPhase.ReadyCheck
                ? _readyCheckResponse
                : string.Empty;
        }

        private void DragSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            DragMove();
            PositionChangedByUser?.Invoke(Left, Top);
        }

        private void Dispatch(Action action)
        {
            if (Dispatcher.CheckAccess())
                action();
            else
                Dispatcher.Invoke(action);
        }
    }
}
