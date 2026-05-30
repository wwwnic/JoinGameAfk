using System.Diagnostics;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;
using JoinGameAfk.Phase;
using JoinGameAfk.Plugin.Phase.ReadyCheck;
using LcuClient;

namespace JoinGameAfk.Presentation.Controller
{
    public partial class PhaseController
    {
        private async Task ObserveRunLoopCompletionAsync(Task runLoopTask, CancellationTokenSource cancellationTokenSource)
        {
            try
            {
                await runLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
            {
                // If cancellation was requested, we don't consider it as an error.
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
                        QueueSupportState queueSupportState = await ResolveQueueSupportStateAsync(http, eventSnapshot, phase, ct);
                        phase = GetEffectivePhaseForQueueFlow(phase, queueSupportState);
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
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
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
                && !_generalSettings.IsInQueueAutomationActive())
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
            if (_generalSettings.UseChampSelectEventStream
                && _isEventStreamAvailable)
            {
                if (!_generalSettings.ChampSelectEventFallbackPollingEnabled)
                    return null;

                return Math.Clamp(
                    _generalSettings.ChampSelectEventFallbackPollIntervalMs,
                    1000,
                    30000);
            }

            // Regular polling covers phase detection, ready checks, and champ select when live events are unavailable or disabled.
            return Math.Clamp(_generalSettings.ChampSelectPollIntervalMs, 100, 5000);
        }
    }
}
