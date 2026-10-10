"""Convert the autopilot's raw RGB screenshots (name_WxH.rgb) to PNGs."""
import re
import sys
from pathlib import Path

import numpy as np
from PIL import Image

src = Path(sys.argv[1]) if len(sys.argv) > 1 else Path.home() / \
    ".local/share/Steam/steamapps/common/Haiku the Robot/BepInEx/plugins/HollowTheRobot/shots"
dst = Path(sys.argv[2]) if len(sys.argv) > 2 else src
dst.mkdir(parents=True, exist_ok=True)
for f in sorted(src.glob("*.rgb")):
    m = re.match(r"(.*)_(\d+)x(\d+)\.rgb$", f.name)
    w, h = int(m[2]), int(m[3])
    a = np.frombuffer(f.read_bytes(), np.uint8).reshape(h, w, 3)[::-1]
    Image.fromarray(a).save(dst / f"{m[1]}.png")
    print(dst / f"{m[1]}.png")
