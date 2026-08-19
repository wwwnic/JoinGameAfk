using System.Text.Json;
using JoinGameAfk.Model;

namespace LcuClient
{
    internal sealed record ChampionEligibilitySnapshot(
        IReadOnlyDictionary<int, ChampionEligibility> Champions,
        DateTime RefreshedAtUtc,
        string Source)
    {
        public static ChampionEligibilitySnapshot Unknown { get; } = new(
            new Dictionary<int, ChampionEligibility>(),
            DateTime.MinValue,
            string.Empty);

        public bool HasReliableEligibilityData => Champions.Count > 0;

        public string? GetUnavailableStatus(int championId)
        {
            if (championId <= 0
                || !Champions.TryGetValue(championId, out ChampionEligibility eligibility))
            {
                return null;
            }

            if (eligibility.IsDisabled)
                return "Disabled";

            return eligibility.IsSelectable
                ? null
                : "Not owned";
        }
    }

    internal readonly record struct ChampionEligibility(bool IsSelectable, bool IsDisabled);

    internal sealed class LeagueChampionEligibilityService
    {
        private static readonly TimeSpan FailureRetryInterval = TimeSpan.FromSeconds(30);

        private readonly Lcu.LeagueClientHttp _http;
        private readonly Action<string>? _log;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private ChampionEligibilitySnapshot _snapshot = ChampionEligibilitySnapshot.Unknown;
        private string? _sessionId;
        private DateTime _lastRefreshAttemptAtUtc = DateTime.MinValue;

        public LeagueChampionEligibilityService(Lcu.LeagueClientHttp http, Action<string>? log = null)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _log = log;
        }

        public async Task<ChampionEligibilitySnapshot> GetSnapshotAsync(string? sessionId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return ChampionEligibilitySnapshot.Unknown;

            DateTime now = DateTime.UtcNow;
            if (IsCurrentSessionFresh(sessionId, now))
                return _snapshot;

            await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                now = DateTime.UtcNow;
                if (IsCurrentSessionFresh(sessionId, now))
                    return _snapshot;

                _sessionId = sessionId;
                _lastRefreshAttemptAtUtc = now;
                _snapshot = await LoadSnapshotAsync(cancellationToken).ConfigureAwait(false);

                if (_snapshot.HasReliableEligibilityData)
                {
                    _log?.Invoke($"Champion eligibility loaded from {_snapshot.Source}. Champions={_snapshot.Champions.Count}.");
                }
                else
                {
                    _log?.Invoke("Champion eligibility unavailable. Pick plans will not be filtered by ownership yet.");
                }

                return _snapshot;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Champion eligibility refresh failed: {ex.Message}");
                return _snapshot;
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        public void Reset()
        {
            _snapshot = ChampionEligibilitySnapshot.Unknown;
            _sessionId = null;
            _lastRefreshAttemptAtUtc = DateTime.MinValue;
        }

        private bool IsCurrentSessionFresh(string sessionId, DateTime now)
        {
            if (!string.Equals(_sessionId, sessionId, StringComparison.Ordinal))
                return false;

            if (_snapshot.HasReliableEligibilityData)
                return true;

            return now - _lastRefreshAttemptAtUtc < FailureRetryInterval;
        }

        private async Task<ChampionEligibilitySnapshot> LoadSnapshotAsync(CancellationToken cancellationToken)
        {
            try
            {
                string gridJson = await _http.GetChampSelectAllGridChampionsAsync(cancellationToken).ConfigureAwait(false);
                ChampionEligibilitySnapshot snapshot = ParseChampSelectGrid(gridJson);
                if (snapshot.HasReliableEligibilityData)
                    return snapshot;

                _log?.Invoke("Champion-select grid did not include usable eligibility data. Trying ownership fallback.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Champion-select grid endpoint failed: {ex.Message}");
            }

            try
            {
                string ownedJson = await _http.GetOwnedChampionsMinimalAsync(cancellationToken).ConfigureAwait(false);
                return ParseOwnedChampionInventory(ownedJson);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Champion eligibility fallback endpoint failed: {ex.Message}");
                return ChampionEligibilitySnapshot.Unknown;
            }
        }

        internal static ChampionEligibilitySnapshot ParseChampSelectGrid(string json)
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
                return ChampionEligibilitySnapshot.Unknown;

            var champions = new Dictionary<int, ChampionEligibility>();
            foreach (JsonElement champion in root.EnumerateArray())
            {
                if (champion.ValueKind != JsonValueKind.Object
                    || !TryGetInt32(champion, "id", out int championId)
                    || championId <= 0)
                {
                    continue;
                }

                bool isDisabled = TryGetBool(champion, "disabled", out bool disabled) && disabled;
                bool hasAccessSignal = false;
                bool isSelectable = false;

                AddAccessFlag(champion, "owned", ref hasAccessSignal, ref isSelectable);
                AddAccessFlag(champion, "freeToPlay", ref hasAccessSignal, ref isSelectable);
                AddAccessFlag(champion, "freeToPlayForQueue", ref hasAccessSignal, ref isSelectable);
                AddAccessFlag(champion, "loyaltyReward", ref hasAccessSignal, ref isSelectable);
                AddAccessFlag(champion, "xboxGPReward", ref hasAccessSignal, ref isSelectable);
                AddAccessFlag(champion, "rented", ref hasAccessSignal, ref isSelectable);

                if (champion.TryGetProperty("ownership", out JsonElement ownership)
                    && ownership.ValueKind == JsonValueKind.Object)
                {
                    AddAccessFlag(ownership, "owned", ref hasAccessSignal, ref isSelectable);
                    AddAccessFlag(ownership, "loyaltyReward", ref hasAccessSignal, ref isSelectable);
                    AddAccessFlag(ownership, "xboxGPReward", ref hasAccessSignal, ref isSelectable);
                    AddAccessFlag(ownership, "rented", ref hasAccessSignal, ref isSelectable);

                    if (ownership.TryGetProperty("rental", out JsonElement rental)
                        && rental.ValueKind == JsonValueKind.Object)
                    {
                        AddAccessFlag(rental, "rented", ref hasAccessSignal, ref isSelectable);
                    }
                }

                // A missing entitlement field is not proof that the player cannot select the champion.
                // Only block explicit denials or an explicit disabled state.
                if (!hasAccessSignal && !isDisabled)
                    continue;

                champions[LeagueChampionId.ToCanonical(championId)] = new ChampionEligibility(isSelectable && !isDisabled, isDisabled);
            }

            return new ChampionEligibilitySnapshot(
                champions,
                DateTime.UtcNow,
                "/lol-champ-select/v1/all-grid-champions");
        }

