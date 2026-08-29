# Objectives - Vengeance Swarm Redux

This document maps out the goals and technical implementation details for fixing the ancient *Risk of Rain 2* Doppelganger skin and item synchronization bugs when playing with **Artifact of Vengeance** and **Artifact of Swarms**.

---

## 🎯 Mod Design Goals

### 1. Item & Equipment Synchronization for Swarms Clones
* Ensure both Doppelgangers spawned under the **Artifact of Swarms** receive the complete player inventory snapshot, equipment, and the `InvadingDoppelganger` item.
* Prevent the memory-pooled array in `MasterCopySpawnCard` from being prematurely wiped during the first clone's spawn.

### 2. Dual Loot Drop on Doppelganger Defeat
* Because both clones properly carry `InvadingDoppelganger`, defeating each clone triggers `DoppelgangerInvasionManager.OnCharacterDeathGlobal`, providing a fair item reward for defeating both Umbras.
* Provide an optional configuration setting in Risk of Options to limit rewards to 1 drop per invasion cycle if desired.

### 3. Skin Synchronization Across Server & Client
* Fix the race condition where `ModelSkinController.Start()` runs before `CharacterBody` links `master.loadout`, resulting in default Skin 0.
* Ensure both clones display the player's selected survivor skin (mastery, alt, and DLC skins) across both singleplayer and multiplayer.

### 4. High Enemy Population Protection
* Bypass map team member limits for Swarms Doppelganger clones to guarantee that the second clone is never blocked from spawning during high-density stages.

---

## 🛠️ Technical Plan & Architecture

### 1. Hooking the Game Logic
* **`RoR2.MasterCopySpawnCard.GetPreSpawnSetupCallback`**:
  * Override the pre-spawn setup callback to copy items, temporary items, and equipment from the card snapshot without clearing `srcItemStacks` during intermediate spawns.
  * Guarantee that `RoR2Content.Items.InvadingDoppelganger` is present on all spawned clones.
* **`RoR2.Artifacts.SwarmsArtifactManager.OnSpawnCardOnSpawnedServerGlobal`**:
  * Ensure `ignoreTeamMemberLimit = true` on the spawn request for Doppelganger clones.
* **`RoR2.CharacterBody.SetLoadoutServer` & `RoR2.CharacterBody.UpdateMasterLink`**:
  * Populate `skinIndex` immediately when loadout is assigned on the server.
  * Check for `ModelSkinController` on the visual model and re-apply the skin if mismatched.
* **`RoR2.ModelSkinController.Start`**:
  * Guard against uninitialized `body.skinIndex` by fetching the preferred skin from `body.master.loadout`.
* **`RoR2.CharacterMaster.OnDeserialize`**:
  * On multiplayer clients, apply the received skin to the active `CharacterBody` upon loadout sync.
* **`RoR2.Artifacts.DoppelgangerInvasionManager.OnCharacterDeathGlobal`**:
  * Support optional suppression of the second drop if the user disabled `Allow Both Swarm Drops` in config.

### 2. In-Game Configuration
* Integrate soft-dependency with **Risk of Options** (`RiskOfOptionsIntegration.cs`) allowing runtime toggles for all fixes, loot rules, and debug logging.

---

## 🗓️ Implementation Checklist

- [x] Create [.csproj](file:///home/nicholasjohnson/Code-Projects/source/VenganceSwarmRedux/VenganceSwarmRedux.csproj) with game assembly and BepInEx references.
- [x] Implement [RiskOfOptionsIntegration.cs](file:///home/nicholasjohnson/Code-Projects/source/VenganceSwarmRedux/RiskOfOptionsIntegration.cs) for config menus.
- [x] Implement [Plugin.cs](file:///home/nicholasjohnson/Code-Projects/source/VenganceSwarmRedux/Plugin.cs) with item snapshot and skin synchronization hooks.
- [x] Configure release packaging with [zip-vengance-swarm-redux-release.sh](file:///home/nicholasjohnson/Code-Projects/ThunderStrore-Release/ror2/zip-vengance-swarm-redux-release.sh).
- [x] Document architecture and bookmarks in [AI_NOTES.md](file:///home/nicholasjohnson/Code-Projects/source/VenganceSwarmRedux/AI_NOTES.md).
- [x] Update [AI_RULES.json](file:///home/nicholasjohnson/Code-Projects/source/VenganceSwarmRedux/AI_RULES.json).
- [x] Compile Debug & Release builds and verify packaging.
