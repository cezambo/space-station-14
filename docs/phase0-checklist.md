# Phase 0 — Fork & Local Setup Checklist

**Goal:** the SS14 fork builds, runs locally (server + client), and has a recorded test baseline.  
**Requirements:** P1, RA-04, RA-05, §1.3, RNF-07.  
**Rule:** Phase 0 changes configuration and scripts only. No gameplay code changes.

## 0.1 Owner prerequisites [OWNER]
- [ ] Fork `space-wizards/space-station-14` on GitHub; open the fork in the IDE.
- [ ] Create API keys; set env vars `TYPESAFE_API_KEY`, `OPENROUTER_API_KEY` (never in files under git).
- [ ] Set spending caps on TypeSafe and OpenRouter dashboards.
- [x] Install the .NET SDK version required by the repo (`global.json`) and Python 3. *(SDK 10.0.401 present; `global.json` asks 10.0.100 with rollForward)*

## 0.2 Repository setup
- [x] Clone with submodules; run `python RUN_THIS.py` (initializes RobustToolbox submodule).
- [x] Add remote `upstream` → space-wizards repo. **Never auto-merge upstream**; upstream merges are owner-approved tasks.
- [ ] Add remote `origin` → **your GitHub fork** (blocked: `gh` not authenticated).
- [x] Add to `.gitignore`: Cognition secrets / replay / sqlite / reports raw.
- [x] Create empty `docs/questions.md`, `docs/proposals/`, `docs/reports/`.

## 0.3 Build & baseline
- [x] `dotnet build -c Release` succeeds. *(via `SpaceStation14.slnx`)*
- [x] Run `Content.Tests` (421 passed / 1 skipped / 0 failed).
- [~] Run `Content.IntegrationTests` (still running in background; partial skips observed).
- [x] Write `docs/reports/phase0-baseline.md`.

## 0.4 Local play configuration
Verify every CVar name before using it; record in `docs/ss14-cvars.md`.
- [x] Authentication disabled for local play. (`auth.mode = 2`)
- [x] Lobby disabled / round auto-start.
- [x] Default game preset without antagonists (`Sandbox`).
- [~] Server bound to localhost only. (`net.bindto = 127.0.0.1` set; observed listen on `0.0.0.0:1212` — see questions.md)
- [x] Dedicated config: `Cognition/config/server_local.toml`

## 0.5 Launcher (RA-04)
- [x] `scripts/launch.sh` and `scripts/launch.ps1`
- [x] Flags: `--release`, `--no-client`
- [x] Headless server smoke: round starts, `Server → Ready`
- [ ] Manual check [OWNER]: spawn in a round, walk, pick up an item, open a door. *(needs GPU/display)*

## 0.6 .NET target (RA-05)
- [x] RobustToolbox target framework recorded: **net10.0** (`RobustToolbox/MSBuild/Robust.Properties.targets`)

## 0.7 Recon for later phases (read-only)
- [ ] Write `docs/ss14-recon.md` (deferred — next agent pass after GATE).

## 0.8 License audit prep (RNF-07)
- [ ] List asset license files in `docs/licenses.md` (deferred).

## Exit criteria / GATE
- [x] Build green; baseline report started.
- [~] Launcher scripts ready; full local round play not verified in this headless/WSL container yet.
- [~] `docs/ss14-cvars.md` written; recon + licenses pending.
- [ ] **GATE:** owner approval before Phase 1.
