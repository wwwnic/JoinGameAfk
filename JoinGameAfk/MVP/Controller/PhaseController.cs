using System.Text.Json;
using System.Diagnostics;
using System.Net.Http;
using JoinGameAfk.Constant;
using JoinGameAfk.Enums;
using JoinGameAfk.Interface;
using JoinGameAfk.Model;
using JoinGameAfk.Services;
using JoinGameAfk.View;
using LcuClient;

namespace JoinGameAfk.MVP.Controller
{
    public class PhaseController : IDisposable
    {
        private static readonly TimeSpan EventStreamStopTimeout = TimeSpan.FromSeconds(2);

        private static readonly TimeSpan[] EventStreamRetryDelays =
        [
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(30)
        ];

        private static readonly IReadOnlyDictionary<int, string> SupportedQueueNames = new Dictionary<int, string>
        {
            [400] = "Normal Draft",
            [420] = "Ranked Solo/Duo",
            [430] = "Blind Pick",
            [440] = "Ranked Flex"
        };

        private static readonly IReadOnlyDictionary<int, string> KnownQueueNames = new Dictionary<int, string>
        {
            [400] = "Normal Draft",
            [420] = "Ranked Solo/Duo",
            [430] = "Blind Pick",
            [440] = "Ranked Flex",
            [450] = "ARAM",
            [480] = "Swiftplay",
            [490] = "Quickplay",
            [700] = "Clash",
            [720] = "ARAM Clash",
            [870] = "Intro Bots",
            [880] = "Beginner Bots",
            [890] = "Intermediate Bots",
            [900] = "ARURF",
            [1020] = "One for All",
            [1400] = "Ultimate Spellbook",
            [1700] = "Arena",
            [1710] = "Arena",
            [1900] = "Pick URF",
            [2300] = "Brawl",
            [2400] = "ARAM: Mayhem"
        };

        private readonly record struct LcuEventSnapshot(
            ClientPhase? Phase,
            string? ChampSelectSessionJson,
            DateTime ChampSelectSessionObservedAtUtc,
            string? ReadyCheckJson,
            string? GameflowSessionJson,
            string? LobbyJson)
        {
            public bool HasChampSelectSession => !string.IsNullOrWhiteSpace(ChampSelectSessionJson);
            public bool HasReadyCheckJson => !string.IsNullOrWhiteSpace(ReadyCheckJson);
            public bool HasGameflowSessionJson => !string.IsNullOrWhiteSpace(GameflowSessionJson);
            public bool HasLobbyJson => !string.IsNullOrWhiteSpace(LobbyJson);
        }

        private readonly record struct QueueSupportState(
            int? QueueId,
            string QueueName,
            bool HasQueue,
            bool IsSupported)
        {
            public static QueueSupportState Unknown { get; } = new(null, string.Empty, false, false);
            public bool IsUnsupported => HasQueue && !IsSupported;
        }

        private readonly PhaseProgressionPage fPhaseProgressionPage;
        private readonly LogsPage _logsPage;
        private readonly ChampSelectSettings _champSelectSettings;
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

        public PhaseController(PhaseProgressionPage phaseProgressionPage, LogsPage logsPage, ChampSelectSettings champSelectSettings)
        {
            fPhaseProgressionPage = phaseProgressionPage;
            _logsPage = logsPage;
            _champSelectSettings = champSelectSettings;
            _notificationSoundPlayer = new NotificationSoundPlayer(LogError);
            _phaseHandlers = [];
            _processManager = new Lcu.ProcessManager(JoinGameAfkConstant.LeagueClient.ProcessName);
            _champSelectSettings.Saved += OnSettingsSaved;
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
            _champSelectSettings.Saved -= OnSettingsSaved;
        }

