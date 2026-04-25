using System.Text.Json;
using JoinGameAfk.Enums;
using JoinGameAfk.Interface;
using JoinGameAfk.Model;
using static LcuClient.Lcu;

public class ChampSelect : IPhaseHandler
{
    private readonly LeagueClientHttp _http;
    private readonly ChampSelectSettings _settings;
    private readonly Action<string>? _log;
    private string? _lastSessionId;
    private bool _hasPicked;
    private bool _hasBanned;
    private bool _hasHoveredPick;
    private bool _hasHoveredBan;
    private bool _manualPickSelectionOverride;
    private bool _manualBanSelectionOverride;
    private int _hoveredPickChampionId;
    private int _hoveredBanChampionId;
    private bool _hasLoggedSessionSummary;
    private string? _lastPickStatusMessage;
    private string? _lastBanStatusMessage;
    private string? _lastTimerSessionId;
    private string? _lastTimerPhase;
    private long _lastReportedTimeLeftMs;
    private long _lastEffectiveTimeLeftMs;
    private DateTime _lastTimerObservedAtUtc;
    private int _pendingPickHoverActionId;
    private int _pendingBanHoverActionId;
    private DateTime _pickHoverReadyAtUtc;
    private DateTime _banHoverReadyAtUtc;

    public ClientPhase ClientPhase => ClientPhase.ChampSelect;

    public ChampSelect(LeagueClientHttp http, ChampSelectSettings settings, Action<string>? log = null)
    {
        _http = http;
        _settings = settings;
        _log = log;
    }

