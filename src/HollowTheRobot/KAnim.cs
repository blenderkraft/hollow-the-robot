using System;
using UnityEngine;

namespace HollowTheRobot
{
    /// <summary>A tiny tk2dSpriteAnimator clone that plays extracted Hollow Knight clips on a SpriteRenderer.</summary>
    public class KAnim : MonoBehaviour
    {
        public SpriteRenderer sr;
        public ClipDef clip;
        public int frame;
        public bool finished;
        public float speed = 1f;
        public bool unscaledTime;
        public bool destroyWhenDone;
        public Action<string> onEvent;

        private float timer;

        public string Current => clip?.name;

        private void Awake()
        {
            if (sr == null) sr = GetComponent<SpriteRenderer>();
        }

        public bool Play(string name, bool restart = false)
        {
            var c = KnightAssets.Clip(name);
            if (c == null) return false;
            if (!restart && clip == c && !finished) return true;
            if (!restart && clip == c && finished && c.wrap != "once") return true;
            clip = c;
            frame = 0;
            timer = 0f;
            finished = false;
            Show();
            Fire();
            return true;
        }

        public float Duration => clip == null || clip.fps <= 0 ? 0f : clip.frames.Length / clip.fps;

        private void Update()
        {
            if (clip == null || finished || clip.frames.Length == 0) return;
            if (clip.wrap == "single") return;
            timer += (unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime) * speed;
            float step = 1f / Mathf.Max(1f, clip.fps);
            while (timer >= step && !finished)
            {
                timer -= step;
                Advance();
            }
        }

        private void Advance()
        {
            int n = clip.frames.Length;
            switch (clip.wrap)
            {
                case "once":
                    if (frame >= n - 1)
                    {
                        finished = true;
                        if (destroyWhenDone) Destroy(gameObject);
                        return;
                    }
                    frame++;
                    break;
                case "loopsection":
                    frame++;
                    if (frame >= n) frame = Mathf.Clamp(clip.loopStart, 0, n - 1);
                    break;
                case "randomframe":
                case "randomloop":
                    frame = UnityEngine.Random.Range(0, n);
                    break;
                default:
                    frame = (frame + 1) % n;
                    break;
            }
            Show();
            Fire();
        }

        private void Show()
        {
            if (sr != null && clip != null && clip.frames.Length > 0)
                sr.sprite = KnightAssets.Sprites[clip.frames[Mathf.Clamp(frame, 0, clip.frames.Length - 1)]];
        }

        private void Fire()
        {
            if (onEvent == null || clip.events == null) return;
            foreach (var e in clip.events)
                if (e.frame == frame) onEvent(e.info);
        }
    }

    public static class Fx
    {
        /// <summary>Spawn a one-shot effect. facing: +1 = HK default (left), -1 = mirrored.</summary>
        public static KAnim Spawn(string clip, Vector3 pos, float facing, Vector2 scale, Transform parent = null,
            int sortingOrder = 50, int sortingLayer = 0, float speed = 1f)
        {
            if (KnightAssets.Clip(clip) == null) return null;
            var go = new GameObject("HTR Fx " + clip);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = new Vector3(scale.x * facing, scale.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingLayerID = sortingLayer;
            sr.sortingOrder = sortingOrder;
            var a = go.AddComponent<KAnim>();
            a.sr = sr;
            a.speed = speed;
            a.destroyWhenDone = true;
            a.Play(clip, true);
            if (a.clip.wrap != "once") UnityEngine.Object.Destroy(go, Mathf.Max(0.2f, a.Duration));
            return a;
        }
    }
}
