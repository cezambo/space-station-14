# SS14 CVars verified for local cognition play

Sources checked in tree at commit `bc4237e2fc`:

- `RobustToolbox/Robust.Shared/CVars.cs`
- `RobustToolbox/Robust.Shared/Network/AuthMode.cs`
- `Content.Shared/CCVar/CCVars.Game.cs`
- `Content.Shared/CCVar/CCVars.Config.cs`
- preset file: `Resources/ConfigPresets/Build/development.toml`

## Applied in `Cognition/config/server_local.toml`

| TOML path | CVar | Verified value / meaning |
|-----------|------|---------------------------|
| `auth.mode` | `auth.mode` | `2` = `AuthMode.Disabled` |
| `auth.allowlocal` | `auth.allowlocal` | `true` |
| `net.port` | `net.port` | `1212` (default) |
| `net.bindto` | `net.bindto` | `127.0.0.1` (localhost only; default is `0.0.0.0,::`) |
| `game.lobbyenabled` | `game.lobbyenabled` | `false` |
| `game.defaultpreset` | `game.defaultpreset` | `Sandbox` (no antags; works with map `Dev`) |
| `game.fallbackpreset` | `game.fallbackpreset` | `Sandbox` |
| `game.map` | (content) | `Dev` (fast load; same as development preset) |
| `events.enabled` | (content) | `false` |
| `hub.advertise` | (hub) | `false` |
| `console.loginlocal` | (console) | `true` |

## Related notes

- Built-in `Resources/ConfigPresets/Build/development.toml` loads only when compiled with `TOOLS` and `config.preset_development=true`. Release builds should pass `--config-file` explicitly (our launcher does).
- Client auto-connect flags: `--connect` and `--connect-address` (default `localhost`) — `Robust.Client/CommandLineArgs.cs`.
- Target framework for `Cognition.Core`: **net10.0** (`RobustToolbox/MSBuild/Robust.Properties.targets`).
