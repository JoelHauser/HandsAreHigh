"""
A crime-paperback / open-world-game style cover for Mercy: a grid of panels with thick black borders,
each a bot with his hands up cut out against a flat, bold colour, painted look, and a title panel.
An homage to the genre's covers, with Mercy's own name and type (no other game's logo or font).

    python -I scripts/make_cover.py        docs/images/cover.jpg (1800 x 2400)

Cut-outs come from art/cutouts (scripts/make_cutouts.py, needs rembg); this needs numpy, pillow
and scipy only.
"""
import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont
from scipy import ndimage

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CUTS = os.path.join(ROOT, "art", "cutouts")
OUT = os.path.join(ROOT, "docs", "images", "cover.jpg")
FONT = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts", "bahnschrift.ttf")

W, H = 1800, 2400
GUTTER = 22
INK = (12, 12, 14)

# name, panel (x0, y0, x1, y1), background top/bottom colours, figure: fit ("w" width / "h" height),
# scale of that fit, horizontal anchor (0..1), rays centre (fractions of the panel)
PANELS = [
    ("sunset",  (0, 0, 1120, 780),      (255, 120, 40), (190, 30, 90),   "h", 0.96, 0.50, (0.5, 0.35)),
    ("snow",    (1120, 0, 1800, 780),   (120, 225, 245), (235, 250, 255), "w", 1.04, 0.50, (0.5, 0.30)),
    ("dark",    (0, 780, 560, 1500),    (255, 205, 40), (240, 120, 20),  "h", 1.02, 0.52, (0.5, 0.25)),
    ("kneel",   (560, 780, 1180, 1500), (255, 70, 150), (120, 20, 120),  "h", 0.98, 0.50, (0.5, 0.30)),
    ("reddoor", (1180, 780, 1800, 1800), (40, 200, 190), (10, 80, 110),  "h", 0.98, 0.50, (0.5, 0.22)),
    ("factory", (0, 1800, 520, 2400),   (250, 70, 40), (120, 10, 30),    "h", 1.00, 0.50, (0.5, 0.25)),
    ("wall",    (520, 1800, 1040, 2400), (150, 90, 230), (50, 20, 110),  "h", 1.00, 0.50, (0.5, 0.25)),
    ("captor",  (1040, 1800, 1800, 2400), (110, 200, 255), (250, 230, 170), "w", 0.98, 0.50, (0.6, 0.35)),
]
TITLE = (0, 1500, 1180, 1800)


def font(size, style="Bold Condensed"):
    f = ImageFont.truetype(FONT, size)
    try:
        f.set_variation_by_name(style)
    except Exception:
        pass
    return f


def inset(r, g=GUTTER // 2):
    x0, y0, x1, y1 = r
    return (x0 + (g if x0 > 0 else GUTTER), y0 + (g if y0 > 0 else GUTTER),
            x1 - (g if x1 < W else GUTTER), y1 - (g if y1 < H else GUTTER))


def background(w, h, top, bottom, rays_at):
    """Vertical gradient, soft sunburst rays, a band of halftone dots at the foot."""
    t = np.linspace(0, 1, h, dtype=np.float32)[:, None, None]
    a = np.array(top, np.float32) * (1 - t) + np.array(bottom, np.float32) * t
    a = np.repeat(a, w, axis=1)
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    cx, cy = rays_at[0] * w, rays_at[1] * h
    ang = np.arctan2(y - cy, x - cx)
    rays = (np.sin(ang * 14) > 0).astype(np.float32)
    a = a * (1 + 0.07 * rays[..., None])
    # halftone dots, growing towards the bottom
    step = 14
    gx, gy = (x % step) - step / 2, (y % step) - step / 2
    r = np.clip((y / h - 0.6) / 0.4, 0, 1) * step * 0.42
    dots = (gx * gx + gy * gy) < r * r
    a = np.where(dots[..., None], a * 0.82, a)
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))


