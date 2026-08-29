using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using RiskOfOptions;
using RiskOfOptions.Options;

namespace VengeanceSwarmRedux
{
    public static class RiskOfOptionsIntegration
    {
        public const string GUID = "com.rune580.riskofoptions";
        public static bool IsLoaded => Chainloader.PluginInfos.ContainsKey(GUID);

        public static void Init(
            ConfigEntry<bool> isEnabled,
            ConfigEntry<bool> allowBothDrops,
            ConfigEntry<bool> enableHighPopFix,
            ConfigEntry<bool> enableDebugLogging)
        {
            if (IsLoaded)
            {
                try
                {
                    InitInternal(isEnabled, allowBothDrops, enableHighPopFix, enableDebugLogging);
                }
                catch (System.Exception ex)
                {
                    Plugin.InstanceLogger?.LogWarning($"RiskOfOptions integration failed to initialize: {ex.Message}");
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InitInternal(
            ConfigEntry<bool> isEnabled,
            ConfigEntry<bool> allowBothDrops,
            ConfigEntry<bool> enableHighPopFix,
            ConfigEntry<bool> enableDebugLogging)
        {
            ModSettingsManager.SetModDescription("Fixes Doppelgangers (Umbras) skin synchronization and item/drop distribution when using Artifact of Vengeance and Artifact of Swarms.");
            ModSettingsManager.AddOption(new CheckBoxOption(isEnabled));
            ModSettingsManager.AddOption(new CheckBoxOption(allowBothDrops));
            ModSettingsManager.AddOption(new CheckBoxOption(enableHighPopFix));
            ModSettingsManager.AddOption(new CheckBoxOption(enableDebugLogging));
        }
    }
}
