using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Rewired;
using UnityEngine;

namespace HollowTheRobot
{
    /// <summary>
    /// Reroutes every Rewired button/axis read inside PlayerScript.Update through these wrappers, so the
    /// Knight can (a) split Heal into tap = cast / hold = focus like Hollow Knight, and (b) lock Haiku's
    /// controls while a spell or Nail Art is playing.
    /// </summary>
    public static class InputHooks
    {
        /// <summary>While true, PlayerScript sees no buttons and no movement.</summary>
        public static bool Locked;

        // Scripted input from the test autopilot: held buttons, buttons pressed this frame, axis values.
        public static readonly HashSet<string> FakeHeld = new HashSet<string>();
        public static readonly HashSet<string> FakeDown = new HashSet<string>();
        public static readonly HashSet<string> FakeUp = new HashSet<string>();
        public static readonly Dictionary<string, float> FakeAxis = new Dictionary<string, float>();

        // Raw reads (real pad/keyboard OR autopilot), used by the Knight's own abilities too.
        public static bool RawDown(Player p, string a) => FakeDown.Contains(a) || p.GetButtonDown(a);
        public static bool RawHeld(Player p, string a) => FakeHeld.Contains(a) || p.GetButton(a);
        public static bool RawUp(Player p, string a) => FakeUp.Contains(a) || p.GetButtonUp(a);
        public static float RawAxis(Player p, string a) => FakeAxis.TryGetValue(a, out var v) ? v : p.GetAxisRaw(a);
        private static float RawAxisSmooth(Player p, string a) => FakeAxis.TryGetValue(a, out var v) ? v : p.GetAxis(a);

        private static bool healPending;
        private static int castDir;
        private static float healHeld;
        private static bool virtualHealDown;
        private static bool virtualHealHeld;

        /// <summary>Called once per frame before PlayerScript.Update.</summary>
        public static void Tick(Player p)
        {
            virtualHealDown = false;
            if (!Plugin.Spells.Value || KnightController.Instance == null)
            {
                virtualHealDown = RawDown(p, "Heal");
                virtualHealHeld = RawHeld(p, "Heal");
                return;
            }
            if (RawDown(p, "Heal"))
            {
                healPending = true;
                healHeld = 0f;
                // Direction is read when Heal goes down, so a quick Up+tap still means Howling Wraiths.
                float v = RawAxis(p, "Move Vertical");
                castDir = v > 0.5f || RawHeld(p, "Attack Up") ? 1 : v < -0.5f || RawHeld(p, "Attack Down") ? -1 : 0;
            }
            if (healPending)
            {
                if (RawHeld(p, "Heal"))
                {
                    healHeld += Time.unscaledDeltaTime;
                    if (healHeld >= Plugin.CastTapTime.Value)
                    {
                        healPending = false;
                        virtualHealDown = true;
                        virtualHealHeld = true;
                    }
                }
                else
                {
                    healPending = false;
                    KnightController.Instance.TryCast(castDir);
                }
            }
            if (!RawHeld(p, "Heal")) virtualHealHeld = false;
        }

        public static bool GetButtonDown(Player p, string action)
        {
            if (action == "Heal") return !Locked && virtualHealDown;
            if (Locked && action != "Exit Game" && action != "Inventory" && action != "Quick Map") return false;
            return RawDown(p, action);
        }

        public static bool GetButton(Player p, string action)
        {
            if (action == "Heal") return !Locked && virtualHealHeld;
            if (Locked) return false;
            return RawHeld(p, action);
        }

        public static bool GetButtonUp(Player p, string action)
        {
            if (Locked) return false;
            return RawUp(p, action);
        }

        public static float GetAxis(Player p, string action)
        {
            if (Locked && action.StartsWith("Move")) return 0f;
            return RawAxisSmooth(p, action);
        }

        public static float GetAxisRaw(Player p, string action)
        {
            if (Locked && action.StartsWith("Move")) return 0f;
            return RawAxis(p, action);
        }
    }

    [HarmonyPatch(typeof(PlayerScript), "Update")]
    internal static class PlayerUpdateInputPatch
    {
        private static readonly string[] Wrapped = { "GetButtonDown", "GetButton", "GetButtonUp", "GetAxis", "GetAxisRaw" };

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replaced = 0;
            foreach (var ins in instructions)
            {
                if ((ins.opcode == OpCodes.Callvirt || ins.opcode == OpCodes.Call) && ins.operand is MethodInfo mi
                    && mi.DeclaringType == typeof(Player) && System.Array.IndexOf(Wrapped, mi.Name) >= 0)
                {
                    var ps = mi.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
                    {
                        ins.opcode = OpCodes.Call;
                        ins.operand = AccessTools.Method(typeof(InputHooks), mi.Name);
                        replaced++;
                    }
                }
                yield return ins;
            }
            Plugin.Log.LogInfo($"PlayerScript.Update: rerouted {replaced} input reads");
        }

        private static void Prefix(PlayerScript __instance)
        {
            InputHooks.Tick(ReInput.players.GetPlayer(0));
        }
    }
}