    public void Handle()
    {
        try
        {
            string json = _http.GetChampSelectSession();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? sessionId = GetSessionId(root);
            if (!string.Equals(_lastSessionId, sessionId, StringComparison.Ordinal))
            {
                Reset();
                _lastSessionId = sessionId;
            }

            if (!TryGetInt32(root, "localPlayerCellId", out int localPlayerCellId))
                return;

            Position assignedPosition = GetAssignedPosition(root, localPlayerCellId);
            var preference = _settings.GetPreference(assignedPosition);
            string champSelectPhase = GetChampSelectPhase(root);
            long totalTimeMs = GetTotalTimeInPhaseMs(root);
            long rawTimeLeftMs = GetAdjustedTimeLeftMs(root);
            long timeLeftMs = GetEffectiveTimeLeftMs(sessionId, champSelectPhase, rawTimeLeftMs);

            if (!_hasLoggedSessionSummary)
            {
                Log($"Champ Select session ready. Position={assignedPosition}, picks=[{FormatChampionIds(preference.PickChampionIds)}], bans=[{FormatChampionIds(preference.BanChampionIds)}], autoLock={_settings.AutoLockSelectionEnabled}, pickLockDelay={_settings.PickLockDelaySeconds}s, banLockDelay={_settings.BanLockDelaySeconds}s.");
                _hasLoggedSessionSummary = true;
            }

            if (!root.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Array)
                return;

            foreach (var actionGroup in actions.EnumerateArray())
            {
                if (actionGroup.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var action in actionGroup.EnumerateArray())
                {
                    if (!TryGetInt32(action, "actorCellId", out int actorCellId) || actorCellId != localPlayerCellId)
                        continue;

                    if (!TryGetBool(action, "completed", out bool completed) || completed)
                        continue;

                    string type = action.TryGetProperty("type", out var typeProperty)
                        ? typeProperty.GetString() ?? string.Empty
                        : string.Empty;

                    if (!TryGetInt32(action, "id", out int actionId))
                        continue;

                    bool isInProgress = TryGetBool(action, "isInProgress", out bool inProgress) && inProgress;
                    int currentChampionId = TryGetInt32(action, "championId", out int championId)
                        ? championId
                        : 0;

                    if (actorCellId != localPlayerCellId)
                        continue;

                    if (type == "pick" && preference.PickChampionIds.Count > 0 && !_hasPicked)
                    {
                        HandlePickAction(actionId, currentChampionId, isInProgress, totalTimeMs, timeLeftMs, preference.PickChampionIds);
                    }
                    else if (type == "ban" && preference.BanChampionIds.Count > 0 && !_hasBanned)
                    {
                        HandleBanAction(actionId, currentChampionId, isInProgress, champSelectPhase, totalTimeMs, timeLeftMs, preference.BanChampionIds);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Champ Select handler error: {ex.Message}");
        }
    }

    public void Reset()
    {
        _lastSessionId = null;
        _hasPicked = false;
        _hasBanned = false;
        _hasHoveredPick = false;
        _hasHoveredBan = false;
        _manualPickSelectionOverride = false;
        _manualBanSelectionOverride = false;
        _hoveredPickChampionId = 0;
        _hoveredBanChampionId = 0;
        _hasLoggedSessionSummary = false;
        _lastPickStatusMessage = null;
        _lastBanStatusMessage = null;
        _lastTimerSessionId = null;
        _lastTimerPhase = null;
        _lastReportedTimeLeftMs = 0;
        _lastEffectiveTimeLeftMs = 0;
        _lastTimerObservedAtUtc = DateTime.MinValue;
        _pendingPickHoverActionId = 0;
        _pendingBanHoverActionId = 0;
        _pickHoverReadyAtUtc = DateTime.MinValue;
        _banHoverReadyAtUtc = DateTime.MinValue;
    }

    private void HandlePickAction(int actionId, int currentChampionId, bool isInProgress, long totalTimeMs, long timeLeftMs, IReadOnlyCollection<int> preferredChampionIds)
    {
        if (currentChampionId == 0 && _hasHoveredPick && !_manualPickSelectionOverride)
        {
            ResetPickHover();
        }
        else if (currentChampionId != 0)
        {
            if (_hasHoveredPick && currentChampionId != _hoveredPickChampionId && !_manualPickSelectionOverride)
            {
                _manualPickSelectionOverride = true;
                LogStatus(ref _lastPickStatusMessage, $"Pick selection changed manually to {FormatChampion(currentChampionId)}. Falling back to last-second auto-lock for your current selection.");
            }

            _hasHoveredPick = true;
            _hoveredPickChampionId = currentChampionId;
        }

        if (!_hasHoveredPick && !_manualPickSelectionOverride)
        {
            if (ShouldAttemptHover(actionId, isPickAction: true, out int hoverDelaySeconds))
            {
                LogStatus(ref _lastPickStatusMessage, $"Pick hover delay elapsed. ActionId={actionId}, currentChampionId={currentChampionId}, inProgress={isInProgress}, timeLeft={FormatTimeLeft(timeLeftMs)}. Attempting hover.");
                TryHoverChampion(actionId, preferredChampionIds, isPickAction: true, actionLabel: "Pick");
            }
            else
            {
                LogStatus(ref _lastPickStatusMessage, $"Pick action detected. ActionId={actionId}, currentChampionId={currentChampionId}, inProgress={isInProgress}, timeLeft={FormatTimeLeft(timeLeftMs)}. Waiting {hoverDelaySeconds}s before hovering.");
            }
        }

        int championIdToLock = currentChampionId != 0 ? currentChampionId : (_manualPickSelectionOverride ? 0 : _hoveredPickChampionId);

        if (championIdToLock == 0)
        {
            if (_manualPickSelectionOverride)
            {
                LogStatus(ref _lastPickStatusMessage, $"Pick selection was changed manually. Waiting for your current champion selection before auto-locking.");
                return;
            }

            LogStatus(ref _lastPickStatusMessage, $"Pick hover not set yet. ActionId={actionId}. Will retry with remaining configured champions.");
            return;
        }

        if (!isInProgress)
        {
            Log($"Pick hovered {FormatChampion(championIdToLock)}. Waiting for pick action to become active. Current time left: {FormatTimeLeft(timeLeftMs)}.");
            return;
        }

        if (!_settings.AutoLockSelectionEnabled)
        {
            LogStatus(ref _lastPickStatusMessage, $"Pick hovered {FormatChampion(championIdToLock)}. Auto-lock is disabled, so the app will not lock before timer 0.");
            return;
        }

        int pickLockDelaySeconds = GetLockDelaySeconds(_settings.PickLockDelaySeconds, _manualPickSelectionOverride);
        if (!ShouldLock(totalTimeMs, timeLeftMs, pickLockDelaySeconds))
        {
            Log($"Pick hovered {FormatChampion(championIdToLock)}. Waiting to lock at <= {pickLockDelaySeconds}s. Current time left: {FormatTimeLeft(timeLeftMs)}.");
            return;
        }

        try
        {
            LogStatus(ref _lastPickStatusMessage, $"Pick lock window reached. Locking {FormatChampion(championIdToLock)} on actionId={actionId}. Time left: {FormatTimeLeft(timeLeftMs)}.");
            _http.CompleteAction(actionId, championIdToLock);
            _hasPicked = true;
            Log($"Pick locked successfully. Champion={FormatChampion(championIdToLock)}, actionId={actionId}.");
        }
        catch (Exception ex)
        {
            Log($"Pick lock failed for {FormatChampion(championIdToLock)} on actionId={actionId}: {ex.Message}");
            ResetPickHover();
        }
    }

    private void HandleBanAction(int actionId, int currentChampionId, bool isInProgress, string champSelectPhase, long totalTimeMs, long timeLeftMs, IReadOnlyCollection<int> preferredChampionIds)
    {
        if (currentChampionId == 0 && _hasHoveredBan && !_manualBanSelectionOverride)
        {
            ResetBanHover();
        }
        else if (currentChampionId != 0)
        {
            if (_hasHoveredBan && currentChampionId != _hoveredBanChampionId && !_manualBanSelectionOverride)
            {
                _manualBanSelectionOverride = true;
                LogStatus(ref _lastBanStatusMessage, $"Ban selection changed manually to {FormatChampion(currentChampionId)}. Falling back to last-second auto-lock for your current selection.");
            }

            _hasHoveredBan = true;
            _hoveredBanChampionId = currentChampionId;
        }

        if (!_hasHoveredBan && !_manualBanSelectionOverride && !string.Equals(champSelectPhase, "PLANNING", StringComparison.OrdinalIgnoreCase))
        {
            if (ShouldAttemptHover(actionId, isPickAction: false, out int hoverDelaySeconds))
            {
                LogStatus(ref _lastBanStatusMessage, $"Ban hover delay elapsed. ActionId={actionId}, currentChampionId={currentChampionId}, inProgress={isInProgress}, phase={champSelectPhase}, timeLeft={FormatTimeLeft(timeLeftMs)}. Attempting hover.");
                TryHoverChampion(actionId, preferredChampionIds, isPickAction: false, actionLabel: "Ban");
            }
            else
            {
                LogStatus(ref _lastBanStatusMessage, $"Ban action detected. ActionId={actionId}, currentChampionId={currentChampionId}, inProgress={isInProgress}, phase={champSelectPhase}, timeLeft={FormatTimeLeft(timeLeftMs)}. Waiting {hoverDelaySeconds}s before hovering.");
            }
        }

        int championIdToLock = currentChampionId != 0 ? currentChampionId : (_manualBanSelectionOverride ? 0 : _hoveredBanChampionId);

        if (championIdToLock == 0)
        {
            if (_manualBanSelectionOverride)
            {
                LogStatus(ref _lastBanStatusMessage, $"Ban selection was changed manually. Waiting for your current champion selection before auto-locking.");
                return;
            }

            if (!string.Equals(champSelectPhase, "PLANNING", StringComparison.OrdinalIgnoreCase))
            {
                LogStatus(ref _lastBanStatusMessage, $"Ban hover not set yet. ActionId={actionId}. Will retry with remaining configured champions.");
            }

            return;
        }

        if (!isInProgress)
        {
            Log($"Ban hovered {FormatChampion(championIdToLock)}. Waiting for ban action to become active. Current time left: {FormatTimeLeft(timeLeftMs)}.");
            return;
        }

        if (!_settings.AutoLockSelectionEnabled)
        {
            LogStatus(ref _lastBanStatusMessage, $"Ban hovered {FormatChampion(championIdToLock)}. Auto-lock is disabled, so the app will not lock before timer 0.");
            return;
        }

        int banLockDelaySeconds = GetLockDelaySeconds(_settings.BanLockDelaySeconds, _manualBanSelectionOverride);
        if (!ShouldLock(totalTimeMs, timeLeftMs, banLockDelaySeconds))
        {
            Log($"Ban hovered {FormatChampion(championIdToLock)}. Waiting to lock at <= {banLockDelaySeconds}s. Current time left: {FormatTimeLeft(timeLeftMs)}.");
            return;
        }

        try
        {
            LogStatus(ref _lastBanStatusMessage, $"Ban lock window reached. Locking {FormatChampion(championIdToLock)} on actionId={actionId}. Time left: {FormatTimeLeft(timeLeftMs)}.");
            _http.CompleteAction(actionId, championIdToLock);
            _hasBanned = true;
            Log($"Ban locked successfully. Champion={FormatChampion(championIdToLock)}, actionId={actionId}.");
        }
        catch (Exception ex)
        {
            Log($"Ban lock failed for {FormatChampion(championIdToLock)} on actionId={actionId}: {ex.Message}");
            ResetBanHover();
        }
    }

    private void TryHoverChampion(int actionId, IReadOnlyCollection<int> championIds, bool isPickAction, string actionLabel)
    {
        foreach (var championId in championIds)
        {
            try
            {
                Log($"{actionLabel}: trying {FormatChampion(championId)} on actionId={actionId}.");
                _http.HoverChampion(actionId, championId);

                if (isPickAction)
                {
                    _hasHoveredPick = true;
                    _hoveredPickChampionId = championId;
                }
                else
                {
                    _hasHoveredBan = true;
                    _hoveredBanChampionId = championId;
                }

                Log($"{actionLabel}: hover succeeded with {FormatChampion(championId)} on actionId={actionId}.");
                break;
            }
            catch (Exception ex)
            {
                Log($"{actionLabel}: hover failed for {FormatChampion(championId)} on actionId={actionId}: {ex.Message}");
            }
        }
    }

    private void ResetPickHover()
    {
        _hasHoveredPick = false;
        _manualPickSelectionOverride = false;
        _hoveredPickChampionId = 0;
        _pendingPickHoverActionId = 0;
        _pickHoverReadyAtUtc = DateTime.MinValue;
        _lastPickStatusMessage = null;
    }

    private void ResetBanHover()
    {
        _hasHoveredBan = false;
        _manualBanSelectionOverride = false;
        _hoveredBanChampionId = 0;
        _pendingBanHoverActionId = 0;
        _banHoverReadyAtUtc = DateTime.MinValue;
        _lastBanStatusMessage = null;
    }

    private bool ShouldAttemptHover(int actionId, bool isPickAction, out int hoverDelaySeconds)
    {
        hoverDelaySeconds = Math.Max(0, _settings.ChampionHoverDelaySeconds);
        DateTime now = DateTime.UtcNow;

        if (isPickAction)
        {
            if (_pendingPickHoverActionId != actionId)
            {
                _pendingPickHoverActionId = actionId;
                _pickHoverReadyAtUtc = now.AddSeconds(hoverDelaySeconds);
            }

            return now >= _pickHoverReadyAtUtc;
        }

        if (_pendingBanHoverActionId != actionId)
        {
            _pendingBanHoverActionId = actionId;
            _banHoverReadyAtUtc = now.AddSeconds(hoverDelaySeconds);
        }

        return now >= _banHoverReadyAtUtc;
    }

    private static Position GetAssignedPosition(JsonElement root, int localPlayerCellId)
    {
        if (root.TryGetProperty("myTeam", out var myTeam))
        {
            foreach (var member in myTeam.EnumerateArray())
            {
                int cellId = member.GetProperty("cellId").GetInt32();
                if (cellId == localPlayerCellId)
                {
                    string position = member.GetProperty("assignedPosition").GetString() ?? "";
                    return position.ToLowerInvariant() switch
                    {
                        "top" => Position.Top,
                        "jungle" => Position.Jungle,
                        "middle" => Position.Mid,
                        "bottom" => Position.Adc,
                        "utility" => Position.Support,
                        _ => Position.Default
                    };
                }
            }
        }

        return Position.Default;
    }

    private static string GetChampSelectPhase(JsonElement root)
    {
        if (root.TryGetProperty("timer", out var timer)
            && timer.ValueKind == JsonValueKind.Object
            && timer.TryGetProperty("phase", out var phaseProperty))
        {
            return phaseProperty.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static long GetAdjustedTimeLeftMs(JsonElement root)
    {
        if (root.TryGetProperty("timer", out var timer)
            && timer.ValueKind == JsonValueKind.Object
            && timer.TryGetProperty("adjustedTimeLeftInPhase", out var timeLeftProp)
            && timeLeftProp.ValueKind == JsonValueKind.Number)
        {
            // The LCU returns this value as a floating-point number (e.g. 88632.5).
            return (long)timeLeftProp.GetDouble();
        }

        return 0;
    }

    private long GetEffectiveTimeLeftMs(string? sessionId, string champSelectPhase, long rawTimeLeftMs)
    {
        DateTime now = DateTime.UtcNow;

        if (rawTimeLeftMs < 0)
            rawTimeLeftMs = 0;

        bool shouldResetBaseline = !string.Equals(_lastTimerSessionId, sessionId, StringComparison.Ordinal)
            || !string.Equals(_lastTimerPhase, champSelectPhase, StringComparison.Ordinal)
            || _lastTimerObservedAtUtc == DateTime.MinValue
            || rawTimeLeftMs != _lastReportedTimeLeftMs;

        if (shouldResetBaseline)
        {
            _lastTimerSessionId = sessionId;
            _lastTimerPhase = champSelectPhase;
            _lastReportedTimeLeftMs = rawTimeLeftMs;
            _lastEffectiveTimeLeftMs = rawTimeLeftMs;
            _lastTimerObservedAtUtc = now;
            return rawTimeLeftMs;
        }

        long elapsedMs = (long)(now - _lastTimerObservedAtUtc).TotalMilliseconds;
        if (elapsedMs <= 0)
            return _lastEffectiveTimeLeftMs;

        _lastEffectiveTimeLeftMs = Math.Max(0, _lastEffectiveTimeLeftMs - elapsedMs);
        _lastTimerObservedAtUtc = now;
        return _lastEffectiveTimeLeftMs;
    }

    private static long GetTotalTimeInPhaseMs(JsonElement root)
    {
        if (root.TryGetProperty("timer", out var timer)
            && timer.ValueKind == JsonValueKind.Object
            && timer.TryGetProperty("totalTimeInPhase", out var totalTimeProp)
            && totalTimeProp.ValueKind == JsonValueKind.Number)
        {
            return (long)totalTimeProp.GetDouble();
        }

        return 0;
    }

    /// <summary>
    /// Returns true when the action should be locked in.
    /// A delaySeconds of 0 means lock immediately; otherwise wait until the timer
    /// shows that many seconds (or fewer) remaining.
    /// </summary>
    private static bool ShouldLock(long totalTimeMs, long timeLeftMs, int delaySeconds)
    {
        if (delaySeconds <= 0)
            return true;

        long thresholdMs = delaySeconds * 1000L;
        if (timeLeftMs <= thresholdMs)
            return true;

        if (totalTimeMs <= 0)
            return false;

        long elapsedMs = totalTimeMs - timeLeftMs;
        long startDelayMs = Math.Max(0, totalTimeMs - thresholdMs);
        return elapsedMs >= startDelayMs;
    }

    private static int GetLockDelaySeconds(int configuredDelaySeconds, bool useLastSecondFallback)
    {
        return useLastSecondFallback ? 1 : configuredDelaySeconds;
    }

    private void Log(string message)
    {
        _log?.Invoke(message);
    }

    private void LogStatus(ref string? lastMessage, string message)
    {
        if (string.Equals(lastMessage, message, StringComparison.Ordinal))
            return;

        lastMessage = message;
        Log(message);
    }

    private static string FormatChampionIds(IReadOnlyCollection<int> championIds)
    {
        return championIds.Count == 0 ? "none" : string.Join(", ", championIds.Select(FormatChampion));
    }

    private static string FormatChampion(int championId)
    {
        return ChampionCatalog.FormatWithName(championId);
    }

    private static string FormatTimeLeft(long timeLeftMs)
    {
        if (timeLeftMs <= 0)
            return "0.0s";

        return $"{timeLeftMs / 1000d:F1}s";
    }

    private static string? GetSessionId(JsonElement root)
    {
        if (root.TryGetProperty("multiUserChatId", out var chatIdProperty))
            return chatIdProperty.GetString();

        return null;
    }

    private static bool TryGetInt32(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out var property)
               && property.ValueKind == JsonValueKind.Number
               && property.TryGetInt32(out value);
    }

    private static bool TryGetBool(JsonElement element, string propertyName, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }
}