def paint(cut, target_w, target_h):
    """The painted-cover look on a cut-out: levels, smooth colour, saturation, crisp edges, ink."""
    cut = cut.resize((target_w, target_h), Image.LANCZOS)
    rgb, alpha = cut.convert("RGB"), cut.getchannel("A")
    a = np.asarray(rgb, np.float32)
    m = np.asarray(alpha) > 128
    lum = a.mean(axis=2)
    lo, hi = np.percentile(lum[m], 2), np.percentile(lum[m], 98.5)
    a = np.clip((a - lo) / max(1.0, hi - lo), 0, 1)
    a = a ** 0.85                                                   # open the shadows a little
    rgb = Image.fromarray((a * 255).astype(np.uint8))
    k = max(3, (target_h // 260) | 1)
    rgb = rgb.filter(ImageFilter.MedianFilter(k)).filter(ImageFilter.SMOOTH_MORE)
    rgb = ImageEnhance.Color(rgb).enhance(1.45)
    rgb = ImageEnhance.Contrast(rgb).enhance(1.18)
    rgb = rgb.filter(ImageFilter.UnsharpMask(radius=3, percent=140, threshold=2))
    # interior ink: strong luminance edges darkened
    e = np.asarray(rgb.convert("L").filter(ImageFilter.FIND_EDGES), np.float32)
    e = ndimage.gaussian_filter(e, 0.8)
    ink = np.clip((e - 28) / 60, 0, 1)[..., None] * 0.55
    p = np.asarray(rgb, np.float32) * (1 - ink)
    out = Image.fromarray(np.clip(p, 0, 255).astype(np.uint8)).convert("RGBA")
    out.putalpha(alpha)
    return out


def outline(alpha, px):
    """A dark outline layer around the figure."""
    m = np.asarray(alpha) > 100
    d = ndimage.binary_dilation(m, iterations=px)
    o = Image.new("RGBA", alpha.size, INK + (0,))
    o.putalpha(Image.fromarray((d * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.8)))
    return o


def panel(name, rect, top, bottom, fit, scale, anchor, rays_at):
    x0, y0, x1, y1 = inset(rect)
    pw, ph = x1 - x0, y1 - y0
    bg = background(pw, ph, top, bottom, rays_at).convert("RGBA")
    cut = Image.open(os.path.join(CUTS, name + ".png")).convert("RGBA")
    cw, ch = cut.size
    s = (pw * scale / cw) if fit == "w" else (ph * scale / ch)
    tw, th = max(1, int(cw * s)), max(1, int(ch * s))
    fig = paint(cut, tw, th)
    ox = int(anchor * pw - tw / 2)
    oy = ph - th + int(ph * 0.02)                                   # feet / waist on the panel's foot
    pad = 8
    layer = Image.new("RGBA", (tw + 2 * pad, th + 2 * pad), (0, 0, 0, 0))
    layer.alpha_composite(fig, (pad, pad))
    ol = outline(layer.getchannel("A"), 5)
    # a soft drop shadow, then the outline, then the figure
    shadow = Image.new("RGBA", layer.size, (0, 0, 0, 0))
    shadow.putalpha(ol.getchannel("A").point(lambda v: int(v * 0.45)).filter(ImageFilter.GaussianBlur(10)))
    for lay, dx, dy in ((shadow, 14, 10), (ol, 0, 0), (layer, 0, 0)):
        tmp = Image.new("RGBA", bg.size, (0, 0, 0, 0))   # paste clips at the panel edges
        tmp.paste(lay, (ox - pad + dx, oy - pad + dy), lay)
        bg.alpha_composite(tmp)
    return bg, (x0, y0)


def title_panel(rect):
    x0, y0, x1, y1 = inset(rect)
    pw, ph = x1 - x0, y1 - y0
    p = Image.new("RGBA", (pw, ph), INK + (255,))
    d = ImageDraw.Draw(p)
    big = font(int(ph * 0.86))
    text = "MERCY"
    spacing = int(big.size * 0.06)
    total = sum(big.getlength(c) for c in text) + spacing * (len(text) - 1)
    x = (pw - total) / 2
    asc, desc = big.getmetrics()
    yb = ph * 0.45 + (asc - desc) / 2
    for c in text:
        d.text((x + 6, yb + 6), c, font=big, fill=(200, 30, 60), anchor="ls")     # offset colour shadow
        d.text((x, yb), c, font=big, fill=(250, 246, 236), anchor="ls")
        x += big.getlength(c) + spacing
    sub = font(int(ph * 0.12), "SemiBold SemiCondensed")
    line = "BOTS CAN GIVE UP"
    sp = int(sub.size * 0.35)
    total = sum(sub.getlength(c) for c in line) + sp * (len(line) - 1)
    x = (pw - total) / 2
    for c in line:
        d.text((x, ph * 0.90), c, font=sub, fill=(255, 205, 40), anchor="ls")
        x += sub.getlength(c) + sp
    # a small tag in the corner, like a rating box
    tag = font(int(ph * 0.085), "Bold SemiCondensed")
    tx, ty = 18, 16
    tw = int(max(tag.getlength(s) for s in ("SPT 4.1", "BIGBRAIN"))) + 20
    th = int(tag.size * 2.2) + 12
    box = Image.new("RGBA", (tw, th), (250, 246, 236, 255))
    bd = ImageDraw.Draw(box)
    for i, s in enumerate(("SPT 4.1", "BIGBRAIN")):
        bd.text((10, 6 + i * int(tag.size * 1.1)), s, font=tag, fill=INK, anchor="lt")
    p.alpha_composite(box, (tx, ty))
    return p, (x0, y0)


def main():
    cover = Image.new("RGBA", (W, H), INK + (255,))
    for spec in PANELS:
        img, at = panel(*spec)
        cover.alpha_composite(img, at)
    img, at = title_panel(TITLE)
    cover.alpha_composite(img, at)
    # a whisper of grain over the whole page so the panels sit together
    rng = np.random.default_rng(7)
    a = np.asarray(cover.convert("RGB"), np.float32)
    a += rng.normal(0, 4.0, a.shape[:2])[..., None]
    Image.fromarray(np.clip(a, 0, 255).astype(np.uint8)).save(OUT, quality=92)
    print(f"cover: {OUT} ({os.path.getsize(OUT) / 1e6:.1f} MB)")


if __name__ == "__main__":
    main()
