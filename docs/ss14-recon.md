# SS14 recon for later phases (Phase 0.7, read-only)

Snapshot of upstream `space-wizards/space-station-14` as cloned in Phase 0 (2026-09-27). No code was changed.
Paths are repo-relative. Constants marked ✓ were re-checked against the source.

## Quick reference for the Cognition bridge

| Need | Hook / API |
|---|---|
| What can the agent see | `ExamineSystemShared.InRangeUnOccluded`, `ExamineRange = 16` ✓, light level via `LightLevelSystem` ✓ (server-side) |
| What can the agent hear | Add `ActiveListenerComponent` ✓ to the mob and subscribe to `ListenEvent` ✓. `VoiceRange = 10` ✓, whisper clear 2 / muffled 5 ✓ |
| What can the agent do | `SharedVerbSystem.GetLocalVerbs(target, user, …)`, interaction via `SharedInteractionSystem.UserInteraction`. `InteractionRange = 1.5` ✓ |
| How to move it | Write `InputMoverComponent` (as `NPCSteeringSystem.SetDirection` does), pathfind with `PathfindingSystem.GetPath` |
| Body without a player | `SharedMindSystem.CreateMind(null, name)` ✓ + `TransferTo(mind, mob)` |
| Needs / state | `SatiationSystem` (hunger + thirst unified), `DamageableSystem`, `MobStateSystem`, `TemperatureSystem`, `RespiratorSystem`, `AtmosphereSystem.GetTileMixture` |
| Audit trail | Admin logs (`LogType`, table `admin_log`). Pickup=36, InteractHand=31, InteractActivate=32, InteractUsing=92 |

---

## 1. Vision / FOV / occlusion, lighting

**Key files:** `Content.Shared/Examine/ExamineSystemShared.cs`, `Content.Shared/Examine/ExaminerComponent.cs`, `Content.Server/Examine/ExamineSystem.cs`, `RobustToolbox/Robust.Shared/GameObjects/Components/Light/OccluderComponent.cs`, `RobustToolbox/Robust.Shared/GameObjects/Systems/OccluderSystem.cs`, `RobustToolbox/Robust.Shared/GameObjects/Components/Eye/EyeComponent.cs`, `RobustToolbox/Robust.Shared/GameObjects/Systems/SharedEyeSystem.cs`, `RobustToolbox/Robust.Shared/GameObjects/Components/Light/SharedPointLightComponent.cs`, `RobustToolbox/Robust.Shared/Light/LightLevelSystem.cs`. Related: `Content.Shared/Silicons/StationAi/StationAiVisionSystem.cs` (tile line of sight via occluders).

- Ranges: `ExamineRange = 16`, `ExamineDetailsRange = 3`, `CritExamineRange = 1.3`, `DeadExamineRange = 0.75`, `MaxRaycastRange = 100`. `GetExaminerRange()` shrinks the range for dead, crit, blind or blurry examiners.
- `InRangeUnOccluded` raycasts through `OccluderSystem.IntersectRay`. An `OccluderComponent` hit blocks unless the origin or target is inside the occluder. `InRangeOverrideEvent` can short-circuit per entity.
- Eye/FOV: `EyeComponent.DrawFov`/`DrawLight`, `VisibilityMask`, `PvsScale`. `FloorOccluderComponent` is only for shaders, not line of sight.
- Lighting: `LightLevelSystem.CalculateLightLevel` / `TryCalculateLightLevel` / `CalculateLightColor` work **server-side** (light tree + occluders). Caveat: they can disagree with the client for non-networked or client-animated lights.

## 2. Chat / speech / whisper

**Key files:** `Content.Shared/Chat/SharedChatSystem.cs`, `Content.Shared/Chat/SharedChatEvents.cs`, `Content.Server/Chat/Systems/ChatSystem.cs` (+ `.PrivateAPI.cs`, `.Utility.cs`), `Content.Shared/Speech/EntitySystems/ListeningSystem.cs`, `Content.Shared/Speech/ListenEvent.cs`, `Content.Shared/Speech/Components/ActiveListenerComponent.cs`, `Content.Shared/Speech/EntitySystems/AccentSystem.cs`, `Content.Server/Radio/EntitySystems/RadioSystem.cs`, `HeadsetSystem.cs`.

- `VoiceRange = 10`, `WhisperClearRange = 2`, `WhisperMuffledRange = 5` (world units). The whisper prefix is `,` and radio uses `;` / `:key`.
- Player routing: `GetRecipients(source, range)` returns sessions on the same map within the range. Normal speech does **not** check occlusion. Whisper uses `InRangeUnOccluded` to decide between clear, muffled and unknown speaker.
- `EntitySpokeEvent` is raised on the speaker. Accents run through `TransformSpeechEvent` → `AccentGetEvent`. There is no real language system; "languages" are accent transforms.
- **Hearing hook for agents:** give the mob `ActiveListenerComponent` (default range = `VoiceRange`). `ListeningSystem` raises `ListenAttemptEvent` then `ListenEvent` on each listener in range, with whisper obfuscation. Radio mics, parrots and voice triggers already use this.

## 3. NPC / HTN, pathfinding, steering

**Key files:** `Content.Server/NPC/Systems/NPCSystem.cs`, `Content.Server/NPC/HTN/HTNSystem.cs`, `HTNComponent.cs`, `HTNPlanJob.cs`, `HTNCompoundPrototype.cs`, `PrimitiveTasks/HTNPrimitiveTask.cs`, `HTNOperator.cs`, `Preconditions/HTNPrecondition.cs`, `Operators/MoveToOperator.cs`, `Content.Server/NPC/NPCBlackboard.cs`, `Content.Server/NPC/Pathfinding/PathfindingSystem.cs`, `Content.Server/NPC/Systems/NPCSteeringSystem.cs`, `Content.Shared/NPC/SharedNPCSteeringSystem.cs`. Prototypes: `Resources/Prototypes/NPCs/`.

- `HTNComponent` (extends `NPCComponent`): `rootTask`, `Blackboard`, `PlanCooldown = 0.45s`. `WakeNPC` adds `ActiveNPCComponent`. **Attaching a player puts the HTN to sleep.**
- Planner: `HTNPlanJob` runs async on a job queue. It expands compound tasks branch by branch, checks preconditions, and collects effects from operators. Execution goes through `Startup` / `Update` / `TaskShutdown`, which return `Continuing` / `Finished` / `Failed` / `BetterPlan`. CVars: `npc.enabled`, `npc.max_updates` (128), `npc.pathfinding`.
- Pathfinding: `PathfindingSystem.GetPath` / `GetPathSafe` (A*/BFS, collision mask, `PathFlags`).
- Steering: context steering over 12 directions. `SetDirection` writes `InputMoverComponent`, which is the same mover pipeline players use. Blackboard defaults: `VisionRadius` 10, `MovementRange` 1.5.

## 4. Verbs, interaction, hands, inventory, storage

**Key files:** `Content.Shared/Verbs/SharedVerbSystem.cs`, `VerbEvents.cs`, `Verb.cs`, `VerbCategory.cs`, `Content.Server/Verbs/VerbSystem.cs`; `Content.Shared/Interaction/SharedInteractionSystem.cs` (+ `.Blocking.cs`, `.Relay.cs`), `InteractHand.cs`, `InteractUsing.cs`, `ActivateInWorldEvent.cs`; `Content.Shared/Hands/EntitySystems/SharedHandsSystem*.cs`; `Content.Shared/Inventory/InventorySystem*.cs`; `Content.Shared/Storage/EntitySystems/SharedStorageSystem.cs`; `Content.Shared.Database/LogType.cs`.

- Verbs: `GetLocalVerbs(target, user, types, force)` raises `GetVerbsEvent<T>`. Most verb types go to the target, `UtilityVerb` to the held item, `InnateVerb` to the user. Access is checked with `InRangeAndAccessible` + `ActionBlockerSystem`. The server re-validates on `ExecuteVerbEvent`. This is the natural source for the agent's action menu (spec RJ-05: never offer impossible actions).
- Interaction: `InteractionRange = 1.5`. `InRangeUnobstructed` raycasts against Impassable | InteractImpassable. `UserInteraction` dispatches to `InteractHand` / `UseInHand` / `InteractUsing` / ranged. Alt-click runs the top `AlternativeVerb`.
- Hands are `ContainerSlot`s. `TryPickup` requires a free hand, an `ItemComponent`, `ActionBlocker.CanPickup`, a whitelist match and container insertability. Inventory: `TryEquip` / `CanEquip`, slot enumerator. Storage: grid size, whitelist and nesting rules.

## 5. Sleep, beds, hunger/thirst, damage, temperature, respiration

**Key files:** `Content.Shared/Bed/Sleep/SleepingSystem.cs`, `Content.Shared/Bed/BedSystem.cs`, `Content.Shared/Buckle/SharedBuckleSystem*.cs`; `Content.Shared/Nutrition/EntitySystems/SatiationSystem.cs` (+ `.Proxy.cs`), `SatiationComponent.cs`, `SatiationDamageSystem.cs`; `Content.Shared/Damage/Systems/DamageableSystem.cs`, `Content.Shared/Mobs/Systems/MobStateSystem.cs`, `MobThresholdSystem.cs`; `Content.Server/Temperature/Systems/TemperatureSystem.cs`; `Content.Server/Body/Systems/RespiratorSystem.cs`, `Content.Server/Atmos/EntitySystems/BarotraumaSystem.cs`.

