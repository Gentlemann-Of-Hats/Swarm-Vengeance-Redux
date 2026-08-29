# Vengeance Swarm Redux

A modern revival and comprehensive fix for the ancient *Risk of Rain 2* Doppelganger (Umbra) bugs when playing with **Artifact of Vengeance** and **Artifact of Swarms**.

---

## ⚔️ Key Features & Fixes

* **Item Synchronization**: Fixes the base game bug where pooled item arrays were cleared upon the first clone spawning, causing the second Swarms Doppelganger to spawn with 0 player items and missing `InvadingDoppelganger`. Both clones now receive your full inventory and equipment.
* **Skin Synchronization**: Fixes the race condition where `ModelSkinController` initialized before `CharacterBody` / `CharacterMaster` linked the survivor's loadout, causing Doppelgangers to revert to default Skin 0. Both clones now correctly wear your selected survivor skin (including mastery, alt, and DLC skins).
* **Guaranteed Item Drops**: Because both clones now properly receive the `InvadingDoppelganger` item, defeating both clones will drop loot.
* **High Population Fix**: Ensures Swarms Doppelganger spawn requests ignore team member limits so high monster density never prevents your clones from appearing.
* **In-Game Configuration**: Full support for [Risk of Options](https://thunderstore.io/package/Rune580/Risk_Of_Options/) allowing you to toggle individual fixes, choose single vs dual loot drops, or enable debug logging.

---

## ⚙️ Configuration

Settings can be customized in-game via **Risk of Options** or by editing `BepInEx/config/com.FortressForce.VengeanceSwarmRedux.cfg`:

| Setting | Default | Description |
| :--- | :--- | :--- |
| `Enabled` | `true` | Enables or disables the mod. |
| `Allow Both Swarm Drops` | `true` | If true, both Swarms clones drop an item on death. If false, only one drop is awarded per invasion. |
| `High Population Fix` | `true` | Prevents map team limits from blocking Swarms Doppelganger spawns. |
| `Enable Debug Logging` | `false` | Outputs detailed spawn and skin logs to the BepInEx console. |

---

## 📦 Installation

* Install using **r2modman** or your favorite mod manager.
* For manual installation, extract `VenganceSwarmRedux.dll` into your `Risk of Rain 2/BepInEx/plugins/` directory.

---

## 📝 Recent Patches

### v1.0.0
* Initial release of Vengeance Swarm Redux.
* Fixed Doppelganger Swarms inventory clearing bug.
* Fixed Doppelganger skin initialization race condition on server and clients.
* Added Risk of Options integration.
