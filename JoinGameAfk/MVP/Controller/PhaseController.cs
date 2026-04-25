using System.Text.Json;
using JoinGameAfk.Constant;
using JoinGameAfk.Enums;
using JoinGameAfk.Interface;
using JoinGameAfk.Model;
using JoinGameAfk.View;
using LcuClient;

namespace JoinGameAfk.MVP.Controller
{
    public class PhaseController
    {
        private readonly PhaseProgressionPage fPhaseProgressionPage;
        private readonly ChampSelectSettings _champSelectSettings;
        private readonly List<IPhaseHandler> _phaseHandlers;

        private CancellationTokenSource? _cts;
        private bool _isRunning;
        private ClientPhase _lastObservedPhase;
        private ClientPhase _lastHandledPhase;
        private bool _isClientConnected;
        private bool _isWaitingForClient;

        public bool IsRunning => _isRunning;

        public PhaseController(PhaseProgressionPage phaseProgressionPage, ChampSelectSettings champSelectSettings)
        {
            fPhaseProgressionPage = phaseProgressionPage;
            _champSelectSettings = champSelectSettings;
            _phaseHandlers = [];
        }

        public void Start()
        {
            if (_isRunning) return;

            _isRunning = true;
            _lastObservedPhase = ClientPhase.Unknown;
            _lastHandledPhase = ClientPhase.Unknown;
            _isClientConnected = false;
            _isWaitingForClient = false;

            fPhaseProgressionPage.SetWatcherState(true);
            fPhaseProgressionPage.SetClientConnection(false);
            fPhaseProgressionPage.UpdatePhase(ClientPhase.Unknown);

            _cts = new CancellationTokenSource();
            _ = RunLoopAsync(_cts.Token);
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _cts?.Cancel();
            fPhaseProgressionPage.SetWatcherState(false);
            fPhaseProgressionPage.SetClientConnection(false);
            fPhaseProgressionPage.UpdatePhase(ClientPhase.Unknown);
        }

        private async Task RunLoopAsync(CancellationToken ct)
        {
            Log("Waiting for League Client process...");

            var processManager = new Lcu.ProcessManager(JoinGameAfkConstant.LeagueClient.ProcessName);
            Lcu.LeagueClientHttp? http = null;

            while (!ct.IsCancellationRequested)
            {
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
                            }

                            await Task.Delay(3000, ct);
                            continue;
                        }

                        _isWaitingForClient = false;
                        http = new Lcu.LeagueClientHttp(auth);
                        InitializeHandlers(http);

                        if (!_isClientConnected)
                        {
                            _isClientConnected = true;
                            Log("Connected to League Client.");
                            fPhaseProgressionPage.SetClientConnection(true);
                        }
                    }

                    ClientPhase phase = await GetCurrentPhaseAsync(http);
                    fPhaseProgressionPage.UpdatePhase(phase);

                    if (phase != _lastObservedPhase)
                    {
                        Log($"Phase changed: {_lastObservedPhase} -> {phase}");
                        _lastObservedPhase = phase;
                        _lastHandledPhase = ClientPhase.Unknown;

                        var champSelectHandler = _phaseHandlers.OfType<ChampSelect>().FirstOrDefault();
                        champSelectHandler?.Reset();
                    }

                    var handler = _phaseHandlers.FirstOrDefault(h => h.ClientPhase == phase);
                    var champSelect = _phaseHandlers.OfType<ChampSelect>().FirstOrDefault();
                    bool isChampSelectFlow = phase is ClientPhase.ChampSelect or ClientPhase.Planning;

                    if (handler != null && _lastHandledPhase != phase)
                    {
                        string? actionMessage = GetActionMessage(phase);
                        if (!string.IsNullOrWhiteSpace(actionMessage))
                            Log(actionMessage);

                        handler.Handle();
                        _lastHandledPhase = phase;
                    }
                    else if (champSelect != null && isChampSelectFlow)
                    {
                        champSelect.Handle();
                    }

                    int delayMs = isChampSelectFlow
                        ? Math.Clamp(_champSelectSettings.ChampSelectPollIntervalMs, 100, 5000)
                        : 2000;
                    await Task.Delay(delayMs, ct);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogError(ex.Message);
                    _isClientConnected = false;
                    _isWaitingForClient = false;
                    _lastObservedPhase = ClientPhase.Unknown;
                    _lastHandledPhase = ClientPhase.Unknown;
                    fPhaseProgressionPage.SetClientConnection(false);
                    fPhaseProgressionPage.UpdatePhase(ClientPhase.Unknown);
                    http = null;
                    await Task.Delay(5000, ct);
                }
            }

            Log("Stopped watching.");
        }

        private void InitializeHandlers(Lcu.LeagueClientHttp http)
        {
            _phaseHandlers.Clear();
            _phaseHandlers.Add(new ReadyCheck(http, _champSelectSettings, Log));
            _phaseHandlers.Add(new ChampSelect(http, _champSelectSettings, Log));
        }

        private void Log(string message)
        {
            fPhaseProgressionPage.WriteLine(message);
            Console.WriteLine(message);
        }

        private void LogError(string message)
        {
            fPhaseProgressionPage.WriteErrorLine(message);
            Console.Error.WriteLine(message);
        }

        private static async Task<ClientPhase> GetCurrentPhaseAsync(Lcu.LeagueClientHttp http)
        {
            try
            {
                string json = await http.GetSessionAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("phase", out var phaseProp))
                {
                    string? phaseStr = phaseProp.GetString();
                    if (Enum.TryParse<ClientPhase>(phaseStr, true, out var phase))
                        return phase;
                }
            }
            catch { }

            return ClientPhase.Unknown;
        }

        private static string GetPhaseSummary(ClientPhase phase)
        {
            return phase switch
            {
                ClientPhase.Lobby => "Lobby detected.",
                ClientPhase.Matchmaking => "Queue started. Waiting for ready check.",
                ClientPhase.ReadyCheck => "Ready check detected.",
                ClientPhase.ChampSelect => "Champion Select detected.",
                ClientPhase.Planning => "Planning phase detected.",
                ClientPhase.InGame => "Game started.",
                _ => "Waiting for a usable League session."
            };
        }

        private static string? GetActionMessage(ClientPhase phase)
        {
            return phase switch
            {
                ClientPhase.ReadyCheck => "Ready check detected.",
                ClientPhase.ChampSelect => "Champion Select is now active. Auto-picking and banning based on your settings.",
                _ => null
            };
        }
    }
}