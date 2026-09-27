# Asset & code license inventory (RNF-07)

**Scope:** inventory only. No decisions are made here; distribution decisions are an owner task (see spec RNF-07).
**Snapshot:** upstream `space-wizards/space-station-14` as cloned in Phase 0 (fork `cezambo/space-station-14`), 2026-09-27.
**Method:** scripted scan of `Resources/**` — `license` field of every RSI `meta.json`, and `license:` entries of every `attributions.yml`. Re-run the scan after each upstream merge.

## Code

| Component | License | Source |
|---|---|---|
| Content (`Content.*`, repo root) | MIT | `LICENSE.TXT` (Space Wizards Federation, 2017–2026) |
| RobustToolbox code contributed **after** 2019-03-13 12:00 UTC | MIT | `RobustToolbox/legal.md`, `LICENSE-MIT.TXT` |
| RobustToolbox code contributed **before** 2019-03-13 12:00 UTC | **GPLv3** (unless relicensed; PJB3005 relicensed to MIT) | `RobustToolbox/legal.md`, `LICENSE-GPLv3.TXT` |
| RobustToolbox images/models/rigging | CC-BY-SA 3.0 US (unless stated) | `RobustToolbox/LICENSE-ASSETS.TXT` |
| Future `Cognition/*` (ours) | TBD by owner | — |

## Textures (RSI `meta.json`) — 3,128 RSIs

| License | Count |
|---|---:|
| CC-BY-SA-3.0 | 2,815 |
| CC0-1.0 | 124 |
| CC-BY-SA-4.0 | 117 |
| CC-BY-NC-SA-3.0 | 47 |
| CC-BY-3.0 | 8 |
| CC-BY-NC-SA-4.0 | 6 |
| CC-BY-NC-4.0 | 5 |
| CC-BY-NC-3.0 | 4 |
| CC-BY-4.0 | 2 |

**NonCommercial (NC): 62 RSIs** — listed in the appendix.

## Audio and other attributed assets (`attributions.yml`, 98 files)

Counts are attribution entries (one entry may cover several files).

| License | Entries |
|---|---:|
| CC-BY-SA-3.0 | 233 |
| CC0-1.0 | 183 |
| CC-BY-4.0 | 53 |
| CC-BY-NC-SA-3.0 | 50 |
| CC-BY-3.0 | 36 |
| Custom | 21 |
| CC-BY-NC-4.0 | 14 |
| CC-BY-SA-4.0 | 9 |
| CC-BY-NC-3.0 | 4 |
| CC-BY-NC-SA-4.0 | 3 |

"Custom" entries need individual reading before any distribution.

Directories containing at least one NC entry:

```
CC-BY-NC-3.0 Audio/Items
CC-BY-NC-3.0 Audio/Items/Artifact
CC-BY-NC-3.0 Audio/Voice/Human
CC-BY-NC-4.0 Audio/Effects
CC-BY-NC-4.0 Audio/Effects/Diseases
CC-BY-NC-4.0 Audio/Effects/Footsteps
CC-BY-NC-4.0 Audio/Items
CC-BY-NC-4.0 Audio/Machines
CC-BY-NC-4.0 Audio/Misc
CC-BY-NC-4.0 Audio/Voice/Human
CC-BY-NC-4.0 Audio/Weapons/Guns/MagIn
CC-BY-NC-4.0 Audio/Weapons/Guns/Misc
CC-BY-NC-SA-3.0 Audio/Ambience/Antag
CC-BY-NC-SA-3.0 Audio/Animals
CC-BY-NC-SA-3.0 Audio/Effects
CC-BY-NC-SA-3.0 Audio/Jukebox
CC-BY-NC-SA-3.0 Audio/Lobby
CC-BY-NC-SA-3.0 Audio/Mecha
CC-BY-NC-SA-3.0 Audio/Voice/Talk
CC-BY-NC-SA-3.0 Audio/Voice/Vulpkanin
CC-BY-NC-SA-3.0 Audio/Voice/Zombie
CC-BY-NC-SA-3.0 Audio/Weapons/Guns/Misc
CC-BY-NC-SA-3.0 Audio/Weapons/Xeno
CC-BY-NC-SA-3.0 Textures/LobbyScreens
CC-BY-NC-SA-3.0 Textures/Parallaxes
CC-BY-NC-SA-3.0 Textures/Tiles
CC-BY-NC-SA-4.0 Audio/Animals
CC-BY-NC-SA-4.0 Textures/Parallaxes
```

Notably this includes lobby music/screens, parallaxes, tiles and speech sounds (`Audio/Voice/Talk`), which are hard to avoid in a normal build.

## Other license files

| File | Notes |
|---|---|
| `Resources/Audio/MidiCustom/LICENSE` | GeneralUser GS v2.0.1 soundfont license |
| `Resources/Fonts/NotoSans/LICENSE.txt`, `Fonts/NotoSansDisplay/LICENSE_OFL.txt` | SIL Open Font License |
| `Resources/Fonts/RobotoMono/LICENSE.txt` | Apache License 2.0 |
| `Resources/Fonts/Boxfont-round/credits.txt` | credits file (read before redistribution) |

