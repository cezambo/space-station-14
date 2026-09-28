# Phase 0 — Fork & Local Setup Checklist

**Goal:** the SS14 fork builds, runs locally (server + client), and has a recorded test baseline.  
**Requirements:** P1, RA-04, RA-05, §1.3, RNF-07.  
**Rule:** Phase 0 changes configuration and scripts only. No gameplay code changes.

## 0.1 Owner prerequisites [OWNER]
- [x] Fork `space-wizards/space-station-14` on GitHub → `cezambo/space-station-14`.
- [ ] Create API keys; set env vars `TYPESAFE_API_KEY`, `OPENROUTER_API_KEY` (never in files under git).
- [ ] Set spending caps on TypeSafe and OpenRouter dashboards.
- [x] Install the .NET SDK version required by the repo (`global.json`) and Python 3. *(SDK 10.0.401 present; `global.json` asks 10.0.100 with rollForward)*

## 0.2 Repository setup
- [x] Clone with submodules; run `python RUN_THIS.py` (initializes RobustToolbox submodule).
- [x] Add remote `upstream` → space-wizards repo. **Never auto-merge upstream**; upstream merges are owner-approved tasks.
- [x] Add remote `origin` → `cezambo/space-station-14`.
- [x] Add to `.gitignore`: Cognition secrets / replay / sqlite / reports raw.
- [x] Create empty `docs/questions.md`, `docs/proposals/`, `docs/reports/`.

## 0.3 Build & baseline
- [x] `dotnet build -c Release` succeeds. *(via `SpaceStation14.slnx`)*
- [x] Run `Content.Tests` (421 passed / 1 skipped / 0 failed).
- [x] Run `Content.IntegrationTests`: 3,164 passed / 5 skipped / 0 failed, in shards (`scripts/run-integration-shards.sh`; upstream 20-min pool limit).
- [x] Write `docs/reports/phase0-baseline.md`.

## 0.4 Local play configuration
Verify every CVar name before using it; record in `docs/ss14-cvars.md`.
- [x] Authentication disabled for local play. (`auth.mode = 2`)
- [x] Lobby disabled / round auto-start.
- [x] Default game preset without antagonists (`Sandbox`).
- [x] Server bound to localhost only (`net.bindto` + `status.bind = 127.0.0.1:1212`; verified with `ss`, Q-P0-01 resolved).
- [x] Dedicated config: `Cognition/config/server_local.toml`

## 0.5 Launcher (RA-04)
- [x] `scripts/launch.sh` and `scripts/launch.ps1`
- [x] Flags: `--release`, `--no-client`
- [x] Headless server smoke: round starts, `Server → Ready`
- [x] Playtest (run by the agent on Xvfb + llvmpipe, input via xdotool, verified server-side): spawn in a round, walk, pick up an item, open a door. See `docs/reports/phase0-playtest.md`.
- [x] Virtual display: `scripts/display.sh` (Xvfb :99 + noVNC :6080); automation helpers: `scripts/ss14ctl.sh`.

## 0.6 .NET target (RA-05)
- [x] RobustToolbox target framework recorded: **net10.0** (`RobustToolbox/MSBuild/Robust.Properties.targets`)

## 0.7 Recon for later phases (read-only)
- [x] Write `docs/ss14-recon.md` (9 topics + quick reference for the Cognition bridge).

## 0.8 License audit prep (RNF-07)
- [x] List asset license files in `docs/licenses.md` (inventory only; 62 NC textures + NC audio dirs flagged, engine GPLv3 pre-2019 noted).

## Exit criteria / GATE
- [x] Build green; baseline report started.
- [x] Launcher scripts ready; local round play verified (walk / pickup / door).
- [x] `docs/ss14-cvars.md`, `docs/ss14-recon.md`, `docs/licenses.md` exist.
- [ ] **GATE:** owner approval before Phase 1.
