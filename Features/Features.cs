using EFT;                              //
using EFT.HealthSystem;                 //
using HarmonyLib;                       //
using SPT.Reflection.Patching;          //
using System;                           //
using System.Collections;               //
using System.Collections.Generic;       //
using System.Linq;                      //
using System.Reflection;                //
using RevivalMod.Constants;
using EFT.InventoryLogic;               //
using UnityEngine;                      //
using EFT.Communications;               //
using Comfort.Common;                   //
using RevivalMod.Helpers;               //

namespace RevivalMod.Features
{
    /// <summary>
    /// Enhanced revival feature with manual activation and temporary invulnerability with restrictions
    /// </summary>
    internal class RevivalFeatures : ModulePatch
    {
        // New constants for effects
        private static readonly float MOVEMENT_SPEED_MULTIPLIER = 0.1f; // 40% normal speed during invulnerability
        private static readonly bool FORCE_CROUCH_DURING_INVULNERABILITY = false; // Force player to crouch during invulnerability
        private static readonly bool DISABLE_SHOOTING_DURING_INVULNERABILITY = false; // Disable shooting during invulnerability

        // States
        private static Dictionary<string, long> _lastRevivalTimesByPlayer = new Dictionary<string, long>();
        private static Dictionary<string, bool> _playerInCriticalState = new Dictionary<string, bool>();
        private static Dictionary<string, bool> _playerIsInvulnerable = new Dictionary<string, bool>();
        private static Dictionary<string, float> _playerInvulnerabilityTimers = new Dictionary<string, float>();
        //private static Dictionary<string, float> _originalAwareness = new Dictionary<string, float>(); // Renamed from _criticalModeTags
        private static Dictionary<string, float> _originalMovementSpeed = new Dictionary<string, float>(); // Store original movement speed
        private static Dictionary<string, EFT.PlayerAnimator.EWeaponAnimationType> _originalWeaponAnimationType = new Dictionary<string, PlayerAnimator.EWeaponAnimationType>();
        private static Player PlayerClient { get; set; } = null;

        protected override MethodBase GetTargetMethod()
        {
            // We're patching the Update method of Player to constantly check for revival key press
            return AccessTools.Method(typeof(Player), nameof(Player.UpdateTick));
        }