        private async Task ObserveRunLoopCompletionAsync(Task runLoopTask, CancellationTokenSource cancellationTokenSource)
        {
            try
            {
                await runLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
            {
                // If cancellation was requested, we don't consider it an error.
            }
            catch (Exception ex)
            {
                if (!_isShutdownRequested)
                    LogError($"Watcher stopped unexpectedly. {ex.Message}");
            }
            finally
            {
                if (ReferenceEquals(_runLoopTask, runLoopTask))
                {
                    _runLoopTask = null;
                    _cts = null;
                }

                cancellationTokenSource.Dispose();
            }
        }

        private async Task RunLoopAsync(CancellationToken ct)
        {
            Log("Waiting for League Client process...");

            Lcu.LeagueClientHttp? http = null;
            AuthModel? currentAuth = null;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var iterationStopwatch = Stopwatch.StartNew();

                    try
                    {
                        if (TryConsumeClientDisconnectRequest(out string? disconnectMessage))
                        {
                            DisconnectCurrentClient(ref http, ref currentAuth, disconnectMessage);
                            await Task.Delay(3000, ct);
                            continue;
                        }

                        if (http == null || !http.HasAuthToken())
                        {
                            var auth = _processManager.GetLeagueAuth();
                            if (auth == null)
                            {
                                if (!_isWaitingForClient)
                                {
                                    _isWaitingForClient = true;
                                    _isClientConnected = false;
                                    _lastObservedPhase = ClientPhase.Unknown;
                                    _lastHandledPhase = ClientPhase.Unknown;
                                    fPhaseProgressionPage.SetClientConnection(false);
                                    fPhaseProgressionPage.UpdatePhase(ClientPhase.Unknown);
                                    fPhaseProgressionPage.UpdateDashboardStatus(new DashboardStatus());
                                }

                                await Task.Delay(3000, ct);
                                continue;
                            }

                            _isWaitingForClient = false;
                            http = new Lcu.LeagueClientHttp(auth, Log);
                            currentAuth = auth;
                            ResetEventStreamState();
                            StartEventStreamIfEnabled(auth, ct);
                            InitializeHandlers(http);

                            if (!_isClientConnected)
                            {
                                _isClientConnected = true;
                                Log("Connected to League Client.");
                                fPhaseProgressionPage.SetClientConnection(true);
                            }
                        }

                        var activeAuth = _processManager.GetLeagueAuth();
                        if (activeAuth is null)
                        {
                            DisconnectCurrentClient(
                                ref http,
                                ref currentAuth,
                                "League Client process closed. Waiting for League Client process...");
                            await Task.Delay(3000, ct);
                            continue;
                        }

                        if (!IsSameAuth(activeAuth, currentAuth))
                        {
                            DisconnectCurrentClient(
                                ref http,
                                ref currentAuth,
                                "League Client connection changed. Reconnecting...");
                            continue;
                        }

                        LcuEventSnapshot eventSnapshot = ConsumePendingLcuEvents();
                        ClientPhase phase = await ResolveCurrentPhaseAsync(http, eventSnapshot, ct);
                        fPhaseProgressionPage.UpdatePhase(phase);
                        SyncEventStreamState(currentAuth, ct);

                        if (phase != _lastObservedPhase)
                        {
                            if (_lastObservedPhase == ClientPhase.ReadyCheck && phase != ClientPhase.ReadyCheck)
                                _phaseHandlers.OfType<ReadyCheck>().FirstOrDefault()?.CancelPendingAccept();

                            Log($"Phase changed: {_lastObservedPhase} -> {phase}");
                            PlayPhaseSoundAlert(_lastObservedPhase, phase);

                            _lastObservedPhase = phase;
                            _lastHandledPhase = ClientPhase.Unknown;

                            var champSelectHandler = _phaseHandlers.OfType<ChampSelect>().FirstOrDefault();
                            champSelectHandler?.Reset();
                        }

                        var handler = _phaseHandlers.FirstOrDefault(h => h.ClientPhase == phase);
                        var champSelect = _phaseHandlers.OfType<ChampSelect>().FirstOrDefault();
                        bool isChampSelectFlow = IsChampSelectFlow(phase);
                        QueueSupportState queueSupportState = await ResolveQueueSupportStateAsync(http, eventSnapshot, phase, ct);

                        if (queueSupportState.IsUnsupported)
                            LogUnsupportedQueueIfNeeded(queueSupportState);
                        else if (queueSupportState.HasQueue)
                            _lastUnsupportedQueueLogKey = string.Empty;

                        if (phase == ClientPhase.ReadyCheck
                            && handler != null
                            && _lastHandledPhase != phase)
                        {
                            string? actionMessage = GetActionMessage(phase);
                            if (!string.IsNullOrWhiteSpace(actionMessage))
                                Log(actionMessage);

                            await handler.HandleAsync(ct);
                            _lastHandledPhase = phase;
                        }

                        if (isChampSelectFlow && queueSupportState.IsUnsupported)
                        {
                            champSelect?.Reset();
                            fPhaseProgressionPage.UpdateDashboardStatus(BuildUnsupportedModeDashboardStatus(queueSupportState));
                        }
                        else
                        {
                            if (!isChampSelectFlow)
                            {
                                if (phase == ClientPhase.ReadyCheck)
                                    await UpdateReadyCheckDashboardStatusAsync(http, eventSnapshot, queueSupportState, ct);
                                else
                                    fPhaseProgressionPage.UpdateDashboardStatus(BuildNonChampSelectDashboardStatus(queueSupportState));
                            }

                            if (handler != null && _lastHandledPhase != phase)
                            {
                                string? actionMessage = GetActionMessage(phase);
                                if (!string.IsNullOrWhiteSpace(actionMessage))
                                    Log(actionMessage);

                                if (handler is ChampSelect cs)
                                {
                                    if (await TryHandleChampSelectAsync(cs, eventSnapshot, ct))
                                        _lastHandledPhase = phase;
                                }
                                else
                                {
                                    await handler.HandleAsync(ct);
                                    _lastHandledPhase = phase;
                                }
                            }
                            else if (champSelect != null && isChampSelectFlow)
                            {
                                await TryHandleChampSelectAsync(champSelect, eventSnapshot, ct);
                            }
                        }

                        int? delayMs = GetLoopDelayMs();

                        int? remainingDelayMs = delayMs.HasValue
                            ? Math.Max(0, delayMs.Value - (int)iterationStopwatch.ElapsedMilliseconds)
                            : null;
                        await WaitForLoopDelayAsync(remainingDelayMs, ct);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        LogError(ex.Message);
                        DisconnectCurrentClient(ref http, ref currentAuth);
                        await Task.Delay(5000, ct);
                    }
                }
            }
            finally
            {
                Task? eventStreamTask = _eventStreamTask;
                _eventStream?.Dispose();
                _eventStream = null;
                _eventStreamTask = null;
                if (eventStreamTask is not null)
                    await WaitForEventStreamTaskToStopAsync(eventStreamTask, ct);

                ResetEventStreamState();
                ClearClientDisconnectRequest();
                ClearPendingLcuEvents();
                ResetQueueSupportState();
                http?.Dispose();
                Log("Stopped watching.");
            }
        }

        private void DisconnectCurrentClient(
            ref Lcu.LeagueClientHttp? http,
            ref AuthModel? currentAuth,
            string? logMessage = null)
        {
            if (!string.IsNullOrWhiteSpace(logMessage))
                Log(logMessage);

            _phaseHandlers.OfType<ReadyCheck>().FirstOrDefault()?.CancelPendingAccept();
            _eventStream?.Dispose();
            _eventStream = null;
            _eventStreamTask = null;
            ResetEventStreamState();
            ClearPendingLcuEvents();
            ResetQueueSupportState();
            http?.Dispose();
            http = null;
            currentAuth = null;
            _isClientConnected = false;
            _isWaitingForClient = false;
            _lastObservedPhase = ClientPhase.Unknown;
            _lastHandledPhase = ClientPhase.Unknown;
            _hasPendingChampSelectExitSound = false;
            fPhaseProgressionPage.SetClientConnection(false);
            fPhaseProgressionPage.UpdatePhase(ClientPhase.Unknown);
            fPhaseProgressionPage.UpdateDashboardStatus(new DashboardStatus());
        }

