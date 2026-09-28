# Phase 0 baseline

**Date (UTC):** 2026-09-27  
**Commit:** `bc4237e2fc` (`master` tracking `upstream/master` at clone time)  
**Upstream:** https://github.com/space-wizards/space-station-14.git

## Machine

| Item | Value |
|------|--------|
| OS | Linux (Ubuntu 24.04 in Codespace/WSL2), kernel 6.18.33.2-microsoft-standard-WSL2 |
| Arch | x64 (`linux-x64`) |
| CPU | 16 logical processors |
| RAM | ~16 GiB |
| Disk (workspace) | ample (~1.4G repo checkout before build artifacts) |
| Display | Headless container — **no GPU client smoke test yet** |

## Toolchain

| Item | Value |
|------|--------|
| `global.json` SDK | 10.0.100 (`rollForward: latestFeature`) |
| Installed SDK | 10.0.401 |
| Python | 3.14.2 |
| Solution file | `SpaceStation14.slnx` (not `.sln`) |
| RobustToolbox TFM | **net10.0** |

## Build

```text
dotnet build SpaceStation14.slnx -c Release
→ Build succeeded.
→ 694 Warning(s), 0 Error(s)
→ Time Elapsed ~00:03:58
```

## Unit tests (`Content.Tests`)

```text
dotnet test Content.Tests/Content.Tests.csproj -c Release --no-build
→ Passed!  Failed: 0, Passed: 421, Skipped: 1, Total: 422, Duration: ~4 s
→ Skipped: TestAlertManager
```

## Integration tests (`Content.IntegrationTests`)

**Result: 0 failures.** Run in shards with `scripts/run-integration-shards.sh` (game stopped, 2026-09-27):

| Shard | Filter | Passed | Skipped | Failed | Wall time |
|---|---|---:|---:|---:|---:|
| entitytest | `Tests.EntityTest.` | 5 | 0 | 0 | 5m13s |
| gamerules | `Tests.GameRules.` | 118 | 0 | 0 | 12m59s |
| rest-ag | namespaces A–G (minus the two above) | 1,927 | 1 | 0 | 3m05s |
| rest-hz | namespaces H–Z | 1,114 | 4 | 0 | 12m11s |
| **Total** | | **3,164** | **5** | **0** | ~33 min |

**Why shards:** `Content.IntegrationTests/PoolManagerTestEventHandler.cs` shuts the test pool down after
**20 minutes** total (`MaximumTotalTestingTimeLimit`). A single full run takes longer on this machine,
so every test that hadn't started yet fails at once with `Pool manager has not been initialized`.
The earlier "1315 / 1376 failures" were exactly that cascade: all tests that finished before the cutoff
had passed. Each shard stays under 20 min. Test code was not changed.

**Watching progress:** the runner writes `TESTES-PROGRESSO.md` (repo root, gitignored) every 5 s
(state, per-shard progress bar, ETA, last test, failures). `scripts/test-dashboard.py --once` prints the same in a terminal.
Raw logs/TRX: `docs/reports/raw/integration-shards/` (gitignored).

Pre-existing failures (if any) are recorded, not fixed (phase0 rule).

## Remotes

| Remote | URL | Notes |
|--------|-----|--------|
| `upstream` | `https://github.com/space-wizards/space-station-14.git` | Official; never auto-merge |
| `origin` | `https://github.com/cezambo/space-station-14.git` | Owner fork |

## Local play artifacts

- Config: `Cognition/config/server_local.toml`
- Launch: `scripts/launch.sh`, `scripts/launch.ps1`
- Spec retained: `SS14 + JEV - FINAL.md`
- Devcontainer: `.devcontainer/devcontainer.json` (universal image; may need .NET/Python already present)

## Headless server smoke (2026-09-27)

```text
./bin/Content.Server/Content.Server \
  --config-file Cognition/config/server_local.toml \
  --data-dir Cognition/data/server
→ Configuration loaded from file
→ Name: SS14+JEV local
→ Round started (Sandbox / Dev map)
→ "Server Version 290.0.0.0 -> Ready"
```

Client GUI playtest: done on Xvfb + llvmpipe, see `phase0-playtest.md`.

## Re-verification in new container (2026-09-28, UTC-3)

Fresh clone on a Linux volume (outside `C:\`), commit `8364ca2f5f`.

| Check | Result |
|---|---|
| `git submodule update --init --recursive` + `python3 RUN_THIS.py` | OK (RobustToolbox `fee0dd647`) |
| `dotnet build SpaceStation14.slnx -c Release` | 0 errors, 694 warnings, 2m34s — matches baseline |
| `Content.Tests` | 421 passed / 1 skipped / 0 failed — matches baseline |
| Integration tests | not re-run in this container |
| `TYPESAFE_API_KEY`, `OPENROUTER_API_KEY` | defined |
| `gh auth status` | logged in as `cezambo` |
| Remotes | `origin` URL cleaned (had an embedded token); `upstream` re-added. `master` is 5 ahead / 17 behind `upstream/master` (not merged) |

## Owner items left for Phase 0 exit

1. ~~API keys + spend caps~~ — keys set; spend caps not needed (prepaid credits, topped up manually).
2. GATE approval to start Phase 1.
