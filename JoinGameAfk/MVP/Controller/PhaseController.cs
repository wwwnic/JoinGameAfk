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
    public class PhaseController
    {
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

        private CancellationTokenSource? _cts;
        private Lcu.LeagueClientEventStream? _eventStream;
        private Task? _eventStreamTask;
        private readonly object _lcuEventSignalLock = new();
        private TaskCompletionSource _lcuEventSignal = CreateEventSignal();
        private readonly object _pendingLcuEventsLock = new();
        private ClientPhase? _pendingEventPhase;
        private string? _pendingChampSelectSessionJson;
        private DateTime _pendingChampSelectSessionObservedAtUtc;
        private bool _isRunning;
        private ClientPhase _lastObservedPhase;
        private ClientPhase _lastHandledPhase;
        private bool _isClientConnected;
        private bool _isEventStreamAvailable;
        private bool _eventStreamUnavailableForCurrentClient;
        private bool _isWaitingForClient;

        public bool IsRunning => _isRunning;

        public PhaseController(PhaseProgressionPage phaseProgressionPage, LogsPage logsPage, ChampSelectSettings champSelectSettings)
        {
            fPhaseProgressionPage = phaseProgressionPage;
            _logsPage = logsPage;
            _champSelectSettings = champSelectSettings;
            _notificationSoundPlayer = new NotificationSoundPlayer(LogError);
            _phaseHandlers = [];
            _champSelectSettings.Saved += OnSettingsSaved;
        }

        public void Start()
        {
            if (_isRunning) return;

            _isRunning = true;
            _lastObservedPhase = ClientPhase.Unknown;
            _lastHandledPhase = ClientPhase.Unknown;
            _isClientConnected = false;
            _isEventStreamAvailable = false;
            _eventStreamUnavailableForCurrentClient = false;
            _isWaitingForClient = false;
            ResetLcuEventSignal();
            ClearPendingLcuEvents();

            fPhaseProgressionPage.SetWatcherState(true);
            fPhaseProgressionPage.SetClientConnection(false);
            fPhaseProgressionPage.UpdatePhase(ClientPhase.Unknown);
            fPhaseProgressionPage.UpdateDashboardStatus(new DashboardStatus());

            _cts = new CancellationTokenSource();
            _ = RunLoopAsync(_cts.Token);
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _cts?.Cancel();
            _eventStream?.Dispose();
            _isEventStreamAvailable = false;
            ClearPendingLcuEvents();
            fPhaseProgressionPage.SetWatcherState(false);
            fPhaseProgressionPage.SetClientConnection(false);
            fPhaseProgressionPage.UpdatePhase(ClientPhase.Unknown);
            fPhaseProgressionPage.UpdateDashboardStatus(new DashboardStatus());
        }

        private async Task RunLoopAsync(CancellationToken ct)
        {
            Log("Waiting for League Client process...");

            var processManager = new Lcu.ProcessManager(JoinGameAfkConstant.LeagueClient.ProcessName);
            Lcu.LeagueClientHttp? http = null;
            AuthModel? currentAuth = null;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var iterationStopwatch = Stopwatch.StartNew();

                    try
                    {
                        if (http == null || !http.HasAuthToken())
                        {
                            var auth = processManager.GetLeagueAuth();
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
                            _eventStreamUnavailableForCurrentClient = false;
                            StartEventStreamIfEnabled(auth, ct);
                            InitializeHandlers(http);

                            if (!_isClientConnected)
                            {
                                _isClientConnected = true;
                                Log("Connected to League Client.");
                                fPhaseProgressionPage.SetClientConnection(true);
                            }
                        }

                        SyncEventStreamState(currentAuth, ct);

                        LcuEventSnapshot eventSnapshot = ConsumePendingLcuEvents();
                        ClientPhase phase = await ResolveCurrentPhaseAsync(http, eventSnapshot, ct);
                        fPhaseProgressionPage.UpdatePhase(phase);

                        if (phase != _lastObservedPhase)
                        {
                            Log($"Phase changed: {_lastObservedPhase} -> {phase}");
                            if (ShouldPlayReadyCheckDetectedCue(phase))
                                _notificationSoundPlayer.PlayReadyCheckDetectedCue(_champSelectSettings.ReadyCheckSoundNotificationKey);

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
                        _isClientConnected = false;
                        _isEventStreamAvailable = false;
                        _eventStreamUnavailableForCurrentClient = false;
                        _isWaitingForClient = false;
                        _lastObservedPhase = ClientPhase.Unknown;
                        _lastHandledPhase = ClientPhase.Unknown;
                        fPhaseProgressionPage.SetClientConnection(false);
                        fPhaseProgressionPage.UpdatePhase(ClientPhase.Unknown);
                        fPhaseProgressionPage.UpdateDashboardStatus(new DashboardStatus());
                        _eventStream?.Dispose();
                        _eventStream = null;
                        _eventStreamTask = null;
                        ClearPendingLcuEvents();
                        http?.Dispose();
                        http = null;
                        currentAuth = null;
                        await Task.Delay(5000, ct);
                    }
                }
            }
            finally
            {
                _eventStream?.Dispose();
                _eventStream = null;
                _eventStreamTask = null;
                _isEventStreamAvailable = false;
                ClearPendingLcuEvents();
                http?.Dispose();
                Log("Stopped watching.");
            }
        }

        private void StartEventStreamIfEnabled(AuthModel auth, CancellationToken cancellationToken)
        {
            _isEventStreamAvailable = false;

            if (!_champSelectSettings.UseChampSelectEventStream)
            {
                _eventStream?.Dispose();
                _eventStream = null;
                _eventStreamTask = null;
                _eventStreamUnavailableForCurrentClient = false;
                return;
            }

            StartEventStream(auth, cancellationToken);
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
                    _isEventStreamAvailable = false;
                    _eventStreamUnavailableForCurrentClient = false;
                    ClearPendingLcuEvents();
                    Log("Live LCU events disabled. Using regular polling.");
                }

                return;
            }

            if (auth is null || _eventStream is not null || _eventStreamUnavailableForCurrentClient)
                return;

            StartEventStream(auth, cancellationToken);
        }

        private void StartEventStream(AuthModel auth, CancellationToken cancellationToken)
        {
            _eventStream?.Dispose();
            _isEventStreamAvailable = true;
            _eventStreamUnavailableForCurrentClient = false;
            _eventStream = new Lcu.LeagueClientEventStream(auth, OnLcuEventReceived, Log);
            var eventStream = _eventStream;

            _eventStreamTask = Task.Run(async () =>
            {
                try
                {
                    await eventStream.RunAsync(cancellationToken);

                    if (!cancellationToken.IsCancellationRequested && _champSelectSettings.UseChampSelectEventStream)
                    {
                        _isEventStreamAvailable = false;
                        _eventStreamUnavailableForCurrentClient = true;
                        _eventStream?.Dispose();
                        _eventStream = null;
                        _eventStreamTask = null;
                        Log("LCU websocket event stream closed. Falling back to polling.");
                        SignalLcuEvent();
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    if (cancellationToken.IsCancellationRequested || !_champSelectSettings.UseChampSelectEventStream)
                        return;

                    Log($"LCU websocket event stream unavailable. Falling back to polling. {ex.Message}");
                    _isEventStreamAvailable = false;
                    _eventStreamUnavailableForCurrentClient = true;
                    _eventStream?.Dispose();
                    _eventStream = null;
                    _eventStreamTask = null;
                    SignalLcuEvent();
                }
            }, CancellationToken.None);
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

            return await GetCurrentPhaseAsync(http, cancellationToken);
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
            _phaseHandlers.Add(new ChampSelect(http, _champSelectSettings, Log));
        }

        private void Log(string message)
        {
            _logsPage.WriteLine(message);
            Console.WriteLine(message);
        }

        private void LogError(string message)
        {
            _logsPage.WriteErrorLine(message);
            Console.Error.WriteLine(message);
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

        private static async Task<ClientPhase> GetCurrentPhaseAsync(Lcu.LeagueClientHttp http, CancellationToken cancellationToken)
        {
            try
            {
                string responseBody = await http.GetGameflowPhaseAsync(cancellationToken);
                string trimmedResponseBody = responseBody.Trim();

                if (!trimmedResponseBody.StartsWith('"')
                    && TryParseClientPhase(trimmedResponseBody.Trim('"'), out var rawPhase))
                {
                    return rawPhase;
                }

                using var doc = JsonDocument.Parse(trimmedResponseBody);
                if (doc.RootElement.ValueKind == JsonValueKind.String)
                {
                    string? phaseStr = doc.RootElement.GetString();
                    if (TryParseClientPhase(phaseStr, out var phase))
                        return phase;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch { }

            return ClientPhase.Unknown;
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

        private bool ShouldPlayReadyCheckDetectedCue(ClientPhase phase)
        {
            return phase == ClientPhase.ReadyCheck
                && _champSelectSettings.InQueueAutomationEnabled
                && _champSelectSettings.ReadyCheckSoundNotificationEnabled;
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
