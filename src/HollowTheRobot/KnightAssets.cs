using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HollowTheRobot
{
    [Serializable]
    public class SpriteDef
    {
        public string name;
        public int x, y, w, h;
        public float px, py;
    }

    [Serializable]
    public class ClipEvent
    {
        public int frame;
        public string info;
    }

    [Serializable]
    public class ClipDef
    {
        public string name;
        public float fps;
        public int loopStart;
        public string wrap;
        public int[] frames;
        public ClipEvent[] events;
    }

    [Serializable]
    public class KnightMeta
    {
        public float ppu;
        public float scale;
        public int atlasWidth, atlasHeight;
        public SpriteDef[] sprites;
        public ClipDef[] clips;
    }

    /// <summary>The Knight's art, extracted from the player's own Hollow Knight install by tools/extract_knight.py.</summary>
    public static class KnightAssets
    {
        public static bool Loaded { get; private set; }
        public static Sprite[] Sprites;
        public static KnightMeta Meta;
        public static readonly Dictionary<string, ClipDef> Clips = new Dictionary<string, ClipDef>();

        /// <summary>Hollow Knight world units -> Haiku world units.</summary>
        public static float HkUnit => Meta == null ? 0.76f : 64f * Meta.scale / Meta.ppu;

        public static bool Load(string dir)
        {
            string json = Path.Combine(dir, "knight.json");
            string png = Path.Combine(dir, "knight_atlas.png");
            if (!File.Exists(json) || !File.Exists(png))
            {
                Plugin.Log.LogError($"Knight assets missing in {dir}. Run: uv run tools/extract_knight.py");
                return false;
            }
            Meta = JsonUtility.FromJson<KnightMeta>(File.ReadAllText(json));
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(png));
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.name = "KnightAtlas";
            UnityEngine.Object.DontDestroyOnLoad(tex);

            Sprites = new Sprite[Meta.sprites.Length];
            for (int i = 0; i < Sprites.Length; i++)
            {
                var d = Meta.sprites[i];
                var pivot = new Vector2(d.w > 0 ? d.px / d.w : 0.5f, d.h > 0 ? d.py / d.h : 0.5f);
                var s = Sprite.Create(tex, new Rect(d.x, d.y, d.w, d.h), pivot, Meta.ppu, 0, SpriteMeshType.FullRect);
                s.name = d.name;
                UnityEngine.Object.DontDestroyOnLoad(s);
                Sprites[i] = s;
            }
            Clips.Clear();
            foreach (var c in Meta.clips) Clips[c.name] = c;
            Loaded = true;
            Plugin.Log.LogInfo($"Loaded {Sprites.Length} Knight sprites and {Clips.Count} clips");
            return true;
        }

        public static ClipDef Clip(string name)
        {
            if (name != null && Clips.TryGetValue(name, out var c)) return c;
            return null;
        }

        /// <summary>Distance from the Knight's pivot down to his feet, in Haiku units.</summary>
        public static float FootOffset()
        {
            var idle = Clip("Idle");
            if (idle == null || idle.frames.Length == 0) return 0f;
            return Meta.sprites[idle.frames[0]].py / Meta.ppu;
        }
    }
}
