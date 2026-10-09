using System.Collections.Generic;
using UnityEngine;

namespace HollowTheRobot
{
    /// <summary>Deals damage to Haiku enemies the same way Haiku's own sword does (IDamageable.TakeDamage).</summary>
    public static class Combat
    {
        public static LayerMask EnemyLayers
        {
            get
            {
                var anim = PlayerScript.instance != null ? PlayerScript.instance.GetComponentInChildren<AnimationScript>() : null;
                return anim != null && anim.attack != null ? anim.attack.enemyLayers : (LayerMask)~0;
            }
        }

        public static int SwordDamage => Mathf.Max(1, GameManager.instance != null ? GameManager.instance.attackDamage : 1);

        public static int Scaled(float mult) => Mathf.Max(1, Mathf.RoundToInt(SwordDamage * mult));

        /// <summary>Hit everything in a box. Returns how many targets were hit.</summary>
        public static int HitBox(Vector2 center, Vector2 size, int damage, HashSet<object> already = null)
        {
            return HitAll(Physics2D.OverlapBoxAll(center, size, 0f, EnemyLayers), center, damage, already);
        }

        public static int HitCircle(Vector2 center, float radius, int damage, HashSet<object> already = null)
        {
            return HitAll(Physics2D.OverlapCircleAll(center, radius, EnemyLayers), center, damage, already);
        }

        private static int HitAll(Collider2D[] cols, Vector2 from, int damage, HashSet<object> already)
        {
            int hits = 0;
            var seen = new HashSet<object>();
            Vector2 playerPos = PlayerScript.instance.transform.position;
            foreach (var c in cols)
            {
                if (c == null) continue;
                object target = c.GetComponent<IDamageable>();
                if (target == null) target = c.GetComponent<INonSwordDmg>();
                if (target == null || !seen.Add(target)) continue;
                if (already != null && !already.Add(target)) continue;
                int side = c.transform.position.x - playerPos.x > 0f ? 1 : -1;
                Vector2 point = c.ClosestPoint(from);
                try
                {
                    if (target is IDamageable d) d.TakeDamage(damage, side, playerPos);
                    else ((INonSwordDmg)target).TakeNonSwordDamage(damage, side, point);
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning($"Damage to {c.name} threw: {e.Message}");
                }
                Fx.Spawn(Random.value < 0.5f ? "Hit/Slash Impact 1" : "Hit/Slash Impact 2", point, side, Vector2.one * 0.8f,
                    null, 60, KnightController.SortingLayer);
                hits++;
            }
            return hits;
        }

        public static void Shake(float duration = 0.25f, float magnitude = 0.03f)
        {
            if (CameraBehavior.instance != null) CameraBehavior.instance.Shake(duration, magnitude);
        }

        public static void Sfx(string path)
        {
            if (SoundManager.instance != null && !string.IsNullOrEmpty(path)) SoundManager.instance.PlayOneShot(path);
        }
    }
}
