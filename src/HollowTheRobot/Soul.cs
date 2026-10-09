using UnityEngine;

namespace HollowTheRobot
{
    /// <summary>Hollow Knight's Soul: earned by striking enemies with the nail, spent on spells.</summary>
    public static class Soul
    {
        public const int Max = 99;
        public static int Current;
        public static float FlashTimer;   // "not enough soul" flash
        public static float GainTimer;    // pulse on gain

        public static void Add(int amount)
        {
            int before = Current;
            Current = Mathf.Clamp(Current + amount, 0, Max);
            if (Current > before) GainTimer = 0.2f;
        }

        public static bool Spend(int amount)
        {
            if (Current < amount)
            {
                FlashTimer = 0.35f;
                return false;
            }
            Current -= amount;
            return true;
        }
    }

    /// <summary>Draws a Hollow Knight style Soul vessel under Haiku's HUD.</summary>
    public class SoulHud : MonoBehaviour
    {
        private Texture2D ring, disc;

        private void Awake()
        {
            ring = MakeCircle(64, 0.80f, 1.0f);
            disc = MakeCircle(64, 0f, 0.78f);
        }

        private static Texture2D MakeCircle(int size, float inner, float outer)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            float r = size / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                t.SetPixel(x, y, d >= inner && d <= outer ? Color.white : Color.clear);
            }
            t.Apply();
            return t;
        }

        private void Update()
        {
            if (Soul.FlashTimer > 0f) Soul.FlashTimer -= Time.unscaledDeltaTime;
            if (Soul.GainTimer > 0f) Soul.GainTimer -= Time.unscaledDeltaTime;
        }

        private void OnGUI()
        {
            if (!Plugin.ShowSoulHud.Value || PlayerScript.instance == null || CameraBehavior.instance == null) return;
            if (Time.timeScale == 0f) return; // hide in pause menu
            float u = Screen.height / 216f; // Haiku's pixel scale
            float size = 22f * u * (1f + Soul.GainTimer);
            var rect = new Rect(8f * u, 22f * u, size, size);

            // empty vessel
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(rect, disc);
            // soul fill from the bottom
            float fill = Soul.Current / (float)Soul.Max;
            if (fill > 0f)
            {
                GUI.color = Soul.Current >= Plugin.SpellCost.Value ? new Color(1f, 1f, 1f, 0.95f) : new Color(0.75f, 0.78f, 0.85f, 0.8f);
                var clip = new Rect(rect.x, rect.y + rect.height * (1f - fill), rect.width, rect.height * fill);
                GUI.BeginGroup(clip);
                GUI.DrawTexture(new Rect(0f, -rect.height * (1f - fill), rect.width, rect.height), disc);
                GUI.EndGroup();
            }
            GUI.color = Soul.FlashTimer > 0f && Mathf.Repeat(Soul.FlashTimer, 0.1f) > 0.05f ? new Color(1f, 0.3f, 0.3f) : Color.white;
            GUI.DrawTexture(rect, ring);
            GUI.color = Color.white;
        }
    }
}
