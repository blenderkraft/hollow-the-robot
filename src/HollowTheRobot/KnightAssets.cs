using System;
using System.Collections.Generic;
using System.Globalization;
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
            string meta = Path.Combine(dir, "knight.txt");
            string raw = Path.Combine(dir, "knight_atlas.rgba");
            if (!File.Exists(meta) || !File.Exists(raw))
            {
                Plugin.Log.LogError($"Knight assets missing in {dir}. Run: uv run tools/extract_knight.py");
                return false;
            }
            Meta = Parse(File.ReadAllLines(meta));
            // Raw RGBA32 rows, bottom-up: Haiku's build has no PNG decoder (ImageConversion is stripped).
            var tex = new Texture2D(Meta.atlasWidth, Meta.atlasHeight, TextureFormat.RGBA32, false);
            tex.LoadRawTextureData(File.ReadAllBytes(raw));
            tex.Apply(false, true);
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

        /// <summary>Reads knight.txt (Haiku's build strips JsonUtility, so no JSON here).</summary>
        private static KnightMeta Parse(string[] lines)
        {
            var inv = CultureInfo.InvariantCulture;
            var m = new KnightMeta();
            var sprites = new List<SpriteDef>();
            var clips = new List<ClipDef>();
            foreach (var line in lines)
            {
                if (line.Length < 2) continue;
                var f = line.Split('\t');
                switch (f[0])
                {
                    case "M":
                        m.ppu = float.Parse(f[1], inv);
                        m.scale = float.Parse(f[2], inv);
                        m.atlasWidth = int.Parse(f[3], inv);
                        m.atlasHeight = int.Parse(f[4], inv);
                        break;
                    case "S":
                        sprites.Add(new SpriteDef
                        {
                            name = f[1], x = int.Parse(f[2], inv), y = int.Parse(f[3], inv),
                            w = int.Parse(f[4], inv), h = int.Parse(f[5], inv),
                            px = float.Parse(f[6], inv), py = float.Parse(f[7], inv),
                        });
                        break;
                    case "C":
                        var events = new List<ClipEvent>();
                        if (f.Length > 6 && f[6].Length > 0)
                            foreach (var e in f[6].Split(';'))
                            {
                                int colon = e.IndexOf(':');
                                events.Add(new ClipEvent { frame = int.Parse(e.Substring(0, colon), inv), info = e.Substring(colon + 1) });
                            }
                        var frames = f[5].Length == 0 ? new int[0] : Array.ConvertAll(f[5].Split(','), x => int.Parse(x, inv));
                        clips.Add(new ClipDef
                        {
                            name = f[1], fps = float.Parse(f[2], inv), loopStart = int.Parse(f[3], inv),
                            wrap = f[4], frames = frames, events = events.ToArray(),
                        });
                        break;
                }
            }
            m.sprites = sprites.ToArray();
            m.clips = clips.ToArray();
            return m;
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
