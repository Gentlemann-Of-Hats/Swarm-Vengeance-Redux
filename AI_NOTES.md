# AI Scratch Notes & Architecture Summary: Vengeance Swarm Redux

## 📑 Bookmarks & Index
Jump directly to specific subsystem documentation:
- [1. Overview & Problem Definition](#1-overview--problem-definition)
- [2. Inventory & Item Copying Pipeline](#2-inventory--item-copying-pipeline) (Affects: `MasterCopySpawnCard`, Item Stacks, `InvadingDoppelganger`, Drops)
- [3. Skin Synchronization Pipeline](#3-skin-synchronization-pipeline) (Affects: `CharacterBody`, `ModelSkinController`, `CharacterMaster`, Multiplayer Sync)
- [4. Swarms Cloning & Population Limits](#4-swarms-cloning--population-limits) (Affects: `SwarmsArtifactManager`, `DirectorSpawnRequest`, `CutHp`)
- [5. Configuration & In-Game Options](#5-configuration--in-game-options) (Affects: `Plugin.cs`, `RiskOfOptionsIntegration.cs`)
- [6. Build, Versioning & Release Workflow](#6-build-versioning--release-workflow) (Affects: `.csproj`, `manifest.json`, `README.md`, Packaging Script)
- [7. Reference Codebases](#7-reference-codebases) (Affects: `ref/RoR2Mods-main`)

---

## 1. Overview & Problem Definition
In vanilla *Risk of Rain 2*, combining the **Artifact of Vengeance** (periodic Doppelganger/Umbra invasions) with the **Artifact of Swarms** (doubled enemy spawns at 50% health) suffered from two ancient bugs:
1. **The Second Clone Loses All Items & Drops No Loot**: The item copying system cleared its pooled memory during the first clone's spawn, causing the second clone to spawn with 0 player items, no `InvadingDoppelganger` item, and no loot drop on death.
2. **Doppelganger Bodies Revert to Default Skin 0**: A race condition during GameObject hierarchy initialization caused `ModelSkinController.Start()` on the visual model to execute before parent `CharacterBody.Start()` / `UpdateMasterLink()` populated the player's selected skin index from `master.loadout`.

---

## 2. Inventory & Item Copying Pipeline
*Affects: [`Plugin.cs:OnMasterCopySpawnCardGetPreSpawnSetupCallback`](file:///home/nicholasjohnson/Code-Projects/source/VenganceSwarmRedux/Plugin.cs#L145-L215), [`MasterCopySpawnCard`](file:///home/nicholasjohnson/Code-Projects/source/VenganceSwarmRedux/ref/RoR2Mods-main/VengeanceSwarmFix/src/SwarmSpawn.cs)*

### Root Cause
1. `DoppelgangerInvasionManager.CreateDoppelganger` instantiates a `DoppelgangerSpawnCard` (inherits `MasterCopySpawnCard`).
2. `MasterCopySpawnCard.CopyDataFromMaster` requests an integer array from `ItemCatalog.RequestItemStackArray()` and fills `srcItemStacks`, `srcTempItemRawValues`, and `srcEquipment`.
3. When Clone 1 spawns, `GetPreSpawnSetupCallback()` copies items into Clone 1's inventory, but terminates with `ItemCatalog.ReturnItemStackArray(srcItemStacks)`, calling `Array.Clear(...)`.
4. When `SwarmsArtifactManager` immediately reuses `result.spawnRequest` to spawn Clone 2, `srcItemStacks` is all zeroes.
5. Clone 2 gets 0 items and no `RoR2Content.Items.InvadingDoppelganger`.
6. When Clone 2 dies, `DoppelgangerInvasionManager.OnCharacterDeathGlobal` skips loot generation because `InvadingDoppelganger` count is 0.

### Fix
* Intercept `On.RoR2.MasterCopySpawnCard.GetPreSpawnSetupCallback`.
* Provide a custom setup callback that reads `srcItemStacks`, `srcTempItemRawValues`, and `srcEquipment` and copies them to `spawnedMaster.inventory`.
* Explicitly verify and ensure `InvadingDoppelganger` item is awarded.
* **Do not** clear or return `srcItemStacks` during intermediate swarm spawns.

---

## 3. Skin Synchronization Pipeline
*Affects: [`Plugin.cs:Skin Synchronization Fixes`](file:///home/nicholasjohnson/Code-Projects/source/VenganceSwarmRedux/Plugin.cs#L237-L368), [`CharacterBody`](file:///home/nicholasjohnson/Code-Projects/source/VenganceSwarmRedux/Plugin.cs#L237), [`ModelSkinController`](file:///home/nicholasjohnson/Code-Projects/source/VenganceSwarmRedux/Plugin.cs#L286), [`CharacterMaster`](file:///home/nicholasjohnson/Code-Projects/source/VenganceSwarmRedux/Plugin.cs#L313)*

### Root Cause
1. `CharacterBody.skinIndex` is a plain field initialized to `0`.
2. `ModelSkinController.Start()` on the child model object calls `ApplySkinAsync((int)characterModel.body.skinIndex, ...)`.
3. In Unity, child component `Start()` methods can execute before parent component `Start()` methods.
4. `CharacterBody.UpdateMasterLink()` (which sets `skinIndex = master.loadout.bodyLoadoutManager.GetSkinIndex(bodyIndex)`) runs in `CharacterBody.Start()`.
5. Because `ModelSkinController.Start()` ran first, it applied Skin 0. When `CharacterBody` later updated `skinIndex`, `ModelSkinController` was not notified.
6. On multiplayer clients, `master.loadout` is deserialized asynchronously; if the body was already spawned, client-side skin application was completely missed.

### Fix
* **Server**: Hook `CharacterBody.SetLoadoutServer` to set `self.skinIndex = loadout.bodyLoadoutManager.GetSkinIndex(self.bodyIndex)` immediately.
* **Master Link Update**: Hook `CharacterBody.UpdateMasterLink` to detect if `ModelSkinController.currentSkinIndex != skinIndex` and re-apply the skin via coroutine.
* **Model Start Guard**: Hook `ModelSkinController.Start` to pre-populate `body.skinIndex` directly from `body.master.loadout` if `body.skinIndex == 0`.
* **Client Deserialization**: Hook `CharacterMaster.OnDeserialize` so that when clients receive `loadout`, they update `resolvedBodyInstance`'s `skinIndex` and apply the skin.

---

## 4. Swarms Cloning & Population Limits
*Affects: [`Plugin.cs:OnSwarmsArtifactManagerOnSpawnCardOnSpawnedServerGlobal`](file:///home/nicholasjohnson/Code-Projects/source/VenganceSwarmRedux/Plugin.cs#L217-L235)*

### Behavior & Fix
* When Swarms spawns the second clone, ensure `result.spawnRequest.ignoreTeamMemberLimit = true` so high enemy counts on the stage do not block the Doppelganger from spawning.
* Both clones receive the standard single stack of `RoR2Content.Items.CutHp` from Swarms.

---

## 5. Configuration & In-Game Options
*Affects: [`RiskOfOptionsIntegration.cs`](file:///home/nicholasjohnson/Code-Projects/source/VenganceSwarmRedux/RiskOfOptionsIntegration.cs), [`Plugin.cs:InitConfig`](file:///home/nicholasjohnson/Code-Projects/source/VenganceSwarmRedux/Plugin.cs#L61-L105)*

| Config Key | Type | Description |
| :--- | :--- | :--- |
| `Enabled` | `bool` | Master toggle for all mod logic. |
| `Allow Both Swarm Drops` | `bool` | When true, both clones drop an item. When false, only 1 drop per invasion cycle. |
| `High Population Fix` | `bool` | Bypasses team limits for Swarms Doppelgangers. |
| `Enable Debug Logging` | `bool` | Verbose BepInEx console output. |

---

## 6. Build, Versioning & Release Workflow
*Affects: [`VenganceSwarmRedux.csproj`](file:///home/nicholasjohnson/Code-Projects/source/VenganceSwarmRedux/VenganceSwarmRedux.csproj), [`../../ThunderStrore-Release/ror2/VenganceSwarmRedux/Release/manifest.json`](file:///home/nicholasjohnson/Code-Projects/ThunderStrore-Release/ror2/VenganceSwarmRedux/Release/manifest.json), [`../../ThunderStrore-Release/ror2/VenganceSwarmRedux/Release/README.md`](file:///home/nicholasjohnson/Code-Projects/ThunderStrore-Release/ror2/VenganceSwarmRedux/Release/README.md), [`zip-vengance-swarm-redux-release.sh`](file:///home/nicholasjohnson/Code-Projects/ThunderStrore-Release/ror2/zip-vengance-swarm-redux-release.sh)*

1. Always synchronize version numbers across:
   - `Plugin.cs` (`PluginVersion = "X.Y.Z"`)
   - `VenganceSwarmRedux.csproj` (`<Version>X.Y.Z</Version>`)
   - `../../ThunderStrore-Release/ror2/VenganceSwarmRedux/Release/manifest.json` (`"version_number": "X.Y.Z"`)
2. Document changes under `Recent Patches` in `../../ThunderStrore-Release/ror2/VenganceSwarmRedux/Release/README.md`.
3. Note: `manifest.json` and `README.md` live exclusively in the Thunderstore release directory to avoid duplicate copies.
4. Run packaging script `/home/nicholasjohnson/Code-Projects/ThunderStrore-Release/ror2/zip-vengance-swarm-redux-release.sh`.

---

## 7. Reference Codebases
- `ref/RoR2Mods-main/VengeanceSwarmFix`: Original mod attempt by Melting-Cube.
- `ref/RoR2Mods-main/BossVengenceRevive`: Revive on boss/doppelganger kill reference.
