using JoinGameAfk.Enums;
using LcuClient;

namespace JoinGameAfk.Presentation.Controller
{
    public partial class PhaseController
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

        private void StartEventStreamIfEnabled(AuthModel auth, CancellationToken cancellationToken)
        {
            _isEventStreamAvailable = false;
            _isEventStreamConnecting = false;

            if (!_generalSettings.UseChampSelectEventStream)
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
            if (!_generalSettings.UseChampSelectEventStream)
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
                        && _generalSettings.UseChampSelectEventStream
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
                        || !_generalSettings.UseChampSelectEventStream
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
            if (GetLeagueAuth() is null)
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
            if (!_generalSettings.UseChampSelectEventStream)
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
    }
}