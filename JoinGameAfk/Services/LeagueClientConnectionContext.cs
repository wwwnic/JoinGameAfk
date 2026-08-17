using JoinGameAfk.Plugin.Services;
using LcuClient;

namespace JoinGameAfk.Services
{
    public sealed class LeagueClientConnectionContext
    {
        private readonly object _syncRoot = new();
        private AuthModel? _auth;
        private LeagueClientRegionLocaleInfo? _regionLocale;

        public event EventHandler? Connected;

        public bool IsConnected
        {
            get
            {
                lock (_syncRoot)
                    return _auth is not null;
            }
        }

        public void SetConnected(AuthModel auth, LeagueClientRegionLocaleInfo regionLocale)
        {
            ArgumentNullException.ThrowIfNull(auth);
            ArgumentNullException.ThrowIfNull(regionLocale);
            bool connectionChanged;
            lock (_syncRoot)
            {
                connectionChanged = _auth is null
                    || !string.Equals(_auth.Port, auth.Port, StringComparison.Ordinal)
                    || !string.Equals(_auth.Base64Token, auth.Base64Token, StringComparison.Ordinal);
                _auth = new AuthModel(auth.Port, auth.Base64Token);
                _regionLocale = regionLocale;
            }

            if (connectionChanged)
                Connected?.Invoke(this, EventArgs.Empty);
        }

        public void SetDisconnected()
        {
            lock (_syncRoot)
            {
                _auth = null;
                _regionLocale = null;
            }
        }

        public void UpdateRegionLocale(LeagueClientRegionLocaleInfo regionLocale)
        {
            ArgumentNullException.ThrowIfNull(regionLocale);
            lock (_syncRoot)
            {
                if (_auth is null)
                    return;

                _regionLocale = regionLocale;
            }
        }

        public bool TryGetRegionLocale(out LeagueClientRegionLocaleInfo? regionLocale)
        {
            lock (_syncRoot)
            {
                regionLocale = _regionLocale;
                return regionLocale is not null;
            }
        }

        public Lcu.LeagueClientHttp CreateHttpClient(Action<string>? log = null)
        {
            AuthModel auth;
            lock (_syncRoot)
            {
                auth = _auth is null
                    ? throw new InvalidOperationException(
                        "League Client data was selected, but the watcher is not connected to the League Client.")
                    : new AuthModel(_auth.Port, _auth.Base64Token);
            }

            return new Lcu.LeagueClientHttp(auth, log);
        }

        public async Task<bool> WaitForConnectionAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (timeout <= TimeSpan.Zero)
                return IsConnected;

            DateTime deadline = DateTime.UtcNow + timeout;
            while (!IsConnected && DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
            }

            return IsConnected;
        }
    }
}
