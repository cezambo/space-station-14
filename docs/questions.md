# Open questions (Phase 0+)

- **Q-P0-01:** With `net.bindto = "127.0.0.1"` in `Cognition/config/server_local.toml`, `ss` still showed listen on `0.0.0.0:1212` / `[::]:1212` after Ready. Confirm whether Robust rewrites bind when IPv6 is present, or whether the TOML value needs the dual-stack form (`127.0.0.1,::1`). Hub advertising is already off.
- **Q-P0-02:** Owner must run `gh auth login`, fork `space-wizards/space-station-14`, and `git remote add origin <fork-url>` before any push.
- **Q-P0-03:** Client GUI playtest blocked in this headless Codespace — needs WSLg or a machine with display/GPU.
