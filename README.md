# Trading Empire (Unity)

Unity port of **Trading Empire**: a manual historical-forex replay terminal plus a city builder whose economy is funded by your trading. The web version (TypeScript) is the reference; this repo re-implements it in C# for Unity 6 (`6000.2.13f1`).

## Layout
- `Assets/Scripts/Core/` pure C# game logic with **no UnityEngine references** (`noEngineReferences`), compiled by Unity and by plain `dotnet`.
- `Assets/Scripts/Core/Progression/` and `Save/` missions, game over, challenges, achievements, and the save system (a small JSON library, the web-compatible v4 codec, checksums, backups).
- `Assets/Scripts/Core/City/` and `Config/` the city: clock, grid, streets, buildings, economy, events, policies, mood, landmarks, capital transfers, world wiring (pure C#).
- `Assets/Scripts/Terminal/` the game UI, built in code (no prefabs): the 3D isometric city (`CityView`, `CityHud`) and the trading terminal (chart mesh, panels, Load data). `GameApp` builds everything when you press Play.
- `Assets/Editor/` editor tools: render the terminal to a PNG from the command line, create the scene.
- `Assets/Scenes/Terminal.unity` an empty scene (camera and light); the terminal builds itself when you press Play.
- `Tests/Core/` a `netstandard2.1` / C# 9 project that compiles the same `Assets/Scripts/Core` files (matches Unity's supported surface).
- `Tests/Core.Tests/` xUnit tests, ported from the web version's tests so the expected numbers match.

## Try it
Open `Assets/Scenes/Terminal.unity` and press **Play**. You start in the city with an empty treasury: open the **Terminal**, trade, close a profitable trade, then use **Transfer** to move realized funds to the city and build. The game autosaves every 30 seconds to your computer and loads the save on start; **Menu** has save, load, export, import, dated backups, missions, achievements and settings. City controls: WASD or arrows and right-drag to pan, mouse wheel to zoom, click a building in the build bar then the map to place (R rotates, Esc cancels), click a building to inspect, upgrade or demolish. The terminal starts on labelled synthetic test data. Space plays or pauses, Right arrow is the next candle, Shift+Right the next bar, L and S place a long or short with the lots and pips in the order panel, the mouse wheel zooms the chart. **Load data...** reads a CSV / MT4 / MT5 file from a path you type, and picks the symbol from the file name or the Symbol button.

Screenshot from the command line (no window needed):
```bash
Unity -batchmode -projectPath . -executeMethod TradingEmpire.EditorTools.TerminalScreenshot.CaptureCli -shot out   # writes out-city.png, out-city-panels.png, out-terminal.png
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
| 3 | City grid, streets, buildings, economy, events, policies, mood, landmarks, capital transfer, 3D city view and HUD | done: 125 tests, views checked from headless screenshots; not yet played interactively |
| 4 | Missions and progression, game over, achievements, trade journal, challenges, saves (web-compatible v4 format, backups, import/export), menu, settings | done: 153 tests, screens checked from headless screenshots; not yet played interactively |

Not ported from the web version: chart drawings and indicators, order planning on the chart, sound, photo mode and fly-over, Electron packaging.

## Opening in Unity
Open the folder with Unity Hub (editor `6000.2.13f1`). Unity generates `.meta` files on first import; commit them afterwards.