        private void RequestClientDisconnect(string message)
        {
            lock (_clientDisconnectLock)
            {
                _pendingClientDisconnectMessage ??= message;
            }

            SignalLcuEvent();
        }

        private bool TryConsumeClientDisconnectRequest(out string? message)
        {
            lock (_clientDisconnectLock)
            {
                message = _pendingClientDisconnectMessage;
                _pendingClientDisconnectMessage = null;
            }

            return !string.IsNullOrWhiteSpace(message);
        }

        private void ClearClientDisconnectRequest()
        {
            lock (_clientDisconnectLock)
            {
                _pendingClientDisconnectMessage = null;
            }
        }

        private void StartEventStreamIfEnabled(AuthModel auth, CancellationToken cancellationToken)
        {
            _isEventStreamAvailable = false;
            _isEventStreamConnecting = false;

            if (!_champSelectSettings.UseChampSelectEventStream)
            {
                _eventStream?.Dispose();
                _eventStream = null;
                _eventStreamTask = null;
                ResetEventStreamState();
                return;
            }

            _nextEventStreamRetryAtUtc = DateTime.MinValue;
        }

        private void SyncEventStreamState(AuthModel? auth, CancellationToken cancellationToken)
        {
            if (!_champSelectSettings.UseChampSelectEventStream)
            {
                if (_eventStream is not null)
                {
                    _eventStream.Dispose();
                    _eventStream = null;
                    _eventStreamTask = null;
                    ResetEventStreamState();
                    ClearPendingLcuEvents();
                    Log("Live LCU events disabled. Using regular polling.");
                }

                return;
            }

            if (auth is null || !_hasReceivedPhaseResponse || _eventStream is not null || _isEventStreamConnecting)
                return;

            if (_nextEventStreamRetryAtUtc > DateTime.UtcNow)
                return;

            StartEventStream(auth, cancellationToken);
        }

