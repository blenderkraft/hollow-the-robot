using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowTheRobot
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dev.hollowtherobot";
        public const string Name = "Hollow the Robot";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        public static ConfigEntry<bool> KnightEnabled;
        public static ConfigEntry<bool> AirDash;
        public static ConfigEntry<bool> Spells;
        public static ConfigEntry<bool> NailArts;
        public static ConfigEntry<float> CastTapTime;
        public static ConfigEntry<float> NailArtChargeTime;
        public static ConfigEntry<int> SoulPerHit;
        public static ConfigEntry<int> SpellCost;
        public static ConfigEntry<float> SpellDamage;
        public static ConfigEntry<float> NailArtDamage;
        public static ConfigEntry<bool> ShowSoulHud;
        public static ConfigEntry<bool> DebugKeys;

        private void Awake()
        {
            Log = Logger;
            Instance = this;

            KnightEnabled = Config.Bind("General", "Play As Knight", true, "Replace Haiku with the Knight.");
            DebugKeys = Config.Bind("General", "Debug Keys", false, "F5 fills Soul, F6 toggles Knight/Haiku visuals.");
            AirDash = Config.Bind("Abilities", "Mothwing Cloak Air Dash", true,
                "Allow one dash in mid-air (needs Haiku's dash unlocked).");
            Spells = Config.Bind("Abilities", "Spells", true,
                "Tap Heal to cast: Vengeful Spirit, Up = Howling Wraiths, Down = Desolate Dive. Hold Heal to Focus.");
            NailArts = Config.Bind("Abilities", "Nail Arts", true,
                "Hold Attack to charge. Release = Great Slash, while dashing = Dash Slash, holding Up/Down = Cyclone Slash.");
            CastTapTime = Config.Bind("Tuning", "Cast Tap Time", 0.2f,
                new ConfigDescription("Release Heal before this many seconds to cast instead of focus.",
                    new AcceptableValueRange<float>(0.08f, 0.5f)));
            NailArtChargeTime = Config.Bind("Tuning", "Nail Art Charge Time", 1.0f,
                new ConfigDescription("Seconds to charge a Nail Art (Hollow Knight uses 1.35).",
                    new AcceptableValueRange<float>(0.4f, 2f)));
            SoulPerHit = Config.Bind("Tuning", "Soul Per Nail Hit", 11, "Hollow Knight gives 11.");
            SpellCost = Config.Bind("Tuning", "Spell Cost", 33, "Hollow Knight charges 33.");
            SpellDamage = Config.Bind("Tuning", "Spell Damage Multiplier", 3f, "Spell damage as a multiple of a sword hit.");
            NailArtDamage = Config.Bind("Tuning", "Nail Art Damage Multiplier", 2.5f, "Great/Dash Slash damage as a multiple of a sword hit.");
            ShowSoulHud = Config.Bind("HUD", "Show Soul Vessel", true, "Draw the Soul vessel under Haiku's health bar.");

            string dir = Path.Combine(Path.GetDirectoryName(Info.Location), "knight");
            if (!KnightAssets.Load(dir))
            {
                Log.LogError("Hollow the Robot is disabled until the Knight's art is extracted.");
                return;
            }

            var h = new Harmony(Guid);
            h.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} {Version} loaded. Arise, little Knight.");
        }
    }
}