        [PatchPostfix]
        private static void Postfix(Player __instance)
		{
			try
			{
				string profileId = __instance.ProfileId;
				Profile profile = __instance.Profile;
				if (profile != null)
				{
					InfoClass info = profile.Info;
					if (info != null)
					{
						string nickname = info.Nickname;
					}
				}
				RevivalFeatures.PlayerClient = __instance;
				if (__instance.IsYourPlayer)
				{
					bool flag;
					float num;
					if (RevivalFeatures._playerIsInvulnerable.TryGetValue(profileId, out flag) && flag && RevivalFeatures._playerInvulnerabilityTimers.TryGetValue(profileId, out num))
					{
						num -= Time.deltaTime;
						RevivalFeatures._playerInvulnerabilityTimers[profileId] = num;
						if (RevivalFeatures.FORCE_CROUCH_DURING_INVULNERABILITY && __instance.MovementContext.PoseLevel > 0f)
						{
							__instance.MovementContext.SetPoseLevel(0f, false);
						}
						if (RevivalFeatures.DISABLE_SHOOTING_DURING_INVULNERABILITY && __instance.HandsController.IsAiming)
						{
							__instance.HandsController.IsAiming = false;
						}
						if (num <= 0f)
						{
							RevivalFeatures.EndInvulnerability(__instance);
						}
					}
					bool flag2;
					if (RevivalFeatures._playerInCriticalState.TryGetValue(profileId, out flag2) && flag2 && Input.GetKeyDown(Settings.REVIVAL_KEY.Value))
					{
						RevivalFeatures.TryPerformManualRevival(__instance);
					}
					if (Input.GetKeyDown(Settings.GIVEUP_KEY.Value))
					{
						Plugin.LogSource.LogInfo("Player pressed F6 to give up in critical state.");
						RevivalFeatures.ForceKillRequested = true;
						RevivalFeatures.EndInvulnerability(__instance);
						RevivalFeatures._playerInCriticalState[profileId] = false;
						__instance.ActiveHealthController.Kill(EDamageType.Bullet);
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.LogSource.LogError("Error in RevivalFeatureExtension patch: " + ex.Message);
			}
		}

        public static bool IsPlayerInCriticalState(string playerId)
        {
            return _playerInCriticalState.TryGetValue(playerId, out bool inCritical) && inCritical;
        }

        public static void SetPlayerCriticalState(Player player, bool criticalState)
        {
            if (!(player == null))
            {
                string profileId = player.ProfileId;
                RevivalFeatures._playerInCriticalState[profileId] = criticalState;
                if (criticalState)
                {
                    RevivalFeatures._playerIsInvulnerable[profileId] = true;
                    RevivalFeatures.ApplyCriticalEffects(player);
                    RevivalFeatures.ApplyRevivableStatePlayer(player);
                    if (!player.IsYourPlayer)
                    {
                        Plugin.LogToFile("Not your player: ");
                        return;
                    }
                    try
                    {
                        NotificationManagerClass.DisplayMessageNotification(string.Concat(new string[]
                        {
                            "CRITICAL CONDITION! Press ",
                            Settings.REVIVAL_KEY.Value.ToString(),
                            " to use your defibrillator! Or press",
                            Settings.GIVEUP_KEY.Value.ToString(),
                            " to give up !"
                        }), ENotificationDurationType.Long, ENotificationIconType.Default, new Color?(Color.red));
                        return;
                    }
                    catch (Exception ex)
                    {
                        Plugin.LogSource.LogError("Error displaying critical state UI: " + ex.Message);
                        return;
                    }
                }
                if (!RevivalFeatures._playerInvulnerabilityTimers.ContainsKey(profileId))
                {
                    RevivalFeatures.RemoveStealthFromPlayer(player);
                    RevivalFeatures._playerIsInvulnerable.Remove(profileId);
                    RevivalFeatures.RestorePlayerMovement(player);
                }
            }
        }

        // Apply effects for critical state without healing
        private static void ApplyCriticalEffects(Player player)
        {
            try
            {
                string profileId = player.ProfileId;
                Profile profile = player.Profile;
                string text;
                if (profile == null)
                {
                    text = null;
                }
                else
                {
                    InfoClass info = profile.Info;
                    text = ((info != null) ? info.Nickname : null);
                }
                string text2 = text ?? "Unknown";
                if (!RevivalFeatures._originalMovementSpeed.ContainsKey(profileId))
                {
                    RevivalFeatures._originalMovementSpeed[profileId] = player.Physical.WalkSpeedLimit;
                }
                player.ActiveHealthController.DoContusion(Settings.REVIVAL_DURATION.Value, 1f);
                player.ActiveHealthController.DoStun(Settings.REVIVAL_DURATION.Value / 2f, 1f);
                player.Physical.WalkSpeedLimit = RevivalFeatures._originalMovementSpeed[profileId] * 0.02f;
                if (player.MovementContext != null)
                {
                    player.MovementContext.SetPoseLevel(0f, false);
                    player.ActiveHealthController.AddFatigue();
                    player.ActiveHealthController.SetStaminaCoeff(0f);
                }
                Plugin.LogSource.LogDebug("Applied critical effects to player " + profileId + ": " + text2);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError("Error applying critical effects: " + ex.Message);
            }
        }

        // Restore player movement after invulnerability ends
        private static void RestorePlayerMovement(Player player)
        {
            try
            {
                string profileId = player.ProfileId;
                Profile profile = player.Profile;
                string text;
                if (profile == null)
                {
                    text = null;
                }
                else
                {
                    InfoClass info = profile.Info;
                    text = ((info != null) ? info.Nickname : null);
                }
                string text2 = text ?? "Unknown";
                float num;
                if (RevivalFeatures._originalMovementSpeed.TryGetValue(profileId, out num))
                {
                    player.Physical.WalkSpeedLimit = num;
                    RevivalFeatures._originalMovementSpeed.Remove(profileId);
                }
                Plugin.LogSource.LogDebug("Restored movement for player " + profileId + ": " + text2);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError("Error restoring player movement: " + ex.Message);
            }
        }


        // Method to make player invisible to AI - improved implementation
        private static void ApplyRevivableStatePlayer(Player player)
        {
            try
            {
                string profileId = player.ProfileId;
                Profile profile = player.Profile;
                string text;
                if (profile == null)
                {
                    text = null;
                }
                else
                {
                    InfoClass info = profile.Info;
                    text = ((info != null) ? info.Nickname : null);
                }
                string text2 = text ?? "Unknown";
                player.PlayDeathSound();
                player.HandsController.IsAiming = false;
                player.MovementContext.EnableSprint(false);
                player.MovementContext.SetPoseLevel(0f, true);
                player.MovementContext.IsInPronePose = true;
                player.SetEmptyHands(null);
                player.ResetLookDirection();
                player.ActiveHealthController.IsAlive = false;
                Plugin.LogSource.LogDebug("Applied improved stealth mode to player " + profileId + ": " + text2);
                Plugin.LogSource.LogDebug(string.Format("Stealth Mode Variables, Current Awareness: {0}, IsAlive: {1}", player.Awareness, player.ActiveHealthController.IsAlive));
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError("Error applying stealth mode: " + ex.Message);
            }
        }

        // Method to remove invisibility from player
        private static void RemoveStealthFromPlayer(Player player)
        {
            try
            {
                string profileId = player.ProfileId;
                Profile profile = player.Profile;
                string text;
                if (profile == null)
                {
                    text = null;
                }
                else
                {
                    InfoClass info = profile.Info;
                    text = ((info != null) ? info.Nickname : null);
                }
                string text2 = text ?? "Unknown";
                player.IsVisible = true;
                player.ActiveHealthController.IsAlive = true;
                player.ActiveHealthController.DoContusion(25f, 0.25f);
                Plugin.LogSource.LogInfo("Removed stealth mode from player " + profileId + ": " + text2);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError("Error removing stealth mode: " + ex.Message);
            }
        }


        public static KeyValuePair<string, bool> CheckRevivalItemInRaidInventory()
        {
            Plugin.LogSource.LogDebug("Checking for revival item in inventory");

            try
            {
                if (PlayerClient == null)
                {
                    if (Singleton<GameWorld>.Instantiated)
                    {
                        PlayerClient = Singleton<GameWorld>.Instance.MainPlayer;
                        Plugin.LogSource.LogDebug($"Initialized PlayerClient: {PlayerClient != null}");
                    }
                    else
                    {
                        Plugin.LogSource.LogWarning("GameWorld not instantiated yet");
                        return new KeyValuePair<string, bool>(string.Empty, false);
                    }
                }

                if (PlayerClient == null)
                {
                    Plugin.LogSource.LogError("PlayerClient is still null after initialization attempt");
                    return new KeyValuePair<string, bool>(string.Empty, false);
                }

                string playerId = PlayerClient.ProfileId;
                var inRaidItems = PlayerClient.Inventory.GetPlayerItems(EPlayerItems.Equipment);
                bool hasItem = inRaidItems.Any(item => item.TemplateId == Constants.Constants.ITEM_ID);

                Plugin.LogSource.LogDebug($"Player {playerId} has revival item: {hasItem}");
                return new KeyValuePair<string, bool>(playerId, hasItem);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error checking revival item: {ex.Message}");
                return new KeyValuePair<string, bool>(string.Empty, false);
            }
        }


        public static bool TryPerformManualRevival(Player player)
        {
            bool flag = player == null;
            bool flag2;
            if (flag)
            {
                flag2 = false;
            }
            else
            {
                string profileId = player.ProfileId;
                bool value = RevivalFeatures.CheckRevivalItemInRaidInventory().Value;
                bool flag3 = false;
                long num;
                bool flag4 = RevivalFeatures._lastRevivalTimesByPlayer.TryGetValue(profileId, out num);
                if (flag4)
                {
                    long num2 = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    flag3 = (float)(num2 - num) < Settings.REVIVAL_COOLDOWN.Value;
                }
                bool flag5 = flag3;
                if (flag5)
                {
                    long num3 = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    int num4 = (int)(Settings.REVIVAL_COOLDOWN.Value - (float)(num3 - num));
                    NotificationManagerClass.DisplayMessageNotification(string.Format("Revival on cooldown! Available in {0} seconds", num4), ENotificationDurationType.Long, ENotificationIconType.Alert, new Color?(Color.yellow));
                    bool flag6 = !Settings.TESTING.Value;
                    if (flag6)
                    {
                        return false;
                    }
                }
                bool flag7 = value || Settings.TESTING.Value;
                if (flag7)
                {
                    bool flag8 = value && !Settings.TESTING.Value;
                    if (flag8)
                    {
                        RevivalFeatures.ConsumeDefibItem(player);
                    }
                    RevivalFeatures.ApplyRevivalEffects(player);
                    RevivalFeatures.StartInvulnerability(player);
                    player.Say(EPhraseTrigger.OnMutter, false, 2f, ETagStatus.Combat, 100, true);
                    RevivalFeatures._playerInCriticalState[profileId] = false;
                    RevivalFeatures._lastRevivalTimesByPlayer[profileId] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    NotificationManagerClass.DisplayMessageNotification("Defibrillator used successfully! You are temporarily invulnerable but limited in movement.", ENotificationDurationType.Long, ENotificationIconType.Default, new Color?(Color.green));
                    Plugin.LogSource.LogInfo("Manual revival performed for player " + profileId);
                    flag2 = true;
                }
                else
                {
                    NotificationManagerClass.DisplayMessageNotification("No defibrillator found! Unable to revive!", ENotificationDurationType.Long, ENotificationIconType.Alert, new Color?(Color.red));
                    flag2 = false;
                }
            }
            return flag2;
        }


        private static void ConsumeDefibItem(Player player)
        {
            try
            {
                if (player == null || player.InventoryController == null)
                {
                    Plugin.LogSource.LogWarning("ConsumeDefibItem: player or InventoryController is null.");
                }
                else
                {
                    Item item2 = player.Inventory.GetPlayerItems(EPlayerItems.Equipment).FirstOrDefault((Item item) => item.TemplateId == "5c052e6986f7746b207bc3c9");
                    if (item2 == null)
                    {
                        Plugin.LogSource.LogWarning("No defibrillator found.");
                    }
                    else
                    {
                        if (item2.Parent != null)
                        {
                            item2.Parent.RemoveWithoutRestrictions(item2);
                            Plugin.LogSource.LogInfo("Consumed defibrillator item: " + item2.Id);
                        }
                        else
                        {
                            Plugin.LogSource.LogWarning("Defib item has no parent container.");
                        }
                        MethodInfo methodInfo = AccessTools.Method(player.InventoryController.Inventory.GetType(), "UpdateTotalWeight", null, null);
                        if (methodInfo != null)
                        {
                            methodInfo.Invoke(player.InventoryController.Inventory, null);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ManualLogSource logSource = Plugin.LogSource;
                string text = "Error in ConsumeDefibItem: ";
                Exception ex2 = ex;
                logSource.LogError(text + ((ex2 != null) ? ex2.ToString() : null));
            }
        }


       private static void ApplyRevivalEffects(Player player)
        {
            try
            {
                ActiveHealthController activeHealthController = player.ActiveHealthController;
                bool flag = activeHealthController == null;
                if (flag)
                {
                    Plugin.LogSource.LogError("Could not get ActiveHealthController");
                }
                else
                {
                    bool flag2 = !Settings.HARDCORE_MODE.Value && Settings.RESTORE_DESTROYED_BODY_PARTS.Value;
                    if (flag2)
                    {
                        foreach (object obj in Enum.GetValues(typeof(EBodyPart)))
                        {
                            EBodyPart ebodyPart = (EBodyPart)obj;
                            Plugin.LogSource.LogDebug(string.Format("{0} is on {1} health.", ebodyPart.ToString(), activeHealthController.GetBodyPartHealth(ebodyPart, false).Current));
                            bool flag3 = activeHealthController.GetBodyPartHealth(ebodyPart, false).Current < 1f;
                            if (flag3)
                            {
                                activeHealthController.FullRestoreBodyPart(ebodyPart);
                                Plugin.LogSource.LogDebug("Restored " + ebodyPart.ToString() + ".");
                            }
                        }
                    }
                    activeHealthController.DoContusion(Settings.REVIVAL_DURATION.Value, 1f);
                    activeHealthController.DoStun(Settings.REVIVAL_DURATION.Value / 2f, 1f);
                    Plugin.LogSource.LogInfo("Applied limited revival effects to player");
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError("Error applying revival effects: " + ex.Message);
            }
        }


        private static void RemoveAllNegativeEffects(ActiveHealthController healthController)
        {
            try
            {
                MethodInfo methodInfo = AccessTools.Method(typeof(ActiveHealthController), "RemoveNegativeEffects", null, null);
                bool flag = methodInfo != null;
                if (flag)
                {
                    foreach (object obj in Enum.GetValues(typeof(EBodyPart)))
                    {
                        EBodyPart ebodyPart = (EBodyPart)obj;
                        try
                        {
                            methodInfo.Invoke(healthController, new object[] { ebodyPart });
                        }
                        catch
                        {
                        }
                    }
                    Plugin.LogSource.LogInfo("Removed all negative effects from player");
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError("Error removing effects: " + ex.Message);
            }
        }

        private static void StartInvulnerability(Player player)
        {
            if (player == null)
                return;

            string playerId = player.ProfileId;
            _playerIsInvulnerable[playerId] = true;
            _playerInvulnerabilityTimers[playerId] = Settings.REVIVAL_DURATION.Value;

            // Apply movement restrictions
            ApplyCriticalEffects(player);

            // Start coroutine for visual flashing effect
            player.StartCoroutine(FlashInvulnerabilityEffect(player));

            Plugin.LogSource.LogInfo($"Started invulnerability for player {playerId} for {Settings.REVIVAL_DURATION.Value} seconds");
        }

        private static void EndInvulnerability(Player player)
        {
            if (player == null)
                return;

            string playerId = player.ProfileId;
            _playerIsInvulnerable[playerId] = false;
            _playerInvulnerabilityTimers.Remove(playerId);

            // Remove stealth from player
            RemoveStealthFromPlayer(player);

            // Remove movement restrictions
            RestorePlayerMovement(player);

            // Show notification that invulnerability has ended
            if (player.IsYourPlayer)
            {
                NotificationManagerClass.DisplayMessageNotification(
                    "Temporary invulnerability has ended.",
                    ENotificationDurationType.Long,
                    ENotificationIconType.Default,
                    Color.white);
            }

            Plugin.LogSource.LogInfo($"Ended invulnerability for player {playerId}");
        }

        private static IEnumerator FlashInvulnerabilityEffect(Player player)
        {
            string playerId = player.ProfileId;
            float flashInterval = 0.5f; // Flash every half second
            bool isVisible = true; // Track visibility state

            // Store original visibility states of all renderers
            Dictionary<Renderer, bool> originalStates = new Dictionary<Renderer, bool>();

            // First ensure player is visible to start
            if (player.PlayerBody != null && player.PlayerBody.BodySkins != null)
            {
                foreach (var kvp in player.PlayerBody.BodySkins)
                {
                    if (kvp.Value != null)
                    {
                        var renderers = kvp.Value.GetComponentsInChildren<Renderer>(true);
                        foreach (var renderer in renderers)
                        {
                            if (renderer != null)
                            {
                                originalStates[renderer] = renderer.enabled;
                                renderer.enabled = true;
                            }
                        }
                    }
                }
            }

            // Now flash the player model
            while (_playerIsInvulnerable.TryGetValue(playerId, out bool isInvulnerable) && isInvulnerable)
            {
                try
                {
                    isVisible = !isVisible; // Toggle visibility

                    // Apply visibility to all renderers in the player model
                    if (player.PlayerBody != null && player.PlayerBody.BodySkins != null)
                    {
                        foreach (var kvp in player.PlayerBody.BodySkins)
                        {
                            if (kvp.Value != null)
                            {
                                var renderers = kvp.Value.GetComponentsInChildren<Renderer>(true);
                                foreach (var renderer in renderers)
                                {
                                    if (renderer != null)
                                    {
                                        renderer.enabled = isVisible;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogError($"Error in flash effect: {ex.Message}");
                }

                yield return new WaitForSeconds(flashInterval);
            }

            // Always ensure player is visible when effect ends by restoring original states
            try
            {
                foreach (var kvp in originalStates)
                {
                    if (kvp.Key != null)
                    {
                        kvp.Key.enabled = true; // Force visibility on exit
                    }
                }
            }
            catch
            {
                // Last resort fallback if the dictionary approach fails
                if (player.PlayerBody != null && player.PlayerBody.BodySkins != null)
                {
                    foreach (var kvp in player.PlayerBody.BodySkins)
                    {
                        if (kvp.Value != null)
                        {
                            kvp.Value.EnableRenderers(true);
                        }
                    }
                }
            }
        }

        public static bool IsPlayerInvulnerable(string playerId)
        {
            return _playerIsInvulnerable.TryGetValue(playerId, out bool invulnerable) && invulnerable;
        }
    }
}