        private void StartEventStream(AuthModel auth, CancellationToken cancellationToken)
        {
            _eventStream?.Dispose();
            _isEventStreamAvailable = false;
            _isEventStreamConnecting = true;
            _eventStream = new Lcu.LeagueClientEventStream(auth, OnLcuEventReceived, OnEventStreamConnected, Log);
            var eventStream = _eventStream;

            _eventStreamTask = Task.Run(async () =>
            {
                try
                {
                    await eventStream.RunAsync(cancellationToken);

                    if (!cancellationToken.IsCancellationRequested
                        && _champSelectSettings.UseChampSelectEventStream
                        && ReferenceEquals(_eventStream, eventStream))
                    {
                        ScheduleEventStreamRetry("LCU websocket event stream closed.");
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    if (cancellationToken.IsCancellationRequested
                        || !_champSelectSettings.UseChampSelectEventStream
                        || !ReferenceEquals(_eventStream, eventStream))
                        return;

                    ScheduleEventStreamRetry($"LCU websocket event stream unavailable. {ex.Message}");
                }
            }, CancellationToken.None);
        }

        private void OnEventStreamConnected()
        {
            _isEventStreamConnecting = false;
            _isEventStreamAvailable = true;
            _eventStreamRetryAttempt = 0;
            _nextEventStreamRetryAtUtc = DateTime.MinValue;
            SignalLcuEvent();
        }

        private void ScheduleEventStreamRetry(string reason)
        {
            if (_processManager.GetLeagueAuth() is null)
            {
                RequestClientDisconnect("League Client process closed. Waiting for League Client process...");
                return;
            }

            _eventStream?.Dispose();
            _eventStream = null;
            _eventStreamTask = null;
            _isEventStreamConnecting = false;
            _isEventStreamAvailable = false;

            TimeSpan retryDelay = GetEventStreamRetryDelay(_eventStreamRetryAttempt);
            _eventStreamRetryAttempt++;
            _nextEventStreamRetryAtUtc = DateTime.UtcNow.Add(retryDelay);

            Log($"{EnsureTrailingSentencePunctuation(reason)} Using regular polling and retrying live events in {FormatRetryDelay(retryDelay)}.");
            SignalLcuEvent();
        }

        private void ResetEventStreamState()
        {
            _isEventStreamConnecting = false;
            _isEventStreamAvailable = false;
            _eventStreamRetryAttempt = 0;
            _nextEventStreamRetryAtUtc = DateTime.MinValue;
            _hasReceivedPhaseResponse = false;
        }

        private static TimeSpan GetEventStreamRetryDelay(int retryAttempt)
        {
            int index = Math.Clamp(retryAttempt, 0, EventStreamRetryDelays.Length - 1);
            return EventStreamRetryDelays[index];
        }

        private static string FormatRetryDelay(TimeSpan delay)
        {
            return delay.TotalSeconds < 1
                ? $"{delay.TotalMilliseconds:0} ms"
                : $"{delay.TotalSeconds:0} seconds";
        }

        private static string EnsureTrailingSentencePunctuation(string text)
        {
            string trimmedText = text.TrimEnd();
            return trimmedText.EndsWith('.') ? trimmedText : $"{trimmedText}.";
        }

        private void OnLcuEventReceived(Lcu.LeagueClientEvent apiEvent)
        {
            if (!_champSelectSettings.UseChampSelectEventStream)
                return;

            if (TryCaptureLcuEvent(apiEvent))
                SignalLcuEvent();
        }

        private bool TryCaptureLcuEvent(Lcu.LeagueClientEvent apiEvent)
        {
            if (string.Equals(apiEvent.Uri, "/lol-gameflow/v1/gameflow-phase", StringComparison.OrdinalIgnoreCase)
                || string.Equals(apiEvent.Uri, "/lol-gameflow/v1/session", StringComparison.OrdinalIgnoreCase))
            {
                bool isGameflowSession = string.Equals(apiEvent.Uri, "/lol-gameflow/v1/session", StringComparison.OrdinalIgnoreCase);

                if (TryGetPhaseFromEvent(apiEvent, out var phase))
                {
                    lock (_pendingLcuEventsLock)
                    {
                        _pendingEventPhase = phase;
                        if (isGameflowSession && IsJsonObject(apiEvent.DataJson))
                            _pendingGameflowSessionJson = apiEvent.DataJson;
                    }
                }
                else if (isGameflowSession && IsJsonObject(apiEvent.DataJson))
                {
                    lock (_pendingLcuEventsLock)
                    {
                        _pendingGameflowSessionJson = apiEvent.DataJson;
                    }
                }

                return true;
            }

            if (string.Equals(apiEvent.Uri, "/lol-lobby/v2/lobby", StringComparison.OrdinalIgnoreCase))
            {
                lock (_pendingLcuEventsLock)
                {
                    _pendingLobbyJson = IsDeleteEvent(apiEvent) || !IsJsonObject(apiEvent.DataJson)
                        ? null
                        : apiEvent.DataJson;
                }

                return true;
            }

            if (string.Equals(apiEvent.Uri, "/lol-matchmaking/v1/ready-check", StringComparison.OrdinalIgnoreCase))
            {
                if (IsReadyCheckInProgress(apiEvent))
                {
                    lock (_pendingLcuEventsLock)
                    {
                        _pendingEventPhase = ClientPhase.ReadyCheck;
                        _pendingReadyCheckJson = apiEvent.DataJson;
                    }
                }

                return true;
            }

            if (!string.Equals(apiEvent.Uri, "/lol-champ-select/v1/session", StringComparison.OrdinalIgnoreCase))
                return false;

            lock (_pendingLcuEventsLock)
            {
                if (IsDeleteEvent(apiEvent))
                {
                    _pendingEventPhase = ClientPhase.Unknown;
                    _pendingChampSelectSessionJson = null;
                    _pendingChampSelectSessionObservedAtUtc = DateTime.MinValue;
                }
                else if (IsJsonObject(apiEvent.DataJson))
                {
                    _pendingEventPhase = IsChampSelectFlow(_lastObservedPhase)
                        ? _lastObservedPhase
                        : ClientPhase.ChampSelect;
                    _pendingChampSelectSessionJson = apiEvent.DataJson;
                    _pendingChampSelectSessionObservedAtUtc = DateTime.UtcNow;
                }
            }

            return true;
        }

        private LcuEventSnapshot ConsumePendingLcuEvents()
        {
            lock (_pendingLcuEventsLock)
            {
                var snapshot = new LcuEventSnapshot(
                    _pendingEventPhase,
                    _pendingChampSelectSessionJson,
                    _pendingChampSelectSessionObservedAtUtc,
                    _pendingReadyCheckJson,
                    _pendingGameflowSessionJson,
                    _pendingLobbyJson);

                _pendingEventPhase = null;
                _pendingChampSelectSessionJson = null;
                _pendingChampSelectSessionObservedAtUtc = DateTime.MinValue;
                _pendingReadyCheckJson = null;
                _pendingGameflowSessionJson = null;
                _pendingLobbyJson = null;
                return snapshot;
            }
        }

        private void ClearPendingLcuEvents()
        {
            lock (_pendingLcuEventsLock)
            {
                _pendingEventPhase = null;
                _pendingChampSelectSessionJson = null;
                _pendingChampSelectSessionObservedAtUtc = DateTime.MinValue;
                _pendingReadyCheckJson = null;
                _pendingGameflowSessionJson = null;
                _pendingLobbyJson = null;
            }
        }

        private async Task<QueueSupportState> ResolveQueueSupportStateAsync(
            Lcu.LeagueClientHttp http,
            LcuEventSnapshot eventSnapshot,
            ClientPhase phase,
            CancellationToken cancellationToken)
        {
            if (phase is ClientPhase.Unknown or ClientPhase.InGame)
            {
                ResetQueueSupportState();
                return QueueSupportState.Unknown;
            }

            if (TryGetQueueSupportStateFromEventSnapshot(eventSnapshot, phase, out var eventState))
                return CacheQueueSupportState(eventState);

            if (phase is ClientPhase.Lobby or ClientPhase.Matchmaking or ClientPhase.ReadyCheck)
            {
                if (await TryGetLobbyQueueSupportStateAsync(http, cancellationToken) is { HasQueue: true } lobbyState)
                    return CacheQueueSupportState(lobbyState);
            }

            if (IsChampSelectFlow(phase) || phase == ClientPhase.ReadyCheck)
            {
                if (await TryGetGameflowQueueSupportStateAsync(http, cancellationToken) is { HasQueue: true } gameflowState)
                    return CacheQueueSupportState(gameflowState);
            }

            return _lastQueueSupportState;
        }

        private static bool TryGetQueueSupportStateFromEventSnapshot(
            LcuEventSnapshot eventSnapshot,
            ClientPhase phase,
            out QueueSupportState queueSupportState)
        {
            try
            {
                if (IsChampSelectFlow(phase))
                {
                    return TryParseGameflowQueueSupportState(eventSnapshot.GameflowSessionJson, out queueSupportState)
                        || TryParseLobbyQueueSupportState(eventSnapshot.LobbyJson, out queueSupportState);
                }

                return TryParseLobbyQueueSupportState(eventSnapshot.LobbyJson, out queueSupportState)
                    || TryParseGameflowQueueSupportState(eventSnapshot.GameflowSessionJson, out queueSupportState);
            }
            catch (JsonException)
            {
                queueSupportState = QueueSupportState.Unknown;
                return false;
            }
        }

        private async Task<QueueSupportState?> TryGetLobbyQueueSupportStateAsync(
            Lcu.LeagueClientHttp http,
            CancellationToken cancellationToken)
        {
            try
            {
                string lobbyJson = await http.GetLobbyAsync(cancellationToken);
                return TryParseLobbyQueueSupportState(lobbyJson, out var state)
                    ? state
                    : QueueSupportState.Unknown;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                return null;
            }
            catch (JsonException)
            {
                return QueueSupportState.Unknown;
            }
        }

        private async Task<QueueSupportState?> TryGetGameflowQueueSupportStateAsync(
            Lcu.LeagueClientHttp http,
            CancellationToken cancellationToken)
        {
            try
            {
                string gameflowJson = await http.GetGameflowSessionAsync(cancellationToken);
                return TryParseGameflowQueueSupportState(gameflowJson, out var state)
                    ? state
                    : QueueSupportState.Unknown;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                return null;
            }
            catch (JsonException)
            {
                return QueueSupportState.Unknown;
            }
        }

        private QueueSupportState CacheQueueSupportState(QueueSupportState queueSupportState)
        {
            if (queueSupportState.HasQueue)
                _lastQueueSupportState = queueSupportState;

            return queueSupportState;
        }

        private void ResetQueueSupportState()
        {
            _lastQueueSupportState = QueueSupportState.Unknown;
            _lastUnsupportedQueueLogKey = string.Empty;
        }

        private static bool TryParseLobbyQueueSupportState(string? lobbyJson, out QueueSupportState queueSupportState)
        {
            queueSupportState = QueueSupportState.Unknown;
            if (string.IsNullOrWhiteSpace(lobbyJson))
                return false;

            using var document = JsonDocument.Parse(lobbyJson);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (root.TryGetProperty("gameConfig", out var gameConfig)
                && gameConfig.ValueKind == JsonValueKind.Object
                && TryReadInt32(gameConfig, "queueId", out int gameConfigQueueId))
            {
                queueSupportState = CreateQueueSupportState(gameConfigQueueId, TryReadQueueDescription(gameConfig));
                return true;
            }

            if (TryReadInt32(root, "queueId", out int rootQueueId))
            {
                queueSupportState = CreateQueueSupportState(rootQueueId, TryReadQueueDescription(root));
                return true;
            }

            return false;
        }

        private static bool TryParseGameflowQueueSupportState(string? gameflowJson, out QueueSupportState queueSupportState)
        {
            queueSupportState = QueueSupportState.Unknown;
            if (string.IsNullOrWhiteSpace(gameflowJson))
                return false;

            using var document = JsonDocument.Parse(gameflowJson);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (root.TryGetProperty("gameData", out var gameData)
                && gameData.ValueKind == JsonValueKind.Object)
            {
                if (gameData.TryGetProperty("queue", out var queue)
                    && queue.ValueKind == JsonValueKind.Object
                    && TryReadQueueId(queue, out int queueObjectId))
                {
                    queueSupportState = CreateQueueSupportState(queueObjectId, TryReadQueueDescription(queue));
                    return true;
                }

                if (TryReadInt32(gameData, "queueId", out int gameDataQueueId))
                {
                    queueSupportState = CreateQueueSupportState(gameDataQueueId, TryReadQueueDescription(gameData));
                    return true;
                }
            }

            if (TryReadInt32(root, "queueId", out int rootQueueId))
            {
                queueSupportState = CreateQueueSupportState(rootQueueId, TryReadQueueDescription(root));
                return true;
            }

            return false;
        }

        private static bool TryReadQueueId(JsonElement queue, out int queueId)
        {
            return TryReadInt32(queue, "id", out queueId)
                || TryReadInt32(queue, "queueId", out queueId);
        }

        private static bool TryReadInt32(JsonElement element, string propertyName, out int value)
        {
            value = 0;
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty(propertyName, out var property))
            {
                return false;
            }

            if (property.ValueKind == JsonValueKind.Number)
                return property.TryGetInt32(out value);

            if (property.ValueKind == JsonValueKind.String
                && int.TryParse(property.GetString(), out value))
            {
                return true;
            }

            return false;
        }

