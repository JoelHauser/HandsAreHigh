"""
docs/images/logo.png: the MERCY wordmark (art/logo-source.png, cream on black) with the black made
transparent, cropped to the letters. The skyline and specks cut into the letters go transparent too.

    python -I scripts/make_logo.py
"""
import os

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "art", "logo-source.png")
OUT = os.path.join(ROOT, "docs", "images", "logo.png")
PAD = 24

im = np.asarray(Image.open(SRC).convert("RGB"), np.float32)
bg = np.median(im[:200].reshape(-1, 3), axis=0)                 # the flat black
lum = im.mean(axis=2)
ink = im[lum > 200].mean(axis=0)                                # the cream of the letters
alpha = np.clip((lum - bg.mean() - 3) / (ink.mean() - bg.mean() - 3), 0, 1)
safe = np.maximum(alpha, 1e-3)[..., None]
rgb = np.clip((im - bg * (1 - alpha[..., None])) / safe, 0, 255)   # colour without the black mixed in
rgba = np.dstack([rgb, alpha * 255]).astype(np.uint8)
ys, xs = np.where(alpha > 0.05)
x0, y0 = max(0, xs.min() - PAD), max(0, ys.min() - PAD)
x1, y1 = min(im.shape[1], xs.max() + PAD + 1), min(im.shape[0], ys.max() + PAD + 1)
Image.fromarray(rgba[y0:y1, x0:x1], "RGBA").save(OUT, optimize=True)
print(f"logo: {OUT} ({x1 - x0}x{y1 - y0}, {os.path.getsize(OUT) / 1e3:.0f} KB)")
