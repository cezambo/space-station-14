# Open questions (written by the agent)

Format per entry:
## Q-<n> — <short title> (<date>, <requirement IDs>)
- Context:
- Options: A) … B) …
- Recommended default:
- Status: open | answered (<answer>)

## Q-1 — Jev cost with full fan-out vs RC-04 (2026-09-28, RC-04, RC-05, §11, §9.3)
- Context: T1.03b showed billing is per call, so RC-05 keeps full fan-out. But question text is billed on
  every call (~2,020 tokens for the 13 decision questions). At 0.5 Hz × 15 characters, Jev alone costs
  ~$5.0/h with a ~2.4k-token state and ~$6.8/h at the 4k `context.target_tokens`, against a $6/h total
  target that must also cover the LLMs. Details: `docs/reports/T1.03b-billing.md`.
- Options: A) keep full fan-out; target ~2.5k-token decision state in T1.14; decide further cuts after
  T1.31 measures real cadence and LLM cost. B) switch to reduced fan-out now. C) lower mean cadence now
  (e.g. 0.35 Hz). D) raise the RC-04 target.
- Recommended default: A.
- Status: answered (2026-09-28): cost target set for 8 characters instead of 15, and mean cadence slightly
  lower (~0.4 Hz). Full fan-out stays. Recorded as D12 / RC-04. Jev estimate ≈ $2.15–2.91/h.
  Follow-up (2026-09-28): RNF-04 session target is also 8 characters; Phase 6 scale steps (10→15→20) unchanged.

## Q-2 — Score level numbering for `memory_importance_new_opinion = 4` (2026-09-28, RJ-19, §14, T1.21, T1.24)
- Context: the API numbers Score levels from 0 (`legend: {"0": …}`). `memory_filter.yaml` importance has
  5 levels: 0 Trivial, 1 Minor, 2 Moderate, 3 Major, 4 Critical. "importance ≥4" reads as "Major or
  above" if counted from 1, but only "Critical" as a 0-based index.
- Options: A) 1-based intent → store the 0-based index 3 (Major or above). B) 0-based → keep 4 (Critical only).
- Recommended default: A (a new opinion from "Major" events; "Critical only" would make new opinions rare).
  Not needed before T1.21.
- Status: open

# Phase 0 questions

- **Q-P0-01:** With `net.bindto = "127.0.0.1"` in `Cognition/config/server_local.toml`, `ss` still showed listen on `0.0.0.0:1212` / `[::]:1212` after Ready. Confirm whether Robust rewrites bind when IPv6 is present, or whether the TOML value needs the dual-stack form (`127.0.0.1,::1`). Hub advertising is already off.
  - **Resolved (2026-09-27):** `net.bindto` was working. The UDP game socket was on `127.0.0.1:1212`. The `0.0.0.0`/`[::]` TCP listener was the status HTTP server, controlled by `status.bind` (default `*:<port>`). We set `status.bind = "127.0.0.1:1212"`, and `ss` now shows only `127.0.0.1:1212` for UDP and TCP. The client still connects.
- **Q-P0-02:** Owner must run `gh auth login`, fork `space-wizards/space-station-14`, and `git remote add origin <fork-url>` before any push.
  - **Resolved (2026-09-28):** fork `cezambo/space-station-14` exists, `gh` is logged in, `origin` and `upstream` are set.
- **Q-P0-03:** Client GUI playtest blocked in this headless Codespace — needs WSLg or a machine with display/GPU.
  - **Resolved (2026-09-27):** playtest done on Xvfb + llvmpipe (`docs/reports/phase0-playtest.md`).
