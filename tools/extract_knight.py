"""Extract the Knight's sprites and animations from YOUR Hollow Knight install.

Hollow the Robot ships no Hollow Knight art. This script reads the tk2d sprite
collections and animation libraries out of your own copy of Hollow Knight,
downsamples them to Haiku's pixel scale and writes an atlas + JSON the BepInEx
plugin loads at runtime.

    uv run tools/extract_knight.py [--hk <Hollow Knight dir>] [--out <dir>]

By default it writes straight into Haiku's BepInEx/plugins/HollowTheRobot/knight.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

import numpy as np
import UnityPy
from PIL import Image
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

STEAM = Path.home() / ".local/share/Steam/steamapps/common"
DEFAULT_HK = STEAM / "Hollow Knight"
DEFAULT_OUT = STEAM / "Haiku the Robot/BepInEx/plugins/HollowTheRobot/knight"

HK_PPU = 64.0  # tk2d texelSize is 1/64 for every collection we use

# Animation libraries (by spriteCollection/anim name) and the clips we want from each.
# None = take every clip in that library.
WANTED = {
    "Knight": [
        "Idle", "Idle Hurt", "Run", "Walk", "Sprint", "Turn", "Run To Idle",
        "Airborne", "Fall", "Land", "HardLand", "Super Hard Land",
        "Double Jump", "Double Jump Wings 2",
        "Wall Slide", "Walljump", "Walljump Puff", "Wall Slash",
        "Dash", "Dash Down", "Dash To Idle", "Dash Effect",
        "Shadow Dash", "Shadow Dash Sharp", "Shadow Dash Burst", "Shadow Recharge",
        "Slash", "SlashAlt", "UpSlash", "DownSlash",
        "SlashEffect", "SlashEffectAlt", "UpSlashEffect", "DownSlashEffect", "Wall Slash",
        "LookUp", "LookDown", "LookUpEnd", "LookDownEnd",
        "Focus", "Focus Get", "Focus End", "Focus Get Once",
        "Fireball Antic", "Fireball1 Cast", "Fireball2 Cast",
        "Quake Antic", "Quake Fall", "Quake Fall 2", "Quake Land", "Quake Land 2",
        "Scream Start", "Scream", "Scream End", "Scream 2", "Scream End 2",
        "Recoil", "Stun", "Death", "Spike Death", "Hazard Respawn", "Respawn Wake",
        "NA Charge", "NA Charged Effect", "NA Big Slash", "NA Big Slash Effect",
        "NA Cyclone Start", "NA Cyclone", "NA Cyclone End", "Cyclone Effect", "Cyclone Effect End",
        "NA Dash Slash", "NA Dash Slash Effect",
        "SD Charge Ground", "SD Charge Ground End", "SD Dash", "SD Air Brake", "SD Wall Charge",
        "SD Hit Wall", "SD Fx Charge", "SD Fx Burst", "SD Fx Bling", "SD Trail", "SD Trail End",
        "Sit", "Sit Idle", "Get Off", "Collect Normal 1", "Collect Normal 2", "Collect Normal 3",
        "Prostrate", "Prostrate Rise", "Wake Up Ground", "Enter", "Exit",
    ],
    # Spell effects library (fireball / dive / wraiths)
    "Spell": [
        "Blast", "Ball", "Ball End", "Impact", "Wisp", "Fireball Wall Impact",
        "Ball Lv2", "Blast Lv2", "Quake Blast", "Quake Charge", "Quake Dash",
        "Quake 1 Pillar", "Quake 2 Blast", "Quake 2 Pillar", "Quake 2 Trail",
        "Scream1", "Scream1 Base", "Scream2", "Scream2 Base", "Sharp Flash",
    ],
    "Focus": None,   # Plink, Focus Effect, Burst Effect ...
    "Dust": None,    # Dash Puff, Jump Puff, Wall Puff
    "Hit": ["Slash Impact 1", "Slash Impact 2", "Impact Lines", "Nail Clash"],
    "Soul": None,    # HUD soul orb fills
}

# Which tk2dSpriteAnimation asset is which: identified by the collections it references
# plus a clip it must contain, so it survives path-id changes between game patches.
LIBRARY_SIGNATURE = {
    "Knight": "Wall Slide",
    "Spell": "Quake 2 Pillar",
    "Focus": "Focus Effect",
    "Dust": "Dash Puff",
    "Hit": "Slash Impact 1",
    "Soul": "Drain",
}

WRAP = ["loop", "loopsection", "once", "pingpong", "randomframe", "randomloop", "single"]


def log(*a):
    print("[extract]", *a, flush=True)


def load_trees(hk: Path):
    data = hk / "hollow_knight_Data"
    if not (data / "resources.assets").exists():
        sys.exit(f"Hollow Knight data not found at {data}")
    version = UnityPy.load(str(data / "globalgamemanagers")).objects[0].assets_file.unity_version
    log("Hollow Knight Unity version", version)
    gen = TypeTreeGenerator(version)
    gen.load_local_dll_folder(str(data / "Managed"))
    gg = UnityPy.load(str(data / "globalgamemanagers.assets"))
    scripts = {o.path_id: o.read().m_ClassName for o in gg.objects if o.type.name == "MonoScript"}
    env = UnityPy.load(str(data / "resources.assets"))
    tc = gen.get_nodes_up("TeamCherry.TK2D", "tk2dSpriteCollectionData")
    ta = gen.get_nodes_up("TeamCherry.TK2D", "tk2dSpriteAnimation")
    objs = {o.path_id: o for o in env.objects}
    cols, libs = {}, []
    for o in env.objects:
        if o.type.name != "MonoBehaviour":
            continue
        mb = o.read(check_read=False)
        cls = scripts.get(mb.m_Script.m_PathID)
        if cls == "tk2dSpriteCollectionData":
            cols[o.path_id] = o.read_typetree(tc)
        elif cls == "tk2dSpriteAnimation":
            libs.append(o.read_typetree(ta))
    return objs, cols, libs


class AtlasCache:
    """Decoded collection textures, flipped so row 0 is the bottom (Unity UV space)."""

    def __init__(self, objs):
        self.objs = objs
        self.cache = {}

    def get(self, col):
        tex_ptr = col["textures"][0]
        pid = tex_ptr["m_PathID"]
        if pid not in self.cache:
            img = self.objs[pid].read().image.convert("RGBA")
            self.cache[pid] = np.asarray(img)[::-1]  # bottom-up
        return self.cache[pid]


def cut_sprite(atlas: np.ndarray, sd: dict) -> tuple[Image.Image, float, float]:
    """Return (image, pivot_x_px, pivot_y_px) at HK resolution, un-rotated, pivot from bottom-left."""
    h, w = atlas.shape[:2]
    pos = sd["positions"]
    uv = sd["uvs"]
    minx, miny = pos[0]["x"], pos[0]["y"]
    maxx, maxy = pos[3]["x"], pos[3]["y"]
    us = [p["x"] * w for p in uv]
    vs = [p["y"] * h for p in uv]
    u0, u1 = int(round(min(us))), int(round(max(us)))
    v0, v1 = int(round(min(vs))), int(round(max(vs)))
    region = atlas[v0:v1, u0:u1]  # [v][u], bottom-up
    if sd["flipped"]:
        # positions x -> v axis, positions y -> u axis
        region = region.transpose(1, 0, 2)
        # uv1 belongs to pos1 (max x); if its v is not the max, the axis is reversed
        if uv[1]["y"] < uv[0]["y"]:
            region = region[:, ::-1]
        if uv[2]["x"] < uv[0]["x"]:
            region = region[::-1, :]
    else:
        if uv[1]["x"] < uv[0]["x"]:
            region = region[:, ::-1]
        if uv[2]["y"] < uv[0]["y"]:
            region = region[::-1, :]
    if region.shape[0] == 0 or region.shape[1] == 0:  # tk2d's blank placeholder frames
        return Image.new("RGBA", (1, 1)), 0.0, 0.0
    img = Image.fromarray(np.ascontiguousarray(region[::-1]), "RGBA")  # back to top-down
    return img, -minx * HK_PPU, -miny * HK_PPU


def downsample(img: Image.Image, px: float, py: float, scale: float, hard: bool):
    """Shrink to Haiku's pixel density. Pivot stays at the same spot."""
    # Pad so the pivot lands on an exact pixel boundary after scaling.
    W = max(1, int(round(img.width * scale)))
    H = max(1, int(round(img.height * scale)))
    small = img.convert("RGBa").resize((W, H), Image.BOX).convert("RGBA")
    a = np.asarray(small).copy()
    alpha = a[..., 3].astype(np.float32) / 255.0
    if hard:
        mask = alpha >= 0.45
        a[..., 3] = np.where(mask, 255, 0)
    else:
        q = np.round(alpha * 4) / 4  # 4 alpha steps keeps glows readable but crisp
        a[..., 3] = (q * 255).astype(np.uint8)
    a[a[..., 3] == 0, :3] = 0
    return Image.fromarray(a, "RGBA"), px * W / img.width, py * H / img.height


