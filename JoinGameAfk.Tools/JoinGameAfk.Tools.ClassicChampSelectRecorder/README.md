# League Classic champion-select recorder

This small diagnostic tool records the LCU payloads needed to compare League
Classic blind and draft champion select with the flows supported by JoinGameAfk.
It only connects to the local League Client API.

Player-identifying properties such as PUUIDs, summoner IDs, Riot IDs, and display
names are replaced with `[redacted]` before a line is written. Authentication
credentials are never written to the log.

From the repository root:

```powershell
dotnet run --project JoinGameAfk.Tools/JoinGameAfk.Tools.ClassicChampSelectRecorder
```

Start the tool before creating the custom lobby. Play through one blind and one
draft champion select, press Ctrl+C, then share the generated
`classic-champ-select-*.jsonl` file.

To select the output path:

```powershell
dotnet run --project JoinGameAfk.Tools/JoinGameAfk.Tools.ClassicChampSelectRecorder -- --output classic-test.jsonl
```

The recorder refuses to overwrite an existing file.
