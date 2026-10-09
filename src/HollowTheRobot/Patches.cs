using HarmonyLib;
using UnityEngine;

namespace HollowTheRobot
{
    [HarmonyPatch(typeof(PlayerScript), "Start")]
    internal static class PlayerStartPatch
    {
        private static void Postfix(PlayerScript __instance)
        {
            if (!Plugin.KnightEnabled.Value) return;
            if (__instance.GetComponent<KnightController>() == null)
                __instance.gameObject.AddComponent<KnightController>();
        }
    }

    // Velocity overrides (dive, dash slash, air dash) must win over Haiku's walk/fall-cap and BetterJumping.
    [HarmonyPatch(typeof(PlayerScript), "FixedUpdate")]
    internal static class PlayerFixedUpdatePatch
    {
        private static void Postfix() => KnightController.Instance?.ApplyVelocityOverride();
    }

    [HarmonyPatch(typeof(BetterJumping), "FixedUpdate")]
    internal static class BetterJumpingPatch
    {
        private static void Postfix() => KnightController.Instance?.ApplyVelocityOverride();
    }

    // Nail swings -> Knight slash animations.
    [HarmonyPatch(typeof(AnimationScript), nameof(AnimationScript.SetTriggerAttack))]
    internal static class SlashPatch
    {
        private static void Postfix() => KnightController.Instance?.OnSlash(0);
    }

    [HarmonyPatch(typeof(AnimationScript), nameof(AnimationScript.SetTriggerAttackDown))]
    internal static class DownSlashPatch
    {
        private static void Postfix() => KnightController.Instance?.OnSlash(-1);
    }

    [HarmonyPatch(typeof(AnimationScript), nameof(AnimationScript.SetTriggerAttackUp))]
    internal static class UpSlashPatch
    {
        private static void Postfix() => KnightController.Instance?.OnSlash(1);
    }

    // Soul: +11 for each swing that connects with something damageable (once per swing, like HK).
    [HarmonyPatch(typeof(Attack), nameof(Attack.AttackEnemy))]
    internal static class SoulGainPatch
    {
        private static readonly AccessTools.FieldRef<Attack, bool> EffectsBuffer =
            AccessTools.FieldRefAccess<Attack, bool>("effectsBuffer");
        private static readonly AccessTools.FieldRef<Attack, float> AttackRange =
            AccessTools.FieldRefAccess<Attack, float>("attackRange");

        private static void Prefix(Attack __instance)
        {
            if (KnightController.Instance == null || EffectsBuffer(__instance)) return;
            foreach (var c in Physics2D.OverlapCircleAll(__instance.transform.position, AttackRange(__instance), __instance.enemyLayers))
            {
                if (c.GetComponent<IDamageable>() != null)
                {
                    Soul.Add(Plugin.SoulPerHit.Value);
                    return;
                }
            }
        }
    }

    [HarmonyPatch(typeof(PlayerScript), nameof(PlayerScript.Jump))]
    internal static class JumpPatch
    {
        private static void Postfix(PlayerScript __instance) => KnightController.Instance?.OnJump();
    }

    [HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.TakeDamage))]
    internal static class HurtPatch
    {
        private static int before;

        private static void Prefix(PlayerHealth __instance) => before = __instance.currentHealth;

        private static void Postfix(PlayerHealth __instance)
        {
            if (__instance.currentHealth < before) KnightController.Instance?.OnHurt();
        }
    }

    [HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.AddHealth))]
    internal static class FocusGetPatch
    {
        private static void Postfix() => KnightController.Instance?.OnHealed();
    }
}
