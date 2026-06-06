using System.Text.Json;

namespace LcuClient
{
    internal sealed record ChampionOwnershipSnapshot(
        IReadOnlySet<int> SelectableChampionIds,
        IReadOnlySet<int> KnownChampionIds,
        DateTime RefreshedAtUtc,
        string Source)
    {
        public static ChampionOwnershipSnapshot Unknown { get; } = new(
            new HashSet<int>(),
            new HashSet<int>(),
            DateTime.MinValue,
            string.Empty);

        public bool HasReliableOwnershipData => KnownChampionIds.Count > 0;

        public bool IsKnownUnowned(int championId)
        {
            return championId > 0
                && KnownChampionIds.Contains(championId)
                && !SelectableChampionIds.Contains(championId);
        }
    }

    internal sealed class LeagueChampionOwnershipService
    {
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan FailureRetryInterval = TimeSpan.FromSeconds(30);

        private readonly Lcu.LeagueClientHttp _http;
        private readonly Action<string>? _log;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private ChampionOwnershipSnapshot _snapshot = ChampionOwnershipSnapshot.Unknown;
        private DateTime _lastRefreshAttemptAtUtc = DateTime.MinValue;

        public LeagueChampionOwnershipService(Lcu.LeagueClientHttp http, Action<string>? log = null)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _log = log;
        }

        public async Task<ChampionOwnershipSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            DateTime now = DateTime.UtcNow;
            if (IsFresh(now))
                return _snapshot;

            await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                now = DateTime.UtcNow;
                if (IsFresh(now))
                    return _snapshot;

                _lastRefreshAttemptAtUtc = now;
                _snapshot = await LoadSnapshotAsync(cancellationToken).ConfigureAwait(false);

                if (_snapshot.HasReliableOwnershipData)
                {
                    _log?.Invoke($"Champion ownership loaded from {_snapshot.Source}. Selectable={_snapshot.SelectableChampionIds.Count}, known={_snapshot.KnownChampionIds.Count}.");
                }
                else
                {
                    _log?.Invoke("Champion ownership unavailable. Pick plans will not be filtered by ownership yet.");
                }

                return _snapshot;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Champion ownership refresh failed: {ex.Message}");
                return _snapshot;
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        private bool IsFresh(DateTime now)
        {
            if (_snapshot.HasReliableOwnershipData
                && now - _snapshot.RefreshedAtUtc < RefreshInterval)
            {
                return true;
            }

            return !_snapshot.HasReliableOwnershipData
                && now - _lastRefreshAttemptAtUtc < FailureRetryInterval;
        }

        private async Task<ChampionOwnershipSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken)
        {
            try
            {
                string summonerJson = await _http.GetCurrentSummonerAsync(cancellationToken).ConfigureAwait(false);
                if (TryGetSummonerId(summonerJson, out long summonerId))
                {
                    string inventoryJson = await _http.GetChampionInventoryMinimalAsync(summonerId, cancellationToken).ConfigureAwait(false);
                    var snapshot = ParseChampionInventory(inventoryJson, $"/lol-champions/v1/inventories/{summonerId}/champions-minimal");
                    if (snapshot.HasReliableOwnershipData)
                        return snapshot;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Champion ownership primary endpoint failed: {ex.Message}");
            }

            try
            {
                string ownedJson = await _http.GetOwnedChampionsMinimalAsync(cancellationToken).ConfigureAwait(false);
                return ParseChampionInventory(ownedJson, "/lol-champions/v1/owned-champions-minimal");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Champion ownership fallback endpoint failed: {ex.Message}");
                return ChampionOwnershipSnapshot.Unknown;
            }
        }

        private static bool TryGetSummonerId(string json, out long summonerId)
        {
            summonerId = 0;

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && TryGetNumberAsInt64(root, "summonerId", out summonerId)
                && summonerId > 0;
        }

        private static ChampionOwnershipSnapshot ParseChampionInventory(string json, string source)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
                return ChampionOwnershipSnapshot.Unknown;

            var selectableChampionIds = new HashSet<int>();
            var knownChampionIds = new HashSet<int>();

            foreach (var champion in root.EnumerateArray())
            {
                if (champion.ValueKind != JsonValueKind.Object
                    || !TryGetInt32(champion, "id", out int championId)
                    || championId <= 0)
                {
                    continue;
                }

                if (!TryGetSelectable(champion, out bool selectable))
                    continue;

                knownChampionIds.Add(championId);
                if (selectable)
                    selectableChampionIds.Add(championId);
            }

            return new ChampionOwnershipSnapshot(
                selectableChampionIds,
                knownChampionIds,
                DateTime.UtcNow,
                source);
        }

        private static bool TryGetSelectable(JsonElement champion, out bool selectable)
        {
            selectable = false;
            bool hasSelectionSignal = false;

            if (champion.TryGetProperty("ownership", out var ownership)
                && ownership.ValueKind == JsonValueKind.Object)
            {
                hasSelectionSignal |= TryIncludeSelectableFlag(ownership, "owned", ref selectable);
                hasSelectionSignal |= TryIncludeSelectableFlag(ownership, "freeToPlayReward", ref selectable);
                hasSelectionSignal |= TryIncludeSelectableFlag(ownership, "xboxGPReward", ref selectable);
            }

            hasSelectionSignal |= TryIncludeSelectableFlag(champion, "owned", ref selectable);
            hasSelectionSignal |= TryIncludeSelectableFlag(champion, "freeToPlay", ref selectable);
            hasSelectionSignal |= TryIncludeSelectableFlag(champion, "freeToPlayForQueue", ref selectable);
            hasSelectionSignal |= TryIncludeSelectableFlag(champion, "freeToPlayReward", ref selectable);
            hasSelectionSignal |= TryIncludeSelectableFlag(champion, "xboxGPReward", ref selectable);

            return hasSelectionSignal;
        }

        private static bool TryIncludeSelectableFlag(JsonElement element, string propertyName, ref bool selectable)
        {
            if (!TryGetBool(element, propertyName, out bool value))
                return false;

            selectable |= value;
            return true;
        }

        private static bool TryGetInt32(JsonElement element, string propertyName, out int value)
        {
            value = 0;
            return element.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.Number
                && property.TryGetInt32(out value);
        }

        private static bool TryGetNumberAsInt64(JsonElement element, string propertyName, out long value)
        {
            value = 0;
            if (!element.TryGetProperty(propertyName, out var property)
                || property.ValueKind != JsonValueKind.Number)
            {
                return false;
            }

            if (property.TryGetInt64(out value))
                return true;

            value = (long)property.GetDouble();
            return true;
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
}
