using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace PassiveForsakenPowers
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class PassiveForsakenPowersPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "radthordax.valheim.PassiveForsakenPowers";
        public const string PluginName = "Passive Forsaken Powers";
        public const string PluginVersion = "1.0.0";

        private static Harmony harmony;

        public static ConfigEntry<int> nexusID;
        public static ConfigEntry<bool> modEnabled;
        public static ConfigEntry<bool> allPowers;

        private void Awake()
        {
            nexusID = Config.Bind<int>("General", "NexusID", 390, "NexusMods ID for updates.");
            modEnabled = Config.Bind<bool>("General", "Enabled", true, "Enable the mod.");
            allPowers = Config.Bind<bool>("General", "AllPowers", false, "Always apply all the Forsaken Powers regardless of character and world progress.");

            if (!modEnabled.Value)
                return;

            harmony = new Harmony(PluginGUID);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        private void OnDestroy()
        {
            harmony.UnpatchSelf();
        }

        // Make the forsaken status effects never expire
        [HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
        internal class Patch_ObjectDB_CopyOtherDB
        {
            static void Postfix(ref ObjectDB __instance)
            {
                foreach (StatusEffect se in __instance.m_StatusEffects)
                {
                    if (se.name.StartsWith("GP_"))
                    {
                        se.m_ttl = 0f;
                        se.m_cooldown = 0f;
                    }
                }
            }
        }

        // Adjust timing of power activation when interacting with the boss item stand
        [HarmonyPatch(typeof(ItemStand), "Awake")]
        internal class Patch_ItemStand_Awake
        {
            static void Postfix(ref ItemStand __instance)
            {
                __instance.m_powerActivationDelay = 1.1f;
            }
        }

        // Allow activating power from boss item stand even if it is currently selected
        [HarmonyPatch(typeof(ItemStand), "IsGuardianPowerActive")]
        internal class Patch_ItemStand_IsGuardianPowerActive
        {
            static bool Prefix(ref bool __result)
            {
                __result = false;
                return false;
            }
        }

        // Apply forsaken power and add trophy for applying effect after future spawns
        [HarmonyPatch(typeof(ItemStand), "DelayedPowerActivation")]
        internal class Patch_ItemStand_DelayedPowerActivation
        {
            static void Postfix(ref ItemStand __instance)
            {
                if (__instance.m_guardianPower == null)
                    return;

                string trophy = __instance.m_supportedItems[0].name;

                Traverse player = Traverse.Create(Player.m_localPlayer);
                player.Field("m_zanim").GetValue<ZSyncAnimation>().SetTrigger("gpower");
                player.Field("m_trophies").GetValue<HashSet<string>>().Add(trophy);

                Debug.Log($"[{PluginName}] Added {trophy} and applied associated forsaken power.");
            }
        }

        // Prevent power activation via keybinding as it is no longer necessary
        [HarmonyPatch(typeof(Player), "StartGuardianPower")]
        internal class Patch_Player_StartGuardianPower
        {
            static bool Prefix()
            {
                return false;
            }
        }

        // Prevent display of forsaken power on the HUD and selected power in Active Effects gui panel.
        [HarmonyPatch(typeof(Player), "GetGuardianPowerHUD")]
        internal class Patch_Player_GetGuardianPowerHUD
        {
            static bool Prefix(ref StatusEffect se, ref float cooldown)
            {
                se = null;
                cooldown = 0f;
                return false;
            }
        }

        // Reapply applicable forsaken powers on load and respawn
        [HarmonyPatch(typeof(Player), "OnSpawned")]
        internal class Patch_Player_OnSpawned
        {
            private static Dictionary<string, bool> powers = new Dictionary<string, bool>()
            {
                { "GP_Eikthyr", false },
                { "GP_TheElder", false },
                { "GP_Bonemass", false },
                { "GP_Moder", false },
                { "GP_Yagluth", false },
            };

            private static Dictionary<string, string> trophyPowers = new Dictionary<string, string>()
            {
                { "TrophyEikthyr", "GP_Eikthyr" },
                { "TrophyTheElder", "GP_TheElder" },
                { "TrophyBonemass", "GP_Bonemass" },
                { "TrophyDragonQueen", "GP_Moder" },
                { "TrophyGoblinKing", "GP_Yagluth" },
            };

            static void Prefix()
            {
                if (!allPowers.Value)
                {
                    List<string> trophies = Player.m_localPlayer.GetTrophies();
                    foreach (string trophy in trophies)
                        if (trophyPowers.ContainsKey(trophy))
                            powers[trophyPowers[trophy]] = true;

                    // Only check ItemStands if not all powers already flagged true.
                    // Unfortunately can only check ItemStands that have been loaded,
                    // so won't pick anything up if the Player is not close to the spawn point.
                    if (powers.ContainsValue(false))
                    {
                        ItemStand[] itemStands = FindObjectsOfType<ItemStand>();
                        foreach (ItemStand itemStand in itemStands)
                            if (itemStand.m_guardianPower != null && powers.ContainsKey(itemStand.m_guardianPower.name))
                                powers[itemStand.m_guardianPower.name] = true;
                    }
                }
                foreach (var power in powers)
                {
                    if (power.Value || allPowers.Value)
                    {
                        Player.m_localPlayer.GetSEMan().AddStatusEffect(power.Key, true);
                        Debug.Log($"[{PluginName}] Applying {power.Key} forsaken power.");
                    }
                }
            }
        }
    }
}
