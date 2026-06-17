# Draft Pick Overview

This document summarizes the Draft Pick flow used by JoinGameAfk and the MockLeagueClient YAML samples.

- Teams: Blue (cells 1–5) vs Red (cells 6–10). Roles: top, jungle, mid, adc, support.
- Local context: LocalSlot (cell id of local player), LocalRole (assigned role), RevealEnemyPickIntents (optional).
- Actions per phase:
  - TimedAction type: pick or ban, optional HoverAtSeconds and LockAtSeconds
  - OptionalTimedAction types: RoleSwap, PickOrderSwap, ChampionSwap (with SourceCell, TargetCell, TriggerAtSeconds)
- Step order:
  - Planning → Ban → BlueFirstPick → RedFirstRotation → BlueSecondRotation → RedSecondRotation → BlueFinalRotation → RedLastPick → Finalization → InGame

```mermaid
flowchart LR
    A[Planning\n- Assign roles\n- Hover pick intents\n- Optional: RoleSwap] --> B[Ban\n- All 10 ban concurrently\n- Optional: RoleSwap]
    B --> C[BlueFirstPick\n- Blue picks cell 1]
    C --> D[RedFirstRotation\n- Red picks cells 6 & 7\n- Optional: PickOrderSwap]
    D --> E[BlueSecondRotation\n- Blue picks cells 2 & 3\n- Optional: PickOrderSwap]
    E --> F[RedSecondRotation\n- Red picks cells 8 & 9\n- Optional: ChampionSwap]
    F --> G[BlueFinalRotation\n- Blue picks cells 4 & 5\n- Optional: ChampionSwap]
    G --> H[RedLastPick\n- Red picks cell 10\n- Optional: ChampionSwap]
    H --> I[Finalization\n- All picks locked\n- Optional: ChampionSwap]
    I --> J[InGame]

    subgraph Legend
    L1[TimedAction: pick/ban\n- Cell: <id>\n- Champion: <name>\n- HoverAtSeconds / LockAtSeconds]
    L2[OptionalTimedAction: RoleSwap / PickOrderSwap / ChampionSwap\n- SourceCell / TargetCell\n- TriggerAtSeconds]
    end
```

## League Client (LCU) API Context

JoinGameAfk communicates with the running League Client through its local League Client Update (LCU) API. This is a private API exposed on `127.0.0.1`; it is not the public Riot developer API.

