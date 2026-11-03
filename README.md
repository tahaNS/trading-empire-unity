# Trading Empire (Unity)

Unity port of **Trading Empire**: a manual historical-forex replay terminal plus a city builder whose economy is funded by your trading. The web version (TypeScript) is the reference; this repo re-implements it in C# for Unity 6 (`6000.2.13f1`).

## Layout
- `Assets/Scripts/Core/` pure C# game logic with **no UnityEngine references** (`noEngineReferences`), compiled by Unity and by plain `dotnet`.
- `Tests/Core/` a `netstandard2.1` / C# 9 project that compiles the same `Assets/Scripts/Core` files (matches Unity's supported surface).
- `Tests/Core.Tests/` xUnit tests, ported from the web version's tests so the expected numbers match.

## Run the logic tests (no Unity needed)
```bash
cd Tests/Core.Tests
dotnet test
```

## Rules carried over from the web game
- Money is integer cents (`Money`), never floats; rounding is half-even, once.
- Domain logic has no UI/engine dependencies; the UI only goes through services.
- No lookahead: only revealed candles are ever exposed (`MarketReplay`).
- Same-candle SL/TP conflict resolves stop-loss first.
- Trading account and city treasury are separate; only the transfer service moves money between them (later phase).

## Port status
| Phase | Scope | State |
| --- | --- | --- |
| 1 | Money, units, instruments (8 symbols), P/L, margin, sizing, CSV/MT4/MT5 loader, replay, execution engine (market, limit, stop, SL/TP, stop-out), account, statistics | done, 53 tests |
| 2 | Trading session facade, timeframes, chart and terminal UI | not started |
| 3 | City grid, buildings, economy, events, policies, capital transfer | not started |
| 4 | Saves, journal, challenges, achievements, settings, polish | not started |

## Opening in Unity
Open the folder with Unity Hub (editor `6000.2.13f1`). Unity generates `.meta` files on first import; commit them afterwards.
