using JoinGameAfk.Tools.ClassicChampSelectRecorder;
using LcuClient;

const string LeagueClientProcessName = "LeagueClientUx";

string outputPath;
try
{
    outputPath = ResolveOutputPath(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine("Usage: dotnet run --project JoinGameAfk.Tools/JoinGameAfk.Tools.ClassicChampSelectRecorder -- [--output <file.jsonl>]");
    return 2;
}

using var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

var processManager = new Lcu.ProcessManager(LeagueClientProcessName);
Console.WriteLine("Waiting for League Client. Press Ctrl+C to stop.");

AuthModel? auth = null;
try
{
    while (!cancellationSource.IsCancellationRequested && auth is null)
    {
        auth = processManager.GetLeagueAuth();
        if (auth is null)
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationSource.Token);
    }
}
catch (OperationCanceledException)
{
    return 0;
}

if (auth is null)
    return 0;

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
using var logWriter = new ChampSelectLogWriter(outputPath);
using var probe = new LeagueClientProbe(auth, logWriter);

logWriter.WriteNote("Recorder connected. Player-identifying JSON properties are replaced with [redacted].");
Console.WriteLine($"Connected. Recording sanitized League Classic champion-select data to:");
Console.WriteLine(outputPath);
Console.WriteLine("Play one or two champion selects, then press Ctrl+C.");

string[] initialSnapshotUris =
[
    "/lol-gameflow/v1/gameflow-phase",
    "/lol-gameflow/v1/session",
    "/lol-lobby/v2/lobby",
    "/lol-champ-select/v1/session"
];

var referenceCapture = new ChampSelectReferenceCapture(probe, logWriter, cancellationSource.Token);
foreach (string uri in initialSnapshotUris)
{
    bool captured = await probe.CaptureAsync(uri, cancellationSource.Token);
    if (captured && string.Equals(uri, "/lol-champ-select/v1/session", StringComparison.OrdinalIgnoreCase))
        referenceCapture.CaptureCurrentSession();
}

using var eventStream = new Lcu.LeagueClientEventStream(
    auth,
    apiEvent =>
    {
        if (!ShouldRecord(apiEvent.Uri))
            return;

        logWriter.WriteEvent(apiEvent);
        referenceCapture.Observe(apiEvent);
    },
    connected: () => logWriter.WriteNote("LCU websocket event stream connected."),
    log: Console.WriteLine);

try
{
    await eventStream.RunAsync(cancellationSource.Token);
}
catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
{
}
catch (Exception ex)
{
    logWriter.WriteNote($"Event stream ended: {ex.GetType().Name}");
    Console.Error.WriteLine($"Event stream ended: {ex.Message}");
}
finally
{
    cancellationSource.Cancel();
    await referenceCapture.DrainAsync();
}

Console.WriteLine($"Recording stopped. Share this sanitized log: {outputPath}");
return 0;

static bool ShouldRecord(string uri)
{
    return uri.StartsWith("/lol-champ-select/", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri, "/lol-gameflow/v1/gameflow-phase", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri, "/lol-gameflow/v1/session", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri, "/lol-lobby/v2/lobby", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri, "/lol-matchmaking/v1/ready-check", StringComparison.OrdinalIgnoreCase);
}

static string ResolveOutputPath(string[] commandLineArgs)
{
    string? requestedPath = null;
    for (int index = 0; index < commandLineArgs.Length; index++)
    {
        if (!string.Equals(commandLineArgs[index], "--output", StringComparison.OrdinalIgnoreCase)
            || index + 1 >= commandLineArgs.Length)
        {
            throw new ArgumentException($"Unknown or incomplete argument: {commandLineArgs[index]}");
        }

        requestedPath = commandLineArgs[++index];
    }

    string fileName = requestedPath
        ?? $"classic-champ-select-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.jsonl";
    string fullPath = Path.GetFullPath(fileName);

    if (File.Exists(fullPath))
        throw new ArgumentException($"Refusing to overwrite existing file: {fullPath}");

    return fullPath;
}

internal sealed class ChampSelectReferenceCapture
{
    private static readonly string[] ReferenceUris =
    [
        "/lol-champ-select/v1/all-grid-champions",
        "/lol-champ-select/v1/pickable-champion-ids",
        "/lol-champ-select/v1/bannable-champion-ids",
        "/lol-champ-select/v1/disabled-champion-ids"
    ];

    private readonly Lock _stateLock = new();
    private readonly LeagueClientProbe _probe;
    private readonly ChampSelectLogWriter _logWriter;
    private readonly CancellationToken _cancellationToken;
    private Task _captureTask = Task.CompletedTask;
    private bool _hasCapturedCurrentSession;

    public ChampSelectReferenceCapture(
        LeagueClientProbe probe,
        ChampSelectLogWriter logWriter,
        CancellationToken cancellationToken)
    {
        _probe = probe;
        _logWriter = logWriter;
        _cancellationToken = cancellationToken;
    }

    public void Observe(Lcu.LeagueClientEvent apiEvent)
    {
        if (!string.Equals(apiEvent.Uri, "/lol-champ-select/v1/session", StringComparison.OrdinalIgnoreCase))
            return;

        lock (_stateLock)
        {
            if (string.Equals(apiEvent.EventType, "Delete", StringComparison.OrdinalIgnoreCase))
            {
                _hasCapturedCurrentSession = false;
                return;
            }
        }

        CaptureCurrentSession();
    }

    public void CaptureCurrentSession()
    {
        lock (_stateLock)
        {
            if (_hasCapturedCurrentSession)
                return;

            _hasCapturedCurrentSession = true;
            _captureTask = CaptureAsync();
        }
    }

    public async Task DrainAsync()
    {
        Task captureTask;
        lock (_stateLock)
        {
            captureTask = _captureTask;
        }

        try
        {
            await captureTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task CaptureAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), _cancellationToken).ConfigureAwait(false);
            foreach (string uri in ReferenceUris)
                await _probe.CaptureAsync(uri, _cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logWriter.WriteNote($"Champion-select reference capture ended: {ex.GetType().Name}");
        }
    }
}
