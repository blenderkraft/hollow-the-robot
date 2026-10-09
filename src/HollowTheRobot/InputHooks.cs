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

        private static bool healPending;
        private static float healHeld;
        private static bool virtualHealDown;
        private static bool virtualHealHeld;

        /// <summary>Called once per frame before PlayerScript.Update.</summary>
        public static void Tick(Player p)
        {
            virtualHealDown = false;
            if (!Plugin.Spells.Value || KnightController.Instance == null)
            {
                virtualHealDown = p.GetButtonDown("Heal");
                virtualHealHeld = p.GetButton("Heal");
                return;
            }
            if (p.GetButtonDown("Heal"))
            {
                healPending = true;
                healHeld = 0f;
            }
            if (healPending)
            {
                if (p.GetButton("Heal"))
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
                    KnightController.Instance.TryCast();
                }
            }
            if (!p.GetButton("Heal")) virtualHealHeld = false;
        }

        public static bool GetButtonDown(Player p, string action)
        {
            if (action == "Heal") return !Locked && virtualHealDown;
            if (Locked && action != "Exit Game" && action != "Inventory" && action != "Quick Map") return false;
            return p.GetButtonDown(action);
        }

        public static bool GetButton(Player p, string action)
        {
            if (action == "Heal") return !Locked && virtualHealHeld;
            if (Locked) return false;
            return p.GetButton(action);
        }

        public static bool GetButtonUp(Player p, string action)
        {
            if (Locked) return false;
            return p.GetButtonUp(action);
        }

        public static float GetAxis(Player p, string action)
        {
            if (Locked && action.StartsWith("Move")) return 0f;
            return p.GetAxis(action);
        }

        public static float GetAxisRaw(Player p, string action)
        {
            if (Locked && action.StartsWith("Move")) return 0f;
            return p.GetAxisRaw(action);
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