        private static string TryReadQueueDescription(JsonElement element)
        {
            foreach (string propertyName in new[] { "description", "name", "shortName", "queueName", "gameMode" })
            {
                if (element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty(propertyName, out var property)
                    && property.ValueKind == JsonValueKind.String)
                {
                    string? value = property.GetString();
                    if (!string.IsNullOrWhiteSpace(value)
                        && !string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
                    {
                        return value.Trim();
                    }
                }
            }

            return string.Empty;
        }

        private static QueueSupportState CreateQueueSupportState(int queueId, string queueDescription)
        {
            bool isSupported = SupportedQueueNames.ContainsKey(queueId);
            string queueName = GetQueueName(queueId, queueDescription);
            return new QueueSupportState(queueId, queueName, true, isSupported);
        }

        private static string GetQueueName(int queueId, string queueDescription)
        {
            if (SupportedQueueNames.TryGetValue(queueId, out string? supportedName))
                return supportedName;

            if (KnownQueueNames.TryGetValue(queueId, out string? knownName))
                return knownName;

            return !string.IsNullOrWhiteSpace(queueDescription)
                ? queueDescription.Trim()
                : $"Queue {queueId}";
        }

        private static DashboardStatus BuildNonChampSelectDashboardStatus(QueueSupportState queueSupportState)
        {
            return ApplyQueueSupportWarning(new DashboardStatus(), queueSupportState);
        }

        private static DashboardStatus BuildUnsupportedModeDashboardStatus(QueueSupportState queueSupportState)
        {
            return ApplyQueueSupportWarning(new DashboardStatus(), queueSupportState);
        }

        private static DashboardStatus ApplyQueueSupportWarning(DashboardStatus status, QueueSupportState queueSupportState)
        {
            if (!queueSupportState.IsUnsupported)
                return status;

            return status with
            {
                IsUnsupportedMode = true,
                UnsupportedQueueText = queueSupportState.QueueName,
                UnsupportedModeText = FormatUnsupportedModeText(queueSupportState)
            };
        }

        private static string FormatUnsupportedQueueLogText(QueueSupportState queueSupportState)
        {
            return queueSupportState.QueueId is int queueId
                ? $"{queueSupportState.QueueName} (queue {queueId})"
                : queueSupportState.QueueName;
        }

        private static string FormatUnsupportedModeText(QueueSupportState queueSupportState)
        {
            string modeText = FormatUnsupportedQueueLogText(queueSupportState);
            return $"{modeText} is not supported for draft tools. Use Normal Draft, Ranked Solo/Duo, or Ranked Flex; auto-accept can still work here.";
        }

        private void LogUnsupportedQueueIfNeeded(QueueSupportState queueSupportState)
        {
            string logKey = queueSupportState.QueueId?.ToString() ?? queueSupportState.QueueName;
            if (string.Equals(_lastUnsupportedQueueLogKey, logKey, StringComparison.Ordinal))
                return;

            _lastUnsupportedQueueLogKey = logKey;
            Log($"Unsupported queue detected: {FormatUnsupportedModeText(queueSupportState)}");
        }

        private async Task<ClientPhase> ResolveCurrentPhaseAsync(
            Lcu.LeagueClientHttp http,
            LcuEventSnapshot eventSnapshot,
            CancellationToken cancellationToken)
        {
            if (eventSnapshot.Phase is ClientPhase eventPhase)
                return eventPhase;

            if (eventSnapshot.HasChampSelectSession)
            {
                return IsChampSelectFlow(_lastObservedPhase)
                    ? _lastObservedPhase
                    : ClientPhase.ChampSelect;
            }

            if (IsChampSelectFlow(_lastObservedPhase))
                return _lastObservedPhase;

            var result = await TryGetCurrentPhaseAsync(http, cancellationToken);
            if (result.ReceivedPhase)
            {
                _hasReceivedPhaseResponse = true;
                return result.Phase;
            }

            return ClientPhase.Unknown;
        }

        private static bool TryGetPhaseFromEvent(Lcu.LeagueClientEvent apiEvent, out ClientPhase phase)
        {
            phase = ClientPhase.Unknown;

            if (string.Equals(apiEvent.Uri, "/lol-gameflow/v1/gameflow-phase", StringComparison.OrdinalIgnoreCase))
                return TryParseClientPhase(apiEvent.DataJson.Trim().Trim('"'), out phase);

            try
            {
                using var document = JsonDocument.Parse(apiEvent.DataJson);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("phase", out var phaseProperty))
                {
                    return TryParseClientPhase(phaseProperty.GetString(), out phase);
                }
            }
            catch (JsonException)
            {
            }

            return false;
        }

        private static bool IsReadyCheckInProgress(Lcu.LeagueClientEvent apiEvent)
        {
            if (IsDeleteEvent(apiEvent))
                return false;

            try
            {
                using var document = JsonDocument.Parse(apiEvent.DataJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return false;

                if (!document.RootElement.TryGetProperty("state", out var stateProperty))
                    return true;

                string? state = stateProperty.GetString();
                return string.IsNullOrWhiteSpace(state)
                    || string.Equals(state, "InProgress", StringComparison.OrdinalIgnoreCase);
            }
            catch (JsonException)
            {
                return true;
            }
        }

        private static bool IsJsonObject(string json)
        {
            string trimmedJson = json.TrimStart();
            return trimmedJson.StartsWith('{');
        }

        private static bool IsDeleteEvent(Lcu.LeagueClientEvent apiEvent)
        {
            return string.Equals(apiEvent.EventType, "Delete", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSameAuth(AuthModel? left, AuthModel? right)
        {
            return left is not null
                && right is not null
                && string.Equals(left.Port, right.Port, StringComparison.Ordinal)
                && string.Equals(left.Base64Token, right.Base64Token, StringComparison.Ordinal);
        }

        private async Task WaitForLoopDelayAsync(int? delayMs, CancellationToken cancellationToken)
        {
            if (delayMs.HasValue && delayMs.Value <= 0)
                return;

            Task eventTask;
            lock (_lcuEventSignalLock)
            {
                eventTask = _lcuEventSignal.Task;
            }

            Task delayTask = delayMs.HasValue
                ? Task.Delay(delayMs.Value, cancellationToken)
                : Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            Task completedTask = await Task.WhenAny(delayTask, eventTask);
            cancellationToken.ThrowIfCancellationRequested();

            if (!ReferenceEquals(completedTask, eventTask))
                return;

            lock (_lcuEventSignalLock)
            {
                if (ReferenceEquals(_lcuEventSignal.Task, eventTask))
                    _lcuEventSignal = CreateEventSignal();
            }
        }

        private void SignalLcuEvent()
        {
            lock (_lcuEventSignalLock)
            {
                _lcuEventSignal.TrySetResult();
            }
        }

        private void OnSettingsSaved()
        {
            if (_lastObservedPhase == ClientPhase.ReadyCheck
                && !_champSelectSettings.IsInQueueAutomationActive())
            {
                _phaseHandlers.OfType<ReadyCheck>().FirstOrDefault()?.CancelPendingAccept();
            }

            if (!_isShutdownRequested)
                SignalLcuEvent();
        }

        private void ResetLcuEventSignal()
        {
            lock (_lcuEventSignalLock)
            {
                _lcuEventSignal = CreateEventSignal();
            }
        }

        private static TaskCompletionSource CreateEventSignal()
        {
            return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private int? GetLoopDelayMs()
        {
            if (_champSelectSettings.UseChampSelectEventStream
                && _isEventStreamAvailable)
            {
                if (!_champSelectSettings.ChampSelectEventFallbackPollingEnabled)
                    return null;

                return Math.Clamp(
                    _champSelectSettings.ChampSelectEventFallbackPollIntervalMs,
                    1000,
                    30000);
            }

            // Regular polling covers phase detection, ready checks, and champ select when live events are unavailable or disabled.
            return Math.Clamp(_champSelectSettings.ChampSelectPollIntervalMs, 100, 5000);
        }

        private void InitializeHandlers(Lcu.LeagueClientHttp http)
        {
            _phaseHandlers.Clear();
            _phaseHandlers.Add(new ReadyCheck(http, _champSelectSettings, Log));
            _phaseHandlers.Add(new ChampSelect(http, _champSelectSettings, Log, SignalLcuEvent, HandleSoundAlertPlayback));
        }

        private void Log(string message)
        {
            if (!_isShutdownRequested)
                _logsPage.WriteLine(message);

            Console.WriteLine(message);
        }

        private void LogError(string message)
        {
            if (!_isShutdownRequested)
                _logsPage.WriteErrorLine(message);

            Console.Error.WriteLine(message);
        }

        private async Task WaitForEventStreamTaskToStopAsync(Task eventStreamTask, CancellationToken cancellationToken)
        {
            try
            {
                Task completedTask = await Task.WhenAny(
                        eventStreamTask,
                        Task.Delay(EventStreamStopTimeout, CancellationToken.None))
                    .ConfigureAwait(false);

                if (!ReferenceEquals(completedTask, eventStreamTask))
                    return;

                await eventStreamTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // If cancellation was requested, we don't consider it an error.
            }
            catch (Exception ex)
            {
                if (!_isShutdownRequested && !cancellationToken.IsCancellationRequested)
                    LogError($"LCU websocket event stream stopped with an error. {ex.Message}");
            }
        }

        private async Task<bool> TryHandleChampSelectAsync(ChampSelect champSelect, LcuEventSnapshot eventSnapshot, CancellationToken cancellationToken)
        {
            try
            {
                if (eventSnapshot.HasChampSelectSession)
                {
                    await champSelect.HandleSessionJsonAsync(
                        eventSnapshot.ChampSelectSessionJson!,
                        eventSnapshot.ChampSelectSessionObservedAtUtc,
                        cancellationToken);
                }
                else
                {
                    await champSelect.HandleAsync(cancellationToken);
                }

                fPhaseProgressionPage.UpdateDashboardStatus(champSelect.LastDashboardStatus);
                return true;
            }
            catch (HttpRequestException ex)
            {
                Log($"Champ Select session unavailable. Returning to phase detection. {ex.Message}");
                _lastObservedPhase = ClientPhase.Unknown;
                _lastHandledPhase = ClientPhase.Unknown;
                fPhaseProgressionPage.UpdatePhase(ClientPhase.Unknown);
                fPhaseProgressionPage.UpdateDashboardStatus(new DashboardStatus());
                champSelect.Reset();
                return false;
            }
        }

        private async Task UpdateReadyCheckDashboardStatusAsync(
            Lcu.LeagueClientHttp http,
            LcuEventSnapshot eventSnapshot,
            QueueSupportState queueSupportState,
            CancellationToken cancellationToken)
        {
            string? readyCheckJson = eventSnapshot.HasReadyCheckJson
                ? eventSnapshot.ReadyCheckJson
                : null;

            if (string.IsNullOrWhiteSpace(readyCheckJson))
            {
                try
                {
                    readyCheckJson = await http.GetReadyCheckAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (HttpRequestException)
                {
                    fPhaseProgressionPage.UpdateDashboardStatus(BuildNonChampSelectDashboardStatus(queueSupportState));
                    return;
                }
            }

            var readyCheckHandler = _phaseHandlers.OfType<ReadyCheck>().FirstOrDefault();
            string readyCheckResponse = GetReadyCheckResponse(readyCheckJson);
            if (!string.IsNullOrWhiteSpace(readyCheckResponse)
                || !_champSelectSettings.IsInQueueAutomationActive())
            {
                readyCheckHandler?.CancelPendingAccept();
            }

            ReadyCheck.AutoAcceptCountdownSnapshot countdown = string.IsNullOrWhiteSpace(readyCheckResponse)
                ? readyCheckHandler?.GetPendingAutoAcceptCountdown() ?? ReadyCheck.AutoAcceptCountdownSnapshot.Empty
                : ReadyCheck.AutoAcceptCountdownSnapshot.Empty;

            DashboardStatus status = new DashboardStatus
            {
                ReadyCheckResponse = readyCheckResponse,
                ReadyCheckAutoAcceptDelayMilliseconds = countdown.TotalDelayMilliseconds,
                ReadyCheckAutoAcceptTimeLeftMilliseconds = countdown.RemainingMilliseconds,
                ReadyCheckAutoAcceptObservedAtUtc = countdown.ObservedAtUtc
            };

            fPhaseProgressionPage.UpdateDashboardStatus(ApplyQueueSupportWarning(status, queueSupportState));
        }

        private static string GetReadyCheckResponse(string readyCheckJson)
        {
            try
            {
                using var document = JsonDocument.Parse(readyCheckJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("playerResponse", out JsonElement playerResponseProperty)
                    || playerResponseProperty.ValueKind != JsonValueKind.String)
                {
                    return string.Empty;
                }

                string? playerResponse = playerResponseProperty.GetString();
                if (string.Equals(playerResponse, "Accepted", StringComparison.OrdinalIgnoreCase))
                    return "Accepted";

                if (string.Equals(playerResponse, "Declined", StringComparison.OrdinalIgnoreCase))
                    return "Declined";
            }
            catch (JsonException)
            {
            }

            return string.Empty;
        }

        private static async Task<(bool ReceivedPhase, ClientPhase Phase)> TryGetCurrentPhaseAsync(Lcu.LeagueClientHttp http, CancellationToken cancellationToken)
        {
            try
            {
                string responseBody = await http.GetGameflowPhaseAsync(cancellationToken);
                string trimmedResponseBody = responseBody.Trim();

                if (!trimmedResponseBody.StartsWith('"')
                    && TryParseClientPhase(trimmedResponseBody.Trim('"'), out var rawPhase))
                {
                    return (true, rawPhase);
                }

                using var doc = JsonDocument.Parse(trimmedResponseBody);
                if (doc.RootElement.ValueKind == JsonValueKind.String)
                {
                    string? phaseStr = doc.RootElement.GetString();
                    if (TryParseClientPhase(phaseStr, out var phase))
                        return (true, phase);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch { }

            return (false, ClientPhase.Unknown);
        }

        private static bool TryParseClientPhase(string? phaseText, out ClientPhase phase)
        {
            phase = ClientPhase.Unknown;
            if (string.IsNullOrWhiteSpace(phaseText))
                return false;

            if (Enum.TryParse(phaseText, true, out phase))
                return true;

            if (string.Equals(phaseText, "InProgress", StringComparison.OrdinalIgnoreCase))
            {
                phase = ClientPhase.InGame;
                return true;
            }

            if (string.Equals(phaseText, "None", StringComparison.OrdinalIgnoreCase))
            {
                phase = ClientPhase.Unknown;
                return true;
            }

            return false;
        }

        private static bool IsChampSelectFlow(ClientPhase phase)
        {
            return phase is ClientPhase.ChampSelect or ClientPhase.Planning;
        }

        private void PlayPhaseSoundAlert(ClientPhase previousPhase, ClientPhase phase)
        {
            TryPlayChampSelectDodgeSoundAlert(previousPhase, phase);

            string? alertId = phase switch
            {
                ClientPhase.ReadyCheck => SoundAlertIds.ReadyCheck,
                ClientPhase.ChampSelect => SoundAlertIds.ChampSelectStart,
                _ => null
            };

            if (alertId is null)
                return;

            PlaySoundAlert(alertId, $"Phase {phase} sound alert");
        }

        private void TryPlayChampSelectDodgeSoundAlert(ClientPhase previousPhase, ClientPhase phase)
        {
            if (_hasPendingChampSelectExitSound && phase != ClientPhase.Unknown)
            {
                if (IsChampSelectDodgeReturnPhase(phase))
                    PlaySoundAlert(SoundAlertIds.ChampSelectEnded, "Champion select dodge sound alert");

                _hasPendingChampSelectExitSound = false;
            }

            if (!IsChampSelectFlow(previousPhase) || IsChampSelectFlow(phase))
                return;

            if (phase == ClientPhase.Unknown)
            {
                _hasPendingChampSelectExitSound = true;
                return;
            }

            if (IsChampSelectDodgeReturnPhase(phase))
                PlaySoundAlert(SoundAlertIds.ChampSelectEnded, "Champion select dodge sound alert");
        }

        private static bool IsChampSelectDodgeReturnPhase(ClientPhase phase)
        {
            return phase is ClientPhase.Lobby or ClientPhase.Matchmaking or ClientPhase.ReadyCheck;
        }

        private void PlaySoundAlert(string alertId, string context)
        {
            PlaySoundAlert(alertId, context, playbackDurationSecondsOverride: null, channelKey: null);
        }

        private void HandleSoundAlertPlayback(SoundAlertPlaybackRequest request)
        {
            switch (request.Command)
            {
                case SoundAlertPlaybackCommand.StopChannel:
                    _notificationSoundPlayer.StopChannel(request.ChannelKey);
                    return;
                case SoundAlertPlaybackCommand.PreloadAlert:
                    PreloadSoundAlert(request.AlertId, request.Context);
                    return;
                case SoundAlertPlaybackCommand.PlayAlert:
                    if (request.AlertId is not null)
                    {
                        PlaySoundAlert(
                            request.AlertId,
                            request.Context,
                            request.PlaybackDurationSeconds,
                            request.ChannelKey);
                    }

                    return;
            }
        }

        private void PreloadSoundAlert(string? alertId, string context)
        {
            if (alertId is null || !_champSelectSettings.IsSoundAlertActive(alertId))
                return;

            _notificationSoundPlayer.PreloadAlert(
                _champSelectSettings.GetSoundAlertSoundKey(alertId),
                context);
        }

        private void PlaySoundAlert(string alertId, string context, int? playbackDurationSecondsOverride, string? channelKey)
        {
            if (!_champSelectSettings.IsSoundAlertActive(alertId))
                return;

            _notificationSoundPlayer.PlayAlert(
                _champSelectSettings.GetSoundAlertSoundKey(alertId),
                _champSelectSettings.GetSoundAlertEffectiveVolumePercent(alertId),
                context,
                new NotificationSoundPlaybackOptions(
                    playbackDurationSecondsOverride ?? _champSelectSettings.GetSoundAlertPlaybackDurationSeconds(alertId),
                    channelKey));
        }

        private static string? GetActionMessage(ClientPhase phase)
        {
            return phase switch
            {
                ClientPhase.ReadyCheck => "Ready check detected.",
                ClientPhase.ChampSelect => "Champion Select is now active. Automation will follow your settings.",
                _ => null
            };
        }
    }
}
