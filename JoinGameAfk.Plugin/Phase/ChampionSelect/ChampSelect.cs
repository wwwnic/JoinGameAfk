using JoinGameAfk.Enums;
using JoinGameAfk.Interface;
using JoinGameAfk.Model;
using LcuClient;
using static LcuClient.Lcu;

namespace JoinGameAfk.Phase;

public partial class ChampSelect : IPhaseHandler
{
    private readonly LeagueClientHttp _http;
    private readonly ChampSelectSettings _settings;
    private readonly LeagueChampionOwnershipService _ownershipService;
    private readonly Action<string>? _log;
    private readonly Action? _requestRefresh;
    private readonly Action<SoundAlertPlaybackRequest>? _playSoundAlert;
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
    private Position _lastAssignedPosition = Position.None;
    private int _pendingPickHoverActionId;
    private int _pendingBanHoverActionId;
    private string _pendingPickHoverPhase = string.Empty;
    private string _pendingBanHoverPhase = string.Empty;
    private DateTime _pickHoverReadyAtUtc;
    private DateTime _banHoverReadyAtUtc;
    private int _pickRetryStateActionId;
    private int _banRetryStateActionId;
    private readonly HashSet<int> _failedPickChampionIds = [];
    private readonly HashSet<int> _failedBanChampionIds = [];
    private readonly HashSet<string> _playedSoundAlertKeys = [];
    private ScheduledLockState? _scheduledPickLock;
    private ScheduledLockState? _scheduledBanLock;
    private CancellationTokenSource? _scheduledHoverWake;
    private int _scheduledHoverWakeActionId;
    private string _scheduledHoverWakePhase = string.Empty;
    private DateTime _scheduledHoverWakeAtUtc;

    public ClientPhase ClientPhase => ClientPhase.ChampSelect;

    public DashboardStatus LastDashboardStatus { get; private set; } = new();

    public ChampSelect(LeagueClientHttp http, ChampSelectSettings settings, Action<string>? log = null, Action? requestRefresh = null, Action<SoundAlertPlaybackRequest>? playSoundAlert = null)
    {
        _http = http;
        _settings = settings;
        _ownershipService = new LeagueChampionOwnershipService(http, log);
        _log = log;
        _requestRefresh = requestRefresh;
        _playSoundAlert = playSoundAlert;
    }

    public async Task HandleAsync(CancellationToken cancellationToken)
    {
        try
        {
            string json = await _http.GetChampSelectSessionAsync(cancellationToken);
            DateTime sessionObservedAtUtc = DateTime.UtcNow;
            await HandleSessionJsonCoreAsync(json, sessionObservedAtUtc, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log($"HandleAsync Token cancellation requested.");
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log($"Champ Select handler error: {ex.Message}");
        }
    }

    public async Task HandleSessionJsonAsync(string json, DateTime sessionObservedAtUtc, CancellationToken cancellationToken)
    {
        try
        {
            await HandleSessionJsonCoreAsync(json, sessionObservedAtUtc, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log($"HandleSessionJsonAsync Token cancellation requested.");
        }
        catch (Exception ex)
        {
            Log($"Champ Select event handler error: {ex.Message}");
        }
    }
}