using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RevivalMod.Constants;
using RevivalMod.Features;
using EFT.InventoryLogic;
using RevivalMod.Helpers;

namespace RevivalMod.Patches
{
    internal class DeathPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.Kill));
        }

        [PatchPrefix]
        static bool Prefix(ActiveHealthController __instance, EDamageType damageType)
        {
            try
            {

                // Get the Player field
                FieldInfo playerField = AccessTools.Field(typeof(ActiveHealthController), "Player");
                if (playerField == null) return true;

                Player player = playerField.GetValue(__instance) as Player;
                if (player == null) return true;

                if (!player.IsYourPlayer || player.IsAI) return true;

                if (RevivalFeatures.ForceKillRequested)
                {
                    RevivalFeatures.ForceKillRequested = false;
                    //Plugin.LogSource.LogInfo("ForceKillRequested: letting player die normally.");
                    return true;
                }

                string playerId = player.ProfileId;
                Profile profile = player.Profile;
                string nickname;
                if (profile == null)
                {
                    nickname = null;
                }
                else
                {
                    InfoClass info = profile.Info;
                    nickname = ((info != null) ? info.Nickname : null);
                }
                nickname = nickname ?? "Unknown";
                // Check if player is invulnerable from recent revival
                if (RevivalFeatures.IsPlayerInvulnerable(playerId))
                {
                    Plugin.LogSource.LogInfo(string.Concat(new string[] { "Player: ", nickname, " (", profileId, ") is invulnerable, blocking death completely" }));
                    return false; // Block the kill completely
                }

                Plugin.LogSource.LogInfo($"DEATH PREVENTION: Player {player.ProfileId} about to die from {damageType}");

                // Check if the player has the revival item
                var inRaidItems = player.Inventory.GetPlayerItems(EPlayerItems.Equipment);
                bool hasDefib = inRaidItems.Any(item => item.TemplateId == Constants.Constants.ITEM_ID);

                Plugin.LogSource.LogInfo($"DEATH PREVENTION: Player has defibrillator: {hasDefib || Settings.TESTING.Value}");

                Plugin.LogSource.LogInfo("DEATH PREVENTION: Setting player to critical state instead of death");
                if (Settings.HARDCORE_MODE.Value)
                {
                    if (Settings.HARDCORE_HEADSHOT_DEFAULT_DEAD.Value && __instance.GetBodyPartHealth(EBodyPart.Head, true).Current < 1) {
                        Plugin.LogSource.LogInfo($"DEATH NOT PREVENTED: Player headshotted");
                        return true;
                    }


                    var _randomNumber = new Random().Range(0, 100)/100;
                    if (Settings.HARDCORE_CHANCE_OF_CRITICAL_STATE.Value < _randomNumber)
                    {
                            Plugin.LogSource.LogInfo($"DEATH NOT PREVENTED: Player was unlucky. Random Number was: {_randomNumber}");
                            return true;
                    }
                }
                    // Set the player in critical state for the revival system
                RevivalFeatures.SetPlayerCriticalState(player, true);

                    // Block the kill completely
                return false;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error in Death prevention patch: {ex.Message}");
            }

            return true;
        }
    }
}