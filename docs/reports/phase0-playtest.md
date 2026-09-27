# Phase 0: local playtest (walk / pick up / door)

**Date:** 2026-09-27. **Who ran it:** the agent (automated). **Map:** Dev. **Preset:** Sandbox. **Config:** `Cognition/config/server_local.toml`.

## Setup
- Display: Xvfb `:99` 1280x800, fluxbox, Mesa llvmpipe (software OpenGL), OpenAL `null` driver (`ALSOFT_DRIVERS=null`, `~/.alsoftrc`).
- Start: `scripts/display.sh`, then `DISPLAY=:99 ALSOFT_DRIVERS=null scripts/launch.sh`. Watch live at `http://localhost:6080/vnc.html`.
- Input: `xdotool` via `scripts/ss14ctl.sh` (console commands, WASD, clicks).
- Evidence comes from the **server**: position from admin log (`spawn` at the player's feet logs the grid coordinates), actions from the `admin_log` table in `Cognition/data/server/preferences.db`, component state from `vvread` (server path).

## Results

| Check | Result | Server evidence |
|---|---|---|
| Spawn into a body | PASS | `Player JoeGenero late joined ... MobArachnid ... as a Captain` |
| Walk | PASS | Holding `D` moved the player from X=31.50 to X=38.95 (grid 2) |
| Pick up item | PASS | admin_log type 36: `Pholcus Algarve ... picked up durathread (MaterialDurathread)` |
| Open door (airlock n10, map entity) | PASS | Walking east from X=40.83: in the doorway the door read `Closing` (it had opened), and the player ended at X=45.30, past the door at X=43.5 |
| Control: bolted door blocks | PASS | Same run with `DoorBolt.BoltsDown=true`: the player stopped at X=42.65 and the door stayed `Closed` |

Screenshot (lighting on, so it's dark): `phase0/door-test.png`.

## Config changes needed to make this work
- `game.new_character_jobs = "Passenger, Captain"`: the Dev map only has Captain slots. Without this, a new player spawns as an observer ghost.
- `[shuttle] arrivals = false` (plus the other settings from `ConfigPresets/Build/development.toml`): with the arrivals shuttle on, the player spawns buckled into a shuttle seat.

## Automation pitfalls (for future agents)
- Do not send `Escape` to "restore focus". It toggles the game menu.
- Do not click at y ≥ 770. That's the fluxbox toolbar, and clicking it iconifies the game window. Use `xdotool windowmap` + `windowfocus`.
- `vvread /entity/N/...` from the client reads the **server** entity N. Prefix `/c` for client-side entities.
- Check the attached entity (`Attaching local player` in the log). An earlier run moved the character into an observer ghost, which passes through walls, so those movement results were invalid and were discarded.
- `togglelight` (client) removes lighting and makes screenshots readable.
