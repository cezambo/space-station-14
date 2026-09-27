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

```text
dotnet test Content.IntegrationTests -c Release --no-build
→ Failed!  Failed: 1315, Passed: 1825, Skipped: 1, Total: 3141, Duration: 20m56s
→ Test Run Aborted.
```

**Likely environmental, not a code regression.** The first failure is a tick desync in the test pool
(`TestPair.SyncTicks: Expected 1, But was -25233`). Then every later test fails in SetUp with
`Pool manager has not been initialized` (cascade). The run overlapped with a manual server smoke test
and heavy CPU/RAM load (~59% RAM on testhost). **Rerun alone** on an idle machine before treating
these as pre-existing failures.

Pre-existing failures (if any) are recorded, not fixed (phase0 rule).

## Remotes

| Remote | URL | Notes |
|--------|-----|--------|
| `upstream` | `https://github.com/space-wizards/space-station-14.git` | Official; never auto-merge |
| `origin` | *(missing)* | Create GitHub fork + `gh auth login`, then `git remote add origin <fork-url>` |

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

Client GUI playtest still needs a display (WSLg / desktop).

## Owner blockers for full Phase 0 exit

1. Authenticate GitHub (`gh auth login`) and create a personal fork; set `origin`.
2. Manual client playtest with a display (WSLg / local GPU host).
3. Finish IntegrationTests run and paste counts below.
4. Optional: API keys + spend caps (needed for Phase 1 billing gate, not for build).