        private static ChampionEligibilitySnapshot ParseOwnedChampionInventory(string json)
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
                return ChampionEligibilitySnapshot.Unknown;

            var champions = new Dictionary<int, ChampionEligibility>();
            foreach (JsonElement champion in root.EnumerateArray())
            {
                if (champion.ValueKind == JsonValueKind.Object
                    && TryGetInt32(champion, "id", out int championId)
                    && championId > 0)
                {
                    // This endpoint returns the owned subset. Entries not returned remain unknown rather
                    // than being treated as unavailable, preserving a safe fallback for free rotations.
                    champions[LeagueChampionId.ToCanonical(championId)] = new ChampionEligibility(IsSelectable: true, IsDisabled: false);
                }
            }

            return new ChampionEligibilitySnapshot(
                champions,
                DateTime.UtcNow,
                "/lol-champions/v1/owned-champions-minimal");
        }

        private static void AddAccessFlag(JsonElement element, string propertyName, ref bool hasAccessSignal, ref bool isSelectable)
        {
            if (!TryGetBool(element, propertyName, out bool value))
                return;

            hasAccessSignal = true;
            isSelectable |= value;
        }

        private static bool TryGetInt32(JsonElement element, string propertyName, out int value)
        {
            value = 0;
            return element.TryGetProperty(propertyName, out JsonElement property)
                && property.ValueKind == JsonValueKind.Number
                && property.TryGetInt32(out value);
        }

        private static bool TryGetBool(JsonElement element, string propertyName, out bool value)
        {
            value = false;
            if (!element.TryGetProperty(propertyName, out JsonElement property)
                || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return false;
            }

            value = property.GetBoolean();
            return true;
        }
    }
}
