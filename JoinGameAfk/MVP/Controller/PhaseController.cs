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

        private readonly record struct LcuEventSnapshot(
            ClientPhase? Phase,
            string? ChampSelectSessionJson,
            DateTime ChampSelectSessionObservedAtUtc)
        {
            public bool HasChampSelectSession => !string.IsNullOrWhiteSpace(ChampSelectSessionJson);
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
        private string? _pendingClientDisconnectMessage;
        private bool _isRunning;
        private ClientPhase _lastObservedPhase;
        private ClientPhase _lastHandledPhase;
        private bool _isClientConnected;
        private bool _isEventStreamConnecting;
        private bool _isEventStreamAvailable;
        private int _eventStreamRetryAttempt;
        private DateTime _nextEventStreamRetryAtUtc;
        private bool _hasReceivedPhaseResponse;
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
            ResetEventStreamState();
            _isWaitingForClient = false;
            ResetLcuEventSignal();
            ClearClientDisconnectRequest();
            ClearPendingLcuEvents();

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
            ClearClientDisconnectRequest();
            ClearPendingLcuEvents();
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
                            Log($"Phase changed: {_lastObservedPhase} -> {phase}");
                            PlayPhaseSoundAlert(phase);

                            _lastObservedPhase = phase;
                            _lastHandledPhase = ClientPhase.Unknown;

                            var champSelectHandler = _phaseHandlers.OfType<ChampSelect>().FirstOrDefault();
                            champSelectHandler?.Reset();
                        }

                        var handler = _phaseHandlers.FirstOrDefault(h => h.ClientPhase == phase);
                        var champSelect = _phaseHandlers.OfType<ChampSelect>().FirstOrDefault();
                        bool isChampSelectFlow = IsChampSelectFlow(phase);

                        if (!isChampSelectFlow)
                            fPhaseProgressionPage.UpdateDashboardStatus(new DashboardStatus());

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

            _eventStream?.Dispose();
            _eventStream = null;
            _eventStreamTask = null;
            ResetEventStreamState();
            ClearPendingLcuEvents();
            http?.Dispose();
            http = null;
            currentAuth = null;
            _isClientConnected = false;
            _isWaitingForClient = false;
            _lastObservedPhase = ClientPhase.Unknown;
            _lastHandledPhase = ClientPhase.Unknown;
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
                if (TryGetPhaseFromEvent(apiEvent, out var phase))
                {
                    lock (_pendingLcuEventsLock)
                    {
                        _pendingEventPhase = phase;
                    }
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
                    _pendingChampSelectSessionObservedAtUtc);

                _pendingEventPhase = null;
                _pendingChampSelectSessionJson = null;
                _pendingChampSelectSessionObservedAtUtc = DateTime.MinValue;
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
            }
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
            _phaseHandlers.Add(new ChampSelect(http, _champSelectSettings, Log, SignalLcuEvent, PlaySoundAlert));
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

        private void PlayPhaseSoundAlert(ClientPhase phase)
        {
            string? alertId = phase switch
            {
                ClientPhase.ReadyCheck => SoundAlertIds.ReadyCheck,
                ClientPhase.ChampSelect => SoundAlertIds.ChampSelectStart,
                ClientPhase.Planning => SoundAlertIds.PlanningStart,
                _ => null
            };

            if (alertId is null)
                return;

            PlaySoundAlert(alertId, $"Phase {phase} sound alert");
        }

        private void PlaySoundAlert(string alertId, string context)
        {
            if (!_champSelectSettings.IsSoundAlertActive(alertId))
                return;

            _notificationSoundPlayer.PlayAlert(
                _champSelectSettings.GetSoundAlertSoundKey(alertId),
                ChampSelectSettings.NormalizeSoundAlertVolumePercent(_champSelectSettings.SoundAlertVolumePercent),
                context);
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
