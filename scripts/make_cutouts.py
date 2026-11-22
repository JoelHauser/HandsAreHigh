"""
Cut the bots out of the screenshots for the cover (scripts/make_cover.py), once.

Needs rembg (pip install "rembg[cpu]"; it downloads its model on first use). The results are kept
in art/cutouts, so make_cover.py itself needs only numpy and pillow.

    <python with rembg> scripts/make_cutouts.py
"""
import os

import numpy as np
from PIL import Image
from rembg import new_session, remove
from scipy import ndimage

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCREENS = os.path.join(ROOT, "art", "screens")
OUT = os.path.join(ROOT, "art", "cutouts")

# name: (screenshot, crop box around the figure(s) in screenshot pixels, model). The general model
# (isnet) dropped raised arms against busy backgrounds; the human model keeps them. The gas-mask
# kneeling shot (road-kneel-gasmask.png) lost an arm with one and kept a tree with the other: unused.
HUMAN, GENERAL = "u2net_human_seg", "isnet-general-use"
CUTS = {
    "sunset":   ("sunset-hands-up.png",        (430, 560, 1370, 1301), HUMAN),
    "snow":     ("snow-hands-up.png",           (0, 0, 1436, 1107),     GENERAL),
    "factory":  ("factory-hands-up.png",        (1250, 380, 1900, 1440), HUMAN),
    "dark":     ("dark-hands-up.png",           (1000, 450, 1700, 1440), GENERAL),
    "reddoor":  ("reddoor-hands-up.png",        (1450, 450, 2000, 1440), HUMAN),
    "wall":     ("wall-behind-head.png",        (1650, 440, 2350, 1440), GENERAL),
    "captor":   ("containers-captor.png",       (900, 550, 2150, 1440),  HUMAN),
    "kneel":    ("containers-kneel-close.png",  (1250, 380, 2250, 1440), GENERAL),
}


def keep_figures(cut):
    """Only the large pieces of the mask (the figures): specks of paper or leaves dropped."""
    a = np.asarray(cut.getchannel("A"))
    lab, n = ndimage.label(a > 24)
    if n <= 1:
        return cut
    sizes = ndimage.sum(np.ones_like(a), lab, range(1, n + 1))
    keep = np.isin(lab, [i + 1 for i, s in enumerate(sizes) if s >= 0.08 * sizes.max()])
    out = cut.copy()
    out.putalpha(Image.fromarray(np.where(keep, a, 0).astype(np.uint8)))
    return out

if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    sessions = {}
    for name, (fn, box, model) in CUTS.items():
        if model not in sessions:
            sessions[model] = new_session(model)
        im = Image.open(os.path.join(SCREENS, fn)).convert("RGB").crop(box)
        cut = keep_figures(remove(im, session=sessions[model], post_process_mask=True))
        bbox = cut.getchannel("A").point(lambda v: 255 if v > 24 else 0).getbbox()
        if bbox:
            cut = cut.crop(bbox)
        cut.save(os.path.join(OUT, name + ".png"))
        print(name, cut.size)