- LCU reference: [League of Legends LCU API Docs](https://lcu.kebs.dev/)
- Riot Client reference: [Riot Client API Docs](https://riotclient.kebs.dev/)
- Shared source: [KebsCS/lcu-and-riotclient-api](https://github.com/KebsCS/lcu-and-riotclient-api)
- Purpose: community-maintained, version-specific endpoint and schema reference for development.
- Runtime: JoinGameAfk does not depend on the documentation website. It connects directly to the local League Client.
- Current scope: JoinGameAfk uses the LCU API. The separate Riot Client API documentation is a reference for the Riot launcher/client layer and is not currently part of the app's runtime integration.
- Authentication: `ProcessManager` reads the League Client's `--app-port` and `--remoting-auth-token`, then the app uses local HTTPS/WSS with Basic authentication.
- Updates: prefer live `OnJsonApiEvent` WebSocket events, with HTTP polling as a fallback and optional safety refresh.

```mermaid
flowchart LR
    SOURCE["KebsCS/lcu-and-riotclient-api\nShared documentation source"]
    LCU_DOCS["LCU API Docs\nlcu.kebs.dev\nUsed for League Client development"]
    RIOT_DOCS["Riot Client API Docs\nriotclient.kebs.dev\nCompanion reference; not currently used"]

    subgraph LOCAL["Player's computer"]
        CLIENT["League Client\nLocal LCU API\nhttps/wss://127.0.0.1:&lt;port&gt;"]
        PROCESS["ProcessManager\nFind app port and auth token"]
        HTTP["LeagueClientHttp\nRead state and send actions"]
        EVENTS["LeagueClientEventStream\nSubscribe to OnJsonApiEvent"]
        CONTROLLER["PhaseController\nResolve ReadyCheck, ChampSelect,\nand gameflow state"]
        HANDLERS["ReadyCheck / ChampSelect\nAccept, hover, pick, ban, lock"]

        CLIENT -- "process command-line credentials" --> PROCESS
        PROCESS -- "port and Basic auth" --> HTTP
        PROCESS -- "port and Basic auth" --> EVENTS
        HTTP <--> CLIENT
        EVENTS <--> CLIENT
        HTTP --> CONTROLLER
        EVENTS --> CONTROLLER
        CONTROLLER --> HANDLERS
        HANDLERS --> HTTP
    end

    SOURCE --> LCU_DOCS
    SOURCE --> RIOT_DOCS
    LCU_DOCS -. "documents endpoints and payloads" .-> HTTP
    LCU_DOCS -. "documents events and schemas" .-> EVENTS
```

LCU routes currently used by JoinGameAfk:

- Game state: `/lol-gameflow/v1/session`, `/lol-gameflow/v1/gameflow-phase`, `/lol-lobby/v2/lobby`
- Queue: `/lol-matchmaking/v1/ready-check`, `/lol-matchmaking/v1/ready-check/accept`
- Champion select: `/lol-champ-select/v1/session`, `/lol-champ-select/v1/session/actions/{actionId}`
- Player and ownership: `/lol-summoner/v1/current-summoner`, champion inventory endpoints
- Locally installed game version: `/lol-patch/v1/game-version`
- Live updates: WebSocket subscription to `OnJsonApiEvent`

## YAML Schema

Root fields
- Version: number
- QueueId: number
- QueueName: string
- LocalSlot: number (alias: LocalPlayerCellId)
- LocalRole: string (alias: LocalPlayerRole)
- RevealEnemyPickIntents: bool
- ActivePhase: string (one of the DraftPickStep names or display names)

Teams
- BlueTeam: array of { Cell: int, Role: string }
- RedTeam: array of { Cell: int, Role: string }

Phases
- Each phase key (Planning, Ban, BlueFirstPick, RedFirstRotation, BlueSecondRotation, RedSecondRotation, BlueFinalRotation, RedLastPick, Finalization, InGame) has:
  - TimeLeftSeconds: int
  - TimedActions: array of
    - { Cell: int, Type: pick|ban, Champion: string?, HoverAtSeconds?: int, LockAtSeconds?: int }
  - OptionalTimedActions: array of
    - { Id?: int, Type: RoleSwap|PickOrderSwap|ChampionSwap, SourceCell?: int, TargetCell?: int, TriggerAtSeconds?: int }

## Behavior & Normalization
- Step names map to DraftPickStep enum; display names via DraftPickSteps.
- Action types normalized to "pick" or "ban".
- Champion names resolved to IDs via ChampionCatalog.
- OptionalTimedAction types parsed case-insensitively; display names allowed.
- TimeLeftSeconds, HoverAtSeconds, LockAtSeconds clamped to >= 0.

## Sound Alerts Between Phases
JoinGameAfk plays sound alerts on key phase transitions and during pick/ban timing:
- Phase transitions (PhaseController):
  - ReadyCheck: plays ReadyCheck alert
  - Entering ChampSelect: plays ChampSelectStart
  - Exiting ChampSelect (dodge/return): plays ChampSelectEnded
- Champion Select (ChampSelect):
  - Action start: pick → PickActionStart, ban → BanActionStart
  - All configured options unavailable: AllOptionsUnavailable
  - Scheduled auto-lock countdowns: PickLockCountdown/BanLockCountdown and PickLockSoon/BanLockSoon
  - Auto-lock complete: PickLockComplete/BanLockComplete
- All alerts respect SoundSettings (volume, thresholds, infinite playback, per-alert enable/disable) and NotificationSoundPlayer.

## Samples
See YAML examples:
- JoinGameAfk.Tools/JoinGameAfk.Tools.MockLeagueClient/Samples/full-champion-select-flow.yaml
- JoinGameAfk.Tools/JoinGameAfk.Tools.MockLeagueClient/Samples/short-champion-select-flow.yaml
- JoinGameAfk.Tools/JoinGameAfk.Tools.MockLeagueClient/Samples/short-champion-select-flow-cell1-open.yaml
