# Trading Empire (Unity)

Unity port of **Trading Empire**: a manual historical-forex replay terminal plus a city builder whose economy is funded by your trading. The web version (TypeScript) is the reference; this repo re-implements it in C# for Unity 6 (`6000.2.13f1`).

## Layout
- `Assets/Scripts/Core/` pure C# game logic with **no UnityEngine references** (`noEngineReferences`), compiled by Unity and by plain `dotnet`.
- `Assets/Scripts/Terminal/` the trading terminal UI (uGUI, built in code: chart mesh, account and order panels, positions / orders / history / statistics, Load data). It needs no prefabs or scene setup.
- `Assets/Editor/` editor tools: render the terminal to a PNG from the command line, create the scene.
- `Assets/Scenes/Terminal.unity` an empty scene (camera and light); the terminal builds itself when you press Play.
- `Tests/Core/` a `netstandard2.1` / C# 9 project that compiles the same `Assets/Scripts/Core` files (matches Unity's supported surface).
- `Tests/Core.Tests/` xUnit tests, ported from the web version's tests so the expected numbers match.

## Try it
Open `Assets/Scenes/Terminal.unity` and press **Play**. It starts on labelled synthetic test data. Space plays or pauses, Right arrow is the next candle, Shift+Right the next bar, L and S place a long or short with the lots and pips in the order panel, the mouse wheel zooms the chart. **Load data...** reads a CSV / MT4 / MT5 file from a path you type, and picks the symbol from the file name or the Symbol button.

Screenshot from the command line (no window needed):
```bash
Unity -batchmode -projectPath . -executeMethod TradingEmpire.EditorTools.TerminalScreenshot.CaptureCli -shot out.png
```

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
| 2 | Trading session facade, timeframes, synthetic data, chart and terminal UI | done: 68 tests, UI rendered and checked from a headless screenshot; not yet played interactively |
| 3 | City grid, buildings, economy, events, policies, capital transfer | not started |
| 4 | Saves, journal, challenges, achievements, settings, polish | not started |

## Opening in Unity
Open the folder with Unity Hub (editor `6000.2.13f1`). Unity generates `.meta` files on first import; commit them afterwards.