- **Hunger and thirst were unified**: there is no `HungerSystem`/`ThirstSystem` anymore. Use `SatiationSystem` with the Hunger/Thirst satiation types (`ModifyValue`, `TryGetValueByThreshold`). The spec's references to hunger/thirst systems must map to this.
- Sleep: `TrySleeping` adds `SleepingComponent`, which blocks speech, sight, standing and self-unbuckle. The entity wakes on damage ≥ `WakeThreshold`. Beds are straps with `HealOnBuckleComponent`, which grant the sleep action and heal while buckled.
- Damage: `TryChangeDamage` raises `DamageChangedEvent` → `MobThresholdSystem` → `MobStateSystem` (Alive / Critical / Dead).
- Temperature exchanges heat with atmos (`AtmosExposedUpdateEvent`). Respiration: low saturation causes gasping and suffocation damage. Barotrauma ticks once per second using pressure seen through protective gear.

## 6. Atmos, puddles/fluids

**Key files:** `Content.Shared/Atmos/GasMixture.cs`, `TileAtmosphere.cs`, `Components/GridAtmosphereComponent.cs`, `Components/MapAtmosphereComponent.cs`, `Content.Server/Atmos/EntitySystems/AtmosphereSystem*.cs` (`.API.cs`, `.CVars.cs`, …), `Content.Shared/CCVar/CCVars.Atmos.cs`, `Content.Server/Fluids/EntitySystems/PuddleSystem.cs` (+ `.Spillable.cs`).

- Each tile's air is `TileAtmosphere.Air` (a `GasMixture`). Maps provide a default mixture (space).
- Queries: `GetTileMixture(entity)` or `GetContainingMixture(ent, ignoreExposed, excite)`, then read `.Pressure` / `.Temperature`.
- Puddles: `PuddleSystem.TrySpillAt` / `TrySplashSpillAt` spawn a `PuddleComponent` holding a chemistry `Solution`.

## 7. Map save/load, entity serialization (for the §16 spike)

**Key files:** `RobustToolbox/Robust.Shared/EntitySerialization/Systems/MapLoaderSystem.cs` (+ `.Save.cs`, `.Load.cs`, `.LoadMap.cs`), `EntitySerializer.cs`, `EntityDeserializer.cs`, `Options.cs`, `RobustToolbox/Robust.Server/Console/Commands/MapCommands.cs`, `RobustToolbox/Robust.Shared/Serialization/Manager/SerializationManager.cs`.

- `TrySaveMap` / `TrySaveGrid` / `TryLoadMap`. Map format version is 7. Console commands: `savemap`, `loadmap`, `savegrid`, `loadgrid`.
- **Saving an initialized (running) map is blocked** unless forced (`savemap <id> <path> true`). It is a mapper tool, not a save-game feature.
- Round state does not survive a save/reload cleanly: minds in nullspace, sessions, the game ticker, rules and antags are all lost. The §16 spike should assume this limitation.

## 8. Mind / role / job, character profiles

**Key files:** `Content.Shared/Mind/MindComponent.cs`, `SharedMindSystem.cs`, `Content.Server/Mind/MindSystem.cs`, `Content.Shared/Mind/Components/MindContainerComponent.cs`, `Content.Shared/Roles/SharedRoleSystem.cs`, `JobPrototype.cs`, `Content.Server/Station/Systems/ServerStationJobsSystem.cs`, `Content.Shared/Preferences/HumanoidCharacterProfile.cs`, `Content.Server/Ghost/Roles/GhostRoleSystem.cs`.

- A mind is a nullspace entity with a `MindComponent` (`UserId`, `OwnedEntity`, objectives). The body points back to it through `MindContainerComponent.Mind`. `TransferTo(mind, entity)` moves the mind into a body.
- Roles are child entities of the mind (`MindAddRole` / `MindAddJobRole`). Jobs are assigned per station by `ServerStationJobsSystem`.
- **A mind without a player** works: `CreateMind(null, name)` + `TransferTo`. Ghost roles use the same path with a `UserId`.
- Observed in Phase 0: the Dev map only has Captain slots. A player with no available job spawns as an observer ghost (see `docs/reports/phase0-playtest.md`).

## 9. Game presets & antagonist rules

**Key files:** `Content.Shared/GameTicking/Prototypes/GamePresetPrototype.cs`, `GameTicker.GameRule.cs`, `Content.Server/GameTicking/ServerGameTicker.GamePreset.cs`, `ServerGameTicker.RoundFlow.cs`, `Content.Shared/GameTicking/Rules/GameRuleSystem.cs`, `Content.Shared/Antag/AntagSelectionSystem*.cs`, `Content.Shared/CCVar/CCVars.Game.cs`, `CCVars.Events.cs`, `Content.Server/StationEvents/EventManagerSystem.cs`.

- A preset lists rule prototypes. Flow: `AddGamePresetRules` → `AddGameRule` → `StartGamePresetRules` → `StartGameRule`, optionally delayed by `DelayedStartRuleComponent`.
- CVars: `game.defaultpreset` (upstream default `secret`, ours `Sandbox`), `game.fallbackpreset`, `game.ignoredpresets`, `events.enabled` (gates mid-round station events).
- Antags: `AntagSelectionSystem` runs for rules that carry an `AntagSelectionComponent`, via `AssignAntags` → `TryMakeAntag`.