## Open points for the owner (not decided here)
- NC assets (textures + audio) restrict any commercial distribution; stripping/replacing them would be a separate task.
- GPLv3 portions of RobustToolbox matter if engine code is redistributed in modified form.
- License for our own `Cognition/` code is not chosen yet.

## Appendix — NC textures

```
CC-BY-NC-3.0 Textures/Clothing/Shoes/Boots/performer.rsi
CC-BY-NC-3.0 Textures/Clothing/Shoes/Misc/snakeskin.rsi
CC-BY-NC-3.0 Textures/Clothing/Uniforms/Jumpskirts/Service/performer.rsi
CC-BY-NC-3.0 Textures/Structures/Furniture/Memorials/generic_memorial.rsi
CC-BY-NC-4.0 Textures/Objects/Weapons/Guns/Projectiles/nanite_crystal.rsi
CC-BY-NC-4.0 Textures/Objects/Weapons/Guns/Projectiles/xeno_toxic.rsi
CC-BY-NC-4.0 Textures/Objects/Weapons/Guns/Projectiles/xenoborg_guided_missile.rsi
CC-BY-NC-4.0 Textures/Objects/Weapons/Guns/Shotguns/sawn_inhands_64x.rsi
CC-BY-NC-4.0 Textures/Objects/Weapons/Guns/Turrets/xenoturret.rsi
CC-BY-NC-SA-3.0 Textures/Clothing/Eyes/Glasses/noir.rsi
CC-BY-NC-SA-3.0 Textures/Clothing/Head/Soft/bizarresoft.rsi
CC-BY-NC-SA-3.0 Textures/Clothing/Uniforms/Jumpsuits/Color/rainbow.rsi
CC-BY-NC-SA-3.0 Textures/Decals/Overlays/greyscale.rsi
CC-BY-NC-SA-3.0 Textures/Mobs/Animals/possum_old.rsi
CC-BY-NC-SA-3.0 Textures/Mobs/Animals/raccoon.rsi
CC-BY-NC-SA-3.0 Textures/Mobs/Animals/scurret/scurret.rsi
CC-BY-NC-SA-3.0 Textures/Mobs/Pets/ferret.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Consumable/Drinks/alienbrainhemorrhage.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Consumable/Drinks/bronx.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Consumable/Drinks/crushdepth.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Consumable/Drinks/dark&stormy.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Consumable/Drinks/electricshark.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Consumable/Drinks/jackrose.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Consumable/Drinks/junglebird.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Consumable/Drinks/kalimotxo.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Consumable/Drinks/monkeybusiness.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Consumable/Drinks/radler.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Consumable/Drinks/tortuga.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Consumable/Drinks/vampiro.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Economy/cash.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Fun/Balls/tennisball.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Fun/Foam/foam_grenade.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Fun/Plushies/vulp.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Fun/rubber_hammer.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Misc/buffering.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Misc/chopstick.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Misc/flies.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Misc/killsign.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Power/powersink.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Specific/Xenoarchaeology/artifact_fragments.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Specific/Xenoarchaeology/item_artifacts.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Specific/Xenoarchaeology/xeno_artifacts.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Tools/omnitool.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Weapons/Guns/Battery/energy_magnum.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Weapons/Guns/Projectiles/projectiles_magnum.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Weapons/Melee/baseball_bat.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Weapons/Melee/chainsaw.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Weapons/Melee/fireaxe.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Weapons/Melee/fireaxeflaming.rsi
CC-BY-NC-SA-3.0 Textures/Objects/Weapons/Melee/incomplete_bat.rsi
CC-BY-NC-SA-3.0 Textures/Structures/Furniture/golden_toilet.rsi
CC-BY-NC-SA-3.0 Textures/Structures/Furniture/toilet.rsi
CC-BY-NC-SA-3.0 Textures/Structures/Piping/Atmospherics/directionalfan.rsi
CC-BY-NC-SA-3.0 Textures/Structures/Power/Generation/Singularity/collector.rsi
CC-BY-NC-SA-3.0 Textures/Structures/Power/cell_recharger.rsi
CC-BY-NC-SA-3.0 Textures/Structures/monitors.rsi
CC-BY-NC-SA-4.0 Textures/Mobs/Animals/buffrat.rsi
CC-BY-NC-SA-4.0 Textures/Objects/Consumable/Drinks/glass_clear.rsi
CC-BY-NC-SA-4.0 Textures/Objects/Consumable/Drinks/glass_coupe_shape.rsi
CC-BY-NC-SA-4.0 Textures/Objects/Consumable/Drinks/glue-tube.rsi
CC-BY-NC-SA-4.0 Textures/Objects/Consumable/Drinks/lube-tube.rsi
CC-BY-NC-SA-4.0 Textures/Objects/Misc/guardian_info.rsi
```
