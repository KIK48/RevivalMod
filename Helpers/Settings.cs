using BepInEx.Configuration; 
using UnityEngine;

namespace RevivalMod.Helpers
{
    internal class Settings
    {
        public static ConfigEntry<float> REVIVAL_DURATION;
        public static ConfigEntry<KeyCode> REVIVAL_KEY;
        public static ConfigEntry<KeyCode> GIVEUP_KEY;
        public static ConfigEntry<float> REVIVAL_COOLDOWN;
        public static ConfigEntry<bool> RESTORE_DESTROYED_BODY_PARTS;

        public static ConfigEntry<bool> HARDCORE_MODE; 
        public static ConfigEntry<bool> HARDCORE_HEADSHOT_DEFAULT_DEAD;
        public static ConfigEntry<float> HARDCORE_CHANCE_OF_CRITICAL_STATE;

        public static ConfigEntry<bool> TESTING;

        public static void Init(ConfigFile config)
        {
            HARDCORE_MODE = config.Bind(
                "Hardcore Mode",
                "Enable Hardcore Mode",
                false,
               "Adapt the values below the change the hardcore settings"
            );
            HARDCORE_CHANCE_OF_CRITICAL_STATE = config.Bind(
                "Hardcore Mode",
                "Chance of critical mode",
                0.75f,
               "Adapt how big the odds are to enter critical state (be revivable) in hardcore mode. 0.75 is 75%"
            );
            HARDCORE_HEADSHOT_DEFAULT_DEAD = config.Bind(
                "Hardcore Mode",
                "Headshot is always dead",
                false,
               "Headshot kills always"
            );

            REVIVAL_DURATION = config.Bind(
                "General",
                "Revival Duration",
                4f,
               "Adapt the duration of the amount of time it takes to revive."
            );
            REVIVAL_KEY = config.Bind(
                "General",
                "Revival Key",
                KeyCode.F5
            );
            Settings.GIVEUP_KEY = config.Bind<KeyCode>(
                "General",
                "Give Up Key",
                KeyCode.F6,
                "Key to press while in critical state to instantly die (give up)"
                );
            REVIVAL_COOLDOWN = config.Bind(
                "General",
                "Revival Cooldown",
                180f
              );
            RESTORE_DESTROYED_BODY_PARTS = config.Bind(
                "General",
                "Restore destroyed body parts after revive",
                false,
               "Does not work if Hardcore Mode is enabled"
            );

            TESTING = config.Bind(
                "Development",
                "Test Mode",
                false,
                new ConfigDescription("", null, new ConfigurationManagerAttributes { IsAdvanced = true })
            );
        }
    }
}
