using System;
using System.Reflection;
using System.Security;
using System.Security.Permissions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using RoR2;
using RoR2.Artifacts;
using RoR2.ContentManagement;
using UnityEngine;
using UnityEngine.Networking;

#pragma warning disable CS0618
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618

namespace VengeanceSwarmRedux
{
    [BepInDependency(RiskOfOptionsIntegration.GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.FortressForce.VengeanceSwarmRedux";
        public const string PluginName = "VengeanceSwarmRedux";
        public const string PluginVersion = "1.0.0";

        public static ManualLogSource InstanceLogger { get; private set; } = null!;
        public static Plugin Instance { get; private set; } = null!;

        // Configuration Entries
        public static ConfigEntry<bool> IsEnabled { get; private set; } = null!;
        public static ConfigEntry<bool> AllowBothDrops { get; private set; } = null!;
        public static ConfigEntry<bool> EnableHighPopFix { get; private set; } = null!;
        public static ConfigEntry<bool> EnableDebugLogging { get; private set; } = null!;

        // Cached Reflection Fields for MasterCopySpawnCard
        private static readonly FieldInfo FieldSrcItemStacks = typeof(MasterCopySpawnCard).GetField("srcItemStacks", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo FieldSrcTempItemRawValues = typeof(MasterCopySpawnCard).GetField("srcTempItemRawValues", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo FieldSrcEquipment = typeof(MasterCopySpawnCard).GetField("srcEquipment", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo FieldOnPreSpawnSetup = typeof(MasterCopySpawnCard).GetField("onPreSpawnSetup", BindingFlags.Instance | BindingFlags.NonPublic);

        public void Awake()
        {
            Instance = this;
            InstanceLogger = Logger;

            InitConfig();
            RiskOfOptionsIntegration.Init(
                IsEnabled,
                AllowBothDrops,
                EnableHighPopFix,
                EnableDebugLogging
            );

            RegisterHooks();

            InstanceLogger.LogInfo($"{PluginName} v{PluginVersion} initialized successfully.");
        }

        private void InitConfig()
        {
            IsEnabled = Config.Bind(
                "1. General",
                "Enabled",
                true,
                "Enables or disables the mod entirely."
            );

            AllowBothDrops = Config.Bind(
                "2. Gameplay",
                "Allow Both Swarm Drops",
                true,
                "If true, both Doppelgangers spawned under Artifact of Swarms will drop an item when defeated. If false, only one drop is awarded per invasion wave."
            );

            EnableHighPopFix = Config.Bind(
                "2. Gameplay",
                "High Population Fix",
                true,
                "Ensures Swarms Doppelganger clones ignore team member limits so they always spawn even when the map has high enemy population."
            );

            EnableDebugLogging = Config.Bind(
                "3. Debug",
                "Enable Debug Logging",
                false,
                "Enables detailed console logging for spawn events, item copies, and skin applications."
            );
        }

        private void RegisterHooks()
        {
            // 1. Fix Item Stacks clearing in MasterCopySpawnCard during Swarms spawns
            On.RoR2.MasterCopySpawnCard.GetPreSpawnSetupCallback += OnMasterCopySpawnCardGetPreSpawnSetupCallback;

            // 2. Fix Swarms spawn high population limit & drop tracking
            On.RoR2.Artifacts.SwarmsArtifactManager.OnSpawnCardOnSpawnedServerGlobal += OnSwarmsArtifactManagerOnSpawnCardOnSpawnedServerGlobal;

            // 3. Fix Skin Sync on Server (when Loadout is assigned)
            On.RoR2.CharacterBody.SetLoadoutServer += OnCharacterBodySetLoadoutServer;

            // 4. Fix Skin Sync when Master link updates on Body Start
            On.RoR2.CharacterBody.UpdateMasterLink += OnCharacterBodyUpdateMasterLink;

            // 5. Fix Skin Sync on ModelSkinController Start timing race condition
            On.RoR2.ModelSkinController.Start += OnModelSkinControllerStart;

            // 6. Fix Skin Sync on Client when CharacterMaster receives Loadout SyncVar
            On.RoR2.CharacterMaster.OnDeserialize += OnCharacterMasterOnDeserialize;

            // 7. Handle single drop suppression if AllowBothDrops is false
            On.RoR2.Artifacts.DoppelgangerInvasionManager.OnCharacterDeathGlobal += OnDoppelgangerDeathGlobal;
        }

        #region Item & Spawn Fixes

        private Action<CharacterMaster> OnMasterCopySpawnCardGetPreSpawnSetupCallback(
            On.RoR2.MasterCopySpawnCard.orig_GetPreSpawnSetupCallback orig,
            MasterCopySpawnCard self)
        {
            if (!IsEnabled.Value)
            {
                return orig(self);
            }

            return (CharacterMaster spawnedMaster) =>
            {
                if (!spawnedMaster || !spawnedMaster.inventory)
                {
                    return;
                }

                try
                {
                    // Copy permanent items without clearing the source array immediately
                    int[] srcItemStacks = FieldSrcItemStacks?.GetValue(self) as int[];
                    if (srcItemStacks != null)
                    {
                        spawnedMaster.inventory.AddItemsFrom(srcItemStacks, _ => true);
                    }

                    // Copy temporary item values
                    float[] srcTemp = FieldSrcTempItemRawValues?.GetValue(self) as float[];
                    if (srcTemp != null)
                    {
                        for (int i = 0; i < srcTemp.Length; i++)
                        {
                            if (srcTemp[i] > 0f)
                            {
                                spawnedMaster.inventory.GiveItemTemp((ItemIndex)i, srcTemp[i]);
                            }
                        }
                    }

                    // Copy equipment slots
                    EquipmentIndex[][] srcEquipment = FieldSrcEquipment?.GetValue(self) as EquipmentIndex[][];
                    if (srcEquipment != null)
                    {
                        for (uint slot = 0; slot < srcEquipment.Length; slot++)
                        {
                            if (srcEquipment[slot] != null)
                            {
                                for (uint set = 0; set < srcEquipment[slot].Length; set++)
                                {
                                    spawnedMaster.inventory.SetEquipmentIndexForSlot(srcEquipment[slot][set], slot, set);
                                }
                            }
                        }
                    }

                    // Ensure InvadingDoppelganger item is present if this is a Doppelganger spawn card
                    if (self is DoppelgangerSpawnCard)
                    {
                        if (spawnedMaster.inventory.GetItemCountEffective(RoR2Content.Items.InvadingDoppelganger) == 0)
                        {
                            spawnedMaster.inventory.GiveItemPermanent(RoR2Content.Items.InvadingDoppelganger);
                        }
                    }

                    // Invoke onPreSpawnSetup callback (AI enemy targeting)
                    Action<CharacterMaster> onPreSpawn = FieldOnPreSpawnSetup?.GetValue(self) as Action<CharacterMaster>;
                    onPreSpawn?.Invoke(spawnedMaster);

                    if (EnableDebugLogging.Value)
                    {
                        InstanceLogger.LogInfo($"[ItemSync] Successfully applied inventory snapshot to spawned master: {spawnedMaster.name}");
                    }
                }
                catch (Exception ex)
                {
                    InstanceLogger.LogError($"[ItemSync] Error in pre-spawn setup callback: {ex}");
                }
            };
        }

        private void OnSwarmsArtifactManagerOnSpawnCardOnSpawnedServerGlobal(
            On.RoR2.Artifacts.SwarmsArtifactManager.orig_OnSpawnCardOnSpawnedServerGlobal orig,
            SpawnCard.SpawnResult result)
        {
            if (IsEnabled.Value && EnableHighPopFix.Value && result.spawnRequest != null)
            {
                // Ensure Swarms Doppelganger requests bypass team member limit
                if (result.spawnRequest.spawnCard is DoppelgangerSpawnCard ||
                    (result.spawnedInstance && result.spawnedInstance.GetComponent<CharacterMaster>()?.inventory?.GetItemCountEffective(RoR2Content.Items.InvadingDoppelganger) > 0))
                {
                    result.spawnRequest.ignoreTeamMemberLimit = true;
                }
            }

            orig(result);
        }

        #endregion

        #region Skin Synchronization Fixes

        private void OnCharacterBodySetLoadoutServer(
            On.RoR2.CharacterBody.orig_SetLoadoutServer orig,
            CharacterBody self,
            Loadout loadout)
        {
            orig(self, loadout);

            if (!IsEnabled.Value || !self || loadout == null)
            {
                return;
            }

            try
            {
                uint skin = loadout.bodyLoadoutManager.GetSkinIndex(self.bodyIndex);
                self.skinIndex = skin;

                if (EnableDebugLogging.Value)
                {
                    InstanceLogger.LogInfo($"[SkinSync] SetLoadoutServer: Body {self.name} assigned skinIndex {skin}");
                }
            }
            catch (Exception ex)
            {
                InstanceLogger.LogError($"[SkinSync] Error in SetLoadoutServer hook: {ex}");
            }
        }

        private void OnCharacterBodyUpdateMasterLink(
            On.RoR2.CharacterBody.orig_UpdateMasterLink orig,
            CharacterBody self)
        {
            orig(self);

            if (!IsEnabled.Value || !self || !self.master || self.master.loadout == null)
            {
                return;
            }

            try
            {
                uint preferredSkin = self.master.loadout.bodyLoadoutManager.GetSkinIndex(self.bodyIndex);
                ApplySkinToBody(self, preferredSkin);
            }
            catch (Exception ex)
            {
                InstanceLogger.LogError($"[SkinSync] Error in UpdateMasterLink hook: {ex}");
            }
        }

        private void OnModelSkinControllerStart(
            On.RoR2.ModelSkinController.orig_Start orig,
            ModelSkinController self)
        {
            if (IsEnabled.Value && self)
            {
                try
                {
                    CharacterModel characterModel = self.GetComponent<CharacterModel>();
                    if (characterModel && characterModel.body && characterModel.body.master && characterModel.body.master.loadout != null)
                    {
                        uint preferredSkin = characterModel.body.master.loadout.bodyLoadoutManager.GetSkinIndex(characterModel.body.bodyIndex);
                        if (preferredSkin > 0)
                        {
                            characterModel.body.skinIndex = preferredSkin;
                        }
                    }
                }
                catch (Exception ex)
                {
                    InstanceLogger.LogError($"[SkinSync] Error in ModelSkinController.Start hook: {ex}");
                }
            }

            orig(self);
        }

        private void OnCharacterMasterOnDeserialize(
            On.RoR2.CharacterMaster.orig_OnDeserialize orig,
            CharacterMaster self,
            NetworkReader reader,
            bool initialState)
        {
            orig(self, reader, initialState);

            if (!IsEnabled.Value || !self || NetworkServer.active || self.loadout == null)
            {
                return;
            }

            try
            {
                CharacterBody body = self.GetBody();
                if (body)
                {
                    uint preferredSkin = self.loadout.bodyLoadoutManager.GetSkinIndex(body.bodyIndex);
                    ApplySkinToBody(body, preferredSkin);
                }
            }
            catch (Exception ex)
            {
                InstanceLogger.LogError($"[SkinSync] Error in CharacterMaster.OnDeserialize hook: {ex}");
            }
        }

        private static void ApplySkinToBody(CharacterBody body, uint skinIndex)
        {
            if (!body) return;

            body.skinIndex = skinIndex;

            if (body.modelLocator && body.modelLocator.modelTransform)
            {
                ModelSkinController skinController = body.modelLocator.modelTransform.GetComponent<ModelSkinController>();
                if (skinController && skinController.skins != null && skinIndex < (uint)skinController.skins.Length)
                {
                    if (skinController.currentSkinIndex != (int)skinIndex)
                    {
                        if (EnableDebugLogging.Value)
                        {
                            InstanceLogger.LogInfo($"[SkinSync] Applying skinIndex {skinIndex} to ModelSkinController on body {body.name}");
                        }
                        skinController.StartCoroutine(skinController.ApplySkinAsync((int)skinIndex, AsyncReferenceHandleUnloadType.AtWill));
                    }
                }
            }
        }

        #endregion

        #region Loot Drop Management

        private static int _lastInvasionCycleDropped = -1;

        private void OnDoppelgangerDeathGlobal(
            On.RoR2.Artifacts.DoppelgangerInvasionManager.orig_OnCharacterDeathGlobal orig,
            DoppelgangerInvasionManager self,
            DamageReport damageReport)
        {
            if (!IsEnabled.Value || AllowBothDrops.Value || !self)
            {
                // Default / vanilla behavior: all qualifying doppelgangers drop items
                orig(self, damageReport);
                return;
            }

            // If user explicitly configured AllowBothDrops = false, only allow 1 drop per invasion cycle
            int currentCycle = -1;
            try
            {
                MethodInfo getCurrentCycle = typeof(DoppelgangerInvasionManager).GetMethod("GetCurrentInvasionCycle", BindingFlags.Instance | BindingFlags.NonPublic);
                if (getCurrentCycle != null)
                {
                    currentCycle = (int)getCurrentCycle.Invoke(self, null);
                }
            }
            catch
            {
                currentCycle = Run.instance ? Mathf.FloorToInt(Run.instance.GetRunStopwatch() / 600f) : -1;
            }

            if (_lastInvasionCycleDropped == currentCycle && currentCycle != -1)
            {
                if (EnableDebugLogging.Value)
                {
                    InstanceLogger.LogInfo($"[LootDrop] Suppressing second swarm doppelganger drop for cycle {currentCycle} (AllowBothDrops is disabled).");
                }
                return;
            }

            _lastInvasionCycleDropped = currentCycle;
            orig(self, damageReport);
        }

        #endregion
    }
}
