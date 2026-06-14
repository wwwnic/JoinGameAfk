# Copilot Instructions

## Directives de projet
- Champion select preference behavior: Default is only a fallback. Role-specific priorities (Top, Mid, Jungle, ADC, Support) must take precedence over Default, with Default appended only for missing champions.

## Draft Pick Docs
- For the full flow diagram, YAML schema, and sound alerts, see: .github/draft-pick-overview.md
- The same document explains the local LCU API integration. Use https://lcu.kebs.dev/ for League Client endpoints and https://riotclient.kebs.dev/ for the separate Riot Client API. Both are generated from https://github.com/KebsCS/lcu-and-riotclient-api and are development references, not runtime dependencies. JoinGameAfk currently uses LCU only.
