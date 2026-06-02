using System.Text;
using JoinGameAfk.Constant;
using JoinGameAfk.Enums;
using JoinGameAfk.Interface;
using JoinGameAfk.Model;
using JoinGameAfk.Presentation.View.Dashboard;
using JoinGameAfk.Services;
using LcuClient;

namespace JoinGameAfk.Presentation.Controller
{
    public partial class PhaseController : IDisposable
    {
        private readonly PhaseProgressionPage fPhaseProgressionPage;
        private readonly LogsPage _logsPage;
        private readonly GeneralSettings _generalSettings;
        private readonly RolePlanSettings _rolePlanSettings;
        private readonly SoundSettings _soundSettings;
        private readonly NotificationSoundPlayer _notificationSoundPlayer;
        private readonly List<IPhaseHandler> _phaseHandlers;
        private readonly Lcu.ProcessManager _processManager;

        private CancellationTokenSource? _cts;
        private Task? _runLoopTask;
        private Lcu.LeagueClientEventStream? _eventStream;
        private Task? _eventStreamTask;
        private readonly object _lcuEventSignalLock = new();
        private TaskCompletionSource _lcuEventSignal = CreateEventSignal();
        private readonly object _clientDisconnectLock = new();
        private readonly object _pendingLcuEventsLock = new();
        private ClientPhase? _pendingEventPhase;
        private string? _pendingChampSelectSessionJson;
        private DateTime _pendingChampSelectSessionObservedAtUtc;
        private string? _pendingReadyCheckJson;
        private string? _pendingGameflowSessionJson;
        private string? _pendingLobbyJson;
        private string? _pendingClientDisconnectMessage;
        private bool _isRunning;
        private ClientPhase _lastObservedPhase;
        private ClientPhase _lastHandledPhase;
        private QueueSupportState _lastQueueSupportState = QueueSupportState.Unknown;
        private bool _hasLookedUpQueueSupportDuringReadyCheck;
        private string _lastUnsupportedQueueLogKey = string.Empty;
        private bool _isClientConnected;
        private bool _isEventStreamConnecting;
        private bool _isEventStreamAvailable;
        private int _eventStreamRetryAttempt;
        private DateTime _nextEventStreamRetryAtUtc;
        private bool _hasReceivedPhaseResponse;
        private bool _hasPendingChampSelectExitSound;
        private bool _isWaitingForClient;
        private bool _isShutdownRequested;
        private bool _disposed;

        public bool IsRunning => _isRunning;

        public PhaseController(
            PhaseProgressionPage phaseProgressionPage,
            LogsPage logsPage,
            GeneralSettings generalSettings,
            RolePlanSettings rolePlanSettings,
            SoundSettings soundSettings)
        {
            fPhaseProgressionPage = phaseProgressionPage;
            _logsPage = logsPage;
            _generalSettings = generalSettings;
            _rolePlanSettings = rolePlanSettings;
            _soundSettings = soundSettings;
            _notificationSoundPlayer = new NotificationSoundPlayer(LogError);
            _phaseHandlers = [];
            _processManager = new Lcu.ProcessManager(JoinGameAfkConstant.LeagueClient.ProcessName);
            _generalSettings.Saved += OnSettingsSaved;
            _rolePlanSettings.Saved += OnSettingsSaved;
        }

        public void Start()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PhaseController));

            if (_isRunning || _runLoopTask is { IsCompleted: false })
                return;

            _isShutdownRequested = false;
            _isRunning = true;
            _lastObservedPhase = ClientPhase.Unknown;
            _lastHandledPhase = ClientPhase.Unknown;
            _isClientConnected = false;
            _hasPendingChampSelectExitSound = false;
            ResetEventStreamState();
            _isWaitingForClient = false;
            ResetLcuEventSignal();
            ClearClientDisconnectRequest();
            ClearPendingLcuEvents();
            ResetQueueSupportState();

            fPhaseProgressionPage.SetWatcherState(true);
            fPhaseProgressionPage.SetClientConnection(false);
            fPhaseProgressionPage.UpdatePhase(ClientPhase.Unknown);
            fPhaseProgressionPage.UpdateDashboardStatus(new DashboardStatus());

            var cancellationTokenSource = new CancellationTokenSource();
            _cts = cancellationTokenSource;
            _runLoopTask = Task.Run(() => RunLoopAsync(cancellationTokenSource.Token), CancellationToken.None);
            _ = ObserveRunLoopCompletionAsync(_runLoopTask, cancellationTokenSource);
        }

        public void Stop()
        {
            StopCore(updateUi: true);
        }

        public void Shutdown()
        {
            _isShutdownRequested = true;
            StopCore(updateUi: false);
        }

        private void StopCore(bool updateUi)
        {
            if (!_isRunning && _runLoopTask is not { IsCompleted: false })
                return;

            _isRunning = false;
            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            _eventStream?.Dispose();
            SignalLcuEvent();
            ResetEventStreamState();
            _hasPendingChampSelectExitSound = false;
            ClearClientDisconnectRequest();
            ClearPendingLcuEvents();
            ResetQueueSupportState();
            if (!updateUi || _isShutdownRequested)
                return;

            fPhaseProgressionPage.SetWatcherState(false);
            fPhaseProgressionPage.SetClientConnection(false);
            fPhaseProgressionPage.UpdatePhase(ClientPhase.Unknown);
            fPhaseProgressionPage.UpdateDashboardStatus(new DashboardStatus());
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Shutdown();
            _generalSettings.Saved -= OnSettingsSaved;
            _rolePlanSettings.Saved -= OnSettingsSaved;
        }

        private AuthModel? GetLeagueAuth()
        {
#if DEBUG
            if (TryGetMockLeagueClientAuth(out var mockAuth))
                return mockAuth;
#endif

            return _processManager.GetLeagueAuth();
        }

#if DEBUG
        private static bool TryGetMockLeagueClientAuth(out AuthModel? auth)
        {
            auth = null;

            string? portText = Environment.GetEnvironmentVariable("JOIN_GAME_AFK_MOCK_LEAGUE_CLIENT_PORT");
            if (string.IsNullOrWhiteSpace(portText)
                || !int.TryParse(portText.Trim(), out int port)
                || port is <= 0 or > 65535)
            {
                return false;
            }

            string? token = Environment.GetEnvironmentVariable("JOIN_GAME_AFK_MOCK_LEAGUE_CLIENT_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
                token = "mock-league-client";

            string encodedToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{token.Trim()}"));
            auth = new AuthModel(port.ToString(), encodedToken);
            return true;
        }
#endif
    }
}