def pack(images, max_w=2048, pad=2):
    """Simple shelf packer. Returns (atlas, [(x, y_from_bottom, w, h)])."""
    order = sorted(range(len(images)), key=lambda i: -images[i].height)
    rects = [None] * len(images)
    x = y = shelf = 0
    for i in order:
        im = images[i]
        if x + im.width + pad > max_w:
            x, y, shelf = 0, y + shelf + pad, 0
        rects[i] = (x, y, im.width, im.height)
        x += im.width + pad
        shelf = max(shelf, im.height)
    total_h = y + shelf
    H = 1
    while H < total_h:
        H *= 2
    atlas = Image.new("RGBA", (max_w, H), (0, 0, 0, 0))
    out = []
    for i, im in enumerate(images):
        rx, ry, rw, rh = rects[i]
        atlas.paste(im, (rx, ry))  # top-down placement
        out.append((rx, H - ry - rh, rw, rh))  # convert to bottom-up for Unity
    return atlas, out


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--hk", type=Path, default=DEFAULT_HK, help="Hollow Knight install folder")
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT, help="output folder")
    ap.add_argument("--scale", type=float, default=0.19,
                    help="HK pixels -> Haiku pixels (0.19 makes the Knight ~1.5x Haiku's height)")
    ap.add_argument("--ppu", type=float, default=16.0, help="Haiku pixels per unit")
    ap.add_argument("--preview", action="store_true", help="also write a contact sheet")
    args = ap.parse_args()

    objs, cols, libs = load_trees(args.hk)
    atlases = AtlasCache(objs)

    picked = {}
    for key, sig in LIBRARY_SIGNATURE.items():
        lib = next((l for l in libs if any(c["name"] == sig for c in l["clips"])), None)
        if lib is None:
            sys.exit(f"Could not find the '{key}' animation library (no clip named '{sig}')")
        picked[key] = lib

    sprite_index = {}   # (collection pid, sprite id) -> output index
    images, pivots, names = [], [], []
    clips_out = []

    for key, lib in picked.items():
        wanted = WANTED[key]
        by_name = {}
        for c in lib["clips"]:
            if c["name"] and c["name"] not in by_name:
                by_name[c["name"]] = c
        names_to_take = wanted if wanted is not None else list(by_name)
        for cname in names_to_take:
            clip = by_name.get(cname)
            if clip is None:
                log(f"warning: clip '{cname}' missing from {key}")
                continue
            hard = key == "Knight" and "Effect" not in cname and "Fx" not in cname \
                and "Puff" not in cname and "Wings" not in cname and "Trail" not in cname \
                and "Burst" not in cname and "Sharp" not in cname
            frames, events = [], []
            for fi, f in enumerate(clip["frames"]):
                ref = f["spriteCollection"]
                if ref["m_FileID"] != 0:
                    log(f"warning: {cname} frame {fi} references another file; skipped")
                    continue
                col = cols[ref["m_PathID"]]
                k = (ref["m_PathID"], f["spriteId"], hard)
                if k not in sprite_index:
                    sd = col["spriteDefinitions"][f["spriteId"]]
                    img, px, py = cut_sprite(atlases.get(col), sd)
                    small, spx, spy = downsample(img, px, py, args.scale, hard)
                    sprite_index[k] = len(images)
                    images.append(small)
                    pivots.append((spx, spy))
                    names.append(sd["name"])
                frames.append(sprite_index[k])
                if f["triggerEvent"]:
                    events.append({"frame": len(frames) - 1, "info": f["eventInfo"]})
            out_name = cname if key == "Knight" else f"{key}/{cname}"
            clips_out.append({
                "name": out_name, "fps": clip["fps"], "loopStart": clip["loopStart"],
                "wrap": WRAP[clip["wrapMode"]] if clip["wrapMode"] < len(WRAP) else "loop",
                "frames": frames, "events": events,
            })
        log(f"{key}: {len(names_to_take)} clips")

    atlas, rects = pack(images)
    args.out.mkdir(parents=True, exist_ok=True)
    atlas.save(args.out / "knight_atlas.png", optimize=True)
    meta = {
        "source": "Extracted from the user's own Hollow Knight install by tools/extract_knight.py",
        "ppu": args.ppu, "scale": args.scale,
        "atlasWidth": atlas.width, "atlasHeight": atlas.height,
        "sprites": [
            {"name": n, "x": r[0], "y": r[1], "w": r[2], "h": r[3], "px": p[0], "py": p[1]}
            for n, r, p in zip(names, rects, pivots)
        ],
        "clips": clips_out,
    }
    (args.out / "knight.json").write_text(json.dumps(meta, separators=(",", ":")))
    log(f"{len(images)} sprites, {len(clips_out)} clips -> {args.out} (atlas {atlas.width}x{atlas.height})")

    if args.preview:
        prev = atlas.copy()
        bg = Image.new("RGBA", prev.size, (90, 90, 110, 255))
        bg.alpha_composite(prev)
        bg.save(args.out / "preview.png")


if __name__ == "__main__":
    main()
