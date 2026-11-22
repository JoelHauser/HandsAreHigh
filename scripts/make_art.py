"""
Mercy's page art and teaser, made from Joel's in-game screenshots (art/screens).

One look for everything: cold, desaturated, teal shadows and warm skin, film grain, vignette,
Bahnschrift type in caps with a bone-white accent bar. The debug-scenario overlays in the
screenshots are cropped away (CLEAN boxes below).

    python -I scripts/make_art.py            everything
    python -I scripts/make_art.py stills     docs/images: thumbnail.png, holding.jpg, captors.jpg
    python -I scripts/make_art.py hero       docs/images/hero.jpg (header: the sunset close-up)
    python -I scripts/make_art.py hero-anim  + docs/images/hero-anim.webp (slow push-in, ~18 MB)
    python -I scripts/make_art.py reel       docs/images/hero-reel.webp (the older four-shot banner)
    python -I scripts/make_art.py teaser     dist/Mercy-teaser.mp4 (1080p, silent, for YouTube)

Needs numpy, pillow and ffmpeg on the path. It is a trailer made of stills, not a recording of the
game: a real in-game capture would beat it.
"""
import math
import os
import subprocess
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCREENS = os.path.join(ROOT, "art", "screens")
IMAGES = os.path.join(ROOT, "docs", "images")
DIST = os.path.join(ROOT, "dist")
FONT = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts", "bahnschrift.ttf")

BONE = (236, 230, 216)      # text
ACCENT = (226, 205, 160)    # the bar under captions: an off-white flag, not a colour
DIM = (150, 156, 160)

# Source shots: file, the clean box (debug overlays cut away), and where the subject is (source px).
SHOTS = {
    "snow":    ("snow-hands-up.png",           (0, 0, 1436, 1107),     (650, 420)),
    "field":   ("field-squad.png",             (640, 0, 2700, 1440),   (1720, 820)),
    "kneel":   ("containers-kneel-close.png",  (640, 0, 2700, 1440),   (1730, 760)),
    "captor":  ("containers-captor.png",       (640, 0, 2700, 1440),   (1560, 860)),
    "ware":    ("warehouse-kneel.png",         (640, 0, 2700, 1440),   (1790, 470)),
}

_cache = {}


def font(size, style="Bold Condensed"):
    f = ImageFont.truetype(FONT, size)
    try:
        f.set_variation_by_name(style)
    except Exception:
        pass
    return f


def grade(img):
    """The look: lifted blacks, teal shadows, warm highlights, 70% saturation, gentle S-curve."""
    a = np.asarray(img.convert("RGB"), dtype=np.float32) / 255.0
    lum = (a @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32))[..., None]
    a = lum + (a - lum) * 0.80
    a = np.clip(a, 0, 1)
    a = np.clip(a * 1.10, 0, 1)                                   # a touch brighter first
    a = a * a * (3 - 2 * a) * 0.35 + a * 0.65                     # soft S-curve
    shadow = np.array([0.06, 0.11, 0.13], dtype=np.float32)        # teal
    high = np.array([1.03, 0.99, 0.93], dtype=np.float32)          # warm
    a = shadow * (1 - lum) * 0.35 + a * (high * lum + (1 - lum))
    a = 0.035 + a * 0.94                                           # lifted blacks
    return Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8))


def source(name):
    """The shot's clean region, graded once."""
    if name not in _cache:
        fn, box, focus = SHOTS[name]
        im = Image.open(os.path.join(SCREENS, fn)).convert("RGB").crop(box)
        _cache[name] = (grade(im), (focus[0] - box[0], focus[1] - box[1]))
    return _cache[name]


def view(name, w, h, zoom=1.0, drift=(0.0, 0.0)):
    """A w x h frame of the shot: the largest window of that aspect around the subject, / zoom,
    shifted by drift (fractions of the window)."""
    im, (fx, fy) = source(name)
    iw, ih = im.size
    aspect = w / h
    vw = min(iw, ih * aspect) / zoom
    vh = vw / aspect
    cx = fx + drift[0] * vw
    cy = fy + drift[1] * vh
    x0 = min(max(cx - vw / 2, 0), iw - vw)
    y0 = min(max(cy - vh / 2, 0), ih - vh)
    return im.resize((w, h), Image.BICUBIC, box=(x0, y0, x0 + vw, y0 + vh))


_vig = {}


def finish(frame, seed=0, grain=0.018, vignette=0.45, bars=0.0):
    """Vignette, film grain and optional letterbox bars."""
    w, h = frame.size
    key = (w, h, vignette)
    if key not in _vig:
        y, x = np.mgrid[0:h, 0:w].astype(np.float32)
        r = np.sqrt(((x - w / 2) / (w / 2)) ** 2 + ((y - h / 2) / (h / 2)) ** 2) / np.sqrt(2)
        _vig[key] = (1 - vignette * np.clip(r - 0.25, 0, 1) ** 1.6)[..., None]
    a = np.asarray(frame, dtype=np.float32) * _vig[key]
    rng = np.random.default_rng(seed)
    n = rng.normal(0, 255 * grain, (h // 2, w // 2)).astype(np.float32)
    n = np.repeat(np.repeat(n, 2, 0), 2, 1)[:h, :w, None]
    a = np.clip(a + n, 0, 255).astype(np.uint8)
    out = Image.fromarray(a)
    if bars > 0:
        d = ImageDraw.Draw(out)
        b = int(h * bars)
        d.rectangle((0, 0, w, b), fill=(6, 7, 8))
        d.rectangle((0, h - b, w, h), fill=(6, 7, 8))
    return out


def shadowed_text(img, xy, text, f, fill=BONE, spacing=0, alpha=1.0, anchor="la"):
    """Text with a soft dark shadow, optionally letter-spaced, faded by alpha."""
    if alpha <= 0:
        return
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    sh = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d, ds = ImageDraw.Draw(layer), ImageDraw.Draw(sh)
    x, y = xy
    if spacing:
        # one glyph at a time on the baseline (top-anchoring each glyph lifted full stops to cap height)
        asc, desc = f.getmetrics()
        y = y + asc if anchor[1] == "t" else y + (asc - desc) / 2 if anchor[1] == "m" else y
        total = sum(f.getlength(c) for c in text) + spacing * (len(text) - 1)
        if anchor[0] == "m":
            x -= total / 2
        elif anchor[0] == "r":
            x -= total
        for c in text:
            ds.text((x + 2, y + 3), c, font=f, fill=(0, 0, 0, int(200 * alpha)), anchor="ls")
            d.text((x, y), c, font=f, fill=fill + (int(255 * alpha),), anchor="ls")
            x += f.getlength(c) + spacing
    else:
        ds.text((x + 2, y + 3), text, font=f, fill=(0, 0, 0, int(200 * alpha)), anchor=anchor)
        d.text((x, y), text, font=f, fill=fill + (int(255 * alpha),), anchor=anchor)
    sh = sh.filter(ImageFilter.GaussianBlur(max(2, f.size // 14)))
    img.alpha_composite(sh)
    img.alpha_composite(layer)


def caption(img, lines, scale=1.0, alpha=1.0, bar=1.0):
    """Lower-left caption: an accent bar, then the line(s) in caps."""
    w, h = img.size
    x = int(w * 0.055)
    big = font(int(h * 0.085 * scale))
    gap = int(big.size * 1.05)
    y = int(h * 0.80) - gap * (len(lines) - 1)
    d = ImageDraw.Draw(img)
    bw = int(w * 0.06 * bar)
    if bw > 0 and alpha > 0:
        d.rectangle((x, y - int(h * 0.035), x + bw, y - int(h * 0.035) + max(2, h // 220)), fill=ACCENT + (int(255 * alpha),))
    for i, line in enumerate(lines):
        shadowed_text(img, (x, y + gap * i), line.upper(), big, spacing=int(big.size * 0.04), alpha=alpha, anchor="lt")


def wordmark(img, cx, cy, size, alpha=1.0, sub=None):
    """MERCY, wide-spaced, with an optional line under it."""
    f = font(size)
    shadowed_text(img, (cx, cy), "MERCY", f, spacing=int(size * 0.28), alpha=alpha, anchor="mm")
    if sub:
        fs = font(max(12, int(size * 0.20)), "SemiBold SemiCondensed")
        d = ImageDraw.Draw(img)
        lw = int(size * 1.6)
        y = cy + int(size * 0.62)
        d.rectangle((cx - lw // 2, y, cx + lw // 2, y + max(2, size // 50)), fill=ACCENT + (int(255 * alpha),))
        shadowed_text(img, (cx, y + int(size * 0.24)), sub.upper(), fs, fill=BONE, spacing=int(fs.size * 0.30), alpha=alpha, anchor="mt")


def ease(t):
    t = min(max(t, 0.0), 1.0)
    return t * t * (3 - 2 * t)


# ---- stills -------------------------------------------------------------------------------

def stills():
    os.makedirs(IMAGES, exist_ok=True)
    # Thumbnail: square, the snow shot (hands up, cigarette, smirk), wordmark at the foot.
    t = finish(view("snow", 1024, 1024, zoom=1.0, drift=(0, 0.05)), seed=1, vignette=0.7).convert("RGBA")
    shade = Image.new("RGBA", t.size, (0, 0, 0, 0))
    ImageDraw.Draw(shade).rectangle((0, 700, 1024, 1024), fill=(8, 10, 12, 0))
    grad = np.linspace(0, 200, 324).astype(np.uint8)
    band = Image.fromarray(np.repeat(grad[:, None], 1024, 1), "L")
    blk = Image.new("RGBA", (1024, 324), (8, 10, 12, 255))
    blk.putalpha(band)
    t.alpha_composite(blk, (0, 700))
    wordmark(t, 512, 880, 150)
    t.convert("RGB").save(os.path.join(IMAGES, "thumbnail.png"))

    # Section images, 1600 x 680 (2.35:1).
    for name, shot, lines, drift, zoom in [
        ("holding.jpg", "kneel", ["On your knees.", "Hands on your head."], (0.12, 0.02), 1.0),
        ("captors.jpg", "field", ["Bots take", "prisoners too."], (0.0, 0.0), 1.0),
    ]:
        im = finish(view(shot, 1600, 680, zoom=zoom, drift=drift), seed=hash(name) & 0xFFFF).convert("RGBA")
        caption(im, lines, scale=0.95)
        im.convert("RGB").save(os.path.join(IMAGES, name), quality=88)
    print("stills: docs/images/thumbnail.png, holding.jpg, captors.jpg")


# ---- hero (animated WebP banner) -----------------------------------------------------------

HERO = [  # shot, caption, zoom from -> to, drift from -> to
    ("snow",   ["He's had enough."],              1.00, 1.08, (0, 0.10), (0, 0.02)),
    ("ware",   ["On your knees."],                1.02, 1.10, (0, 0.08), (0, 0.02)),
    ("captor", ["Bots take prisoners too."],      1.00, 1.07, (-0.05, 0.02), (0.05, 0.0)),
    ("kneel",  ["He's watching", "for his chance."], 1.05, 1.12, (0.18, 0.02), (0.12, 0.0)),
]


def hero_frames(w=1280, h=544, fps=12, shot_s=2.4, title_s=2.6, fade=0.45):
    frames = []
    n_shot = int(shot_s * fps)
    for si, (shot, lines, z0, z1, d0, d1) in enumerate(HERO):
        for i in range(n_shot):
            t = i / n_shot
            z = z0 + (z1 - z0) * t
            d = (d0[0] + (d1[0] - d0[0]) * t, d0[1] + (d1[1] - d0[1]) * t)
            # grain held per shot and faint: per-frame noise made the WebP 13 MB
            f = finish(view(shot, w, h, z, d), seed=si, grain=0.006).convert("RGBA")
            a = ease((t * shot_s - 0.25) / 0.35) * (1 - ease((t * shot_s - (shot_s - 0.35)) / 0.3))
            caption(f, lines, scale=0.9, alpha=a, bar=ease((t * shot_s - 0.15) / 0.4))
            # fade in from black at the start of each shot
            k = ease(t * shot_s / fade)
            if k < 1:
                f = Image.blend(Image.new("RGBA", f.size, (6, 7, 8, 255)), f, k)
            frames.append(f.convert("RGB"))
    # Title over a darkened, blurred last shot.
    n_t = int(title_s * fps)
    base = finish(view("kneel", w, h, 1.12, (0.12, 0.0)), seed=9, grain=0.0).filter(ImageFilter.GaussianBlur(6))
    base = Image.blend(Image.new("RGB", base.size, (6, 7, 8)), base, 0.45)
    for i in range(n_t):
        t = i / n_t
        f = base.copy().convert("RGBA")
        wordmark(f, w // 2, int(h * 0.44), int(h * 0.20), alpha=ease(t * title_s / 0.6), sub="Bots can give up")
        k = 1 - ease((t * title_s - (title_s - 0.4)) / 0.4)
        if k < 1:
            f = Image.blend(Image.new("RGBA", f.size, (6, 7, 8, 255)), f, k)
        frames.append(f.convert("RGB"))
    return frames, fps


def warm(img):
    """The sunset shot keeps its colours: a little contrast and depth only."""
    a = np.asarray(img.convert("RGB"), dtype=np.float32) / 255.0
    a = a * a * (3 - 2 * a) * 0.30 + a * 0.70
    a = 0.02 + a * 0.97
    return Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8))


def hero_sunset(w=1280, h=720, fps=12, secs=10.0):
    """The hero banner: the masked man with his hands up at sunset. A slow push-in towards him that
    eases back out (a seamless loop), MERCY over the sky above him."""
    src = warm(Image.open(os.path.join(SCREENS, "sunset-hands-up.png")).convert("RGB"))
    iw, ih = src.size
    box0 = (0, ih - iw * h / w, iw, ih)                    # widest 16:9 window, bottom-aligned
    fx, fy = 905, 980                                      # his face / chest
    n = int(secs * fps)
    title = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    # darken the sky band behind the title a little for contrast
    band = np.zeros((h, w), np.float32)
    ys = np.arange(h, dtype=np.float32)[:, None]
    band[:] = np.clip(1 - ys / (h * 0.42), 0, 1) ** 1.5 * 120
    shade = Image.new("RGBA", (w, h), (8, 10, 12, 255))
    shade.putalpha(Image.fromarray(band.astype(np.uint8)))
    title.alpha_composite(shade)
    wordmark(title, w // 2, int(h * 0.14), int(h * 0.15), sub="Bots can give up")
    frames = []
    for i in range(n):
        k = 0.5 - 0.5 * math.cos(2 * math.pi * i / n)      # 0 -> 1 -> 0
        z = 1.0 + 0.10 * k
        vw, vh = (box0[2] - box0[0]) / z, (box0[3] - box0[1]) / z
        cx = (box0[0] + box0[2]) / 2 + (fx - (box0[0] + box0[2]) / 2) * k * 0.6
        cy = (box0[1] + box0[3]) / 2 + (fy - (box0[1] + box0[3]) / 2) * k * 0.6
        x0 = min(max(cx - vw / 2, 0), iw - vw)
        y0 = min(max(cy - vh / 2, 0), ih - vh)
        f = src.resize((w, h), Image.BICUBIC, box=(x0, y0, x0 + vw, y0 + vh))
        f = finish(f, seed=3, grain=0.006, vignette=0.5).convert("RGBA")
        f.alpha_composite(title)
        frames.append(f.convert("RGB"))
    return frames, fps


def hero(animated=False):
    """docs/images/hero.jpg, the page header (still). animated: also hero-anim.webp, the slow push-in
    (~18 MB: the foliage changes every frame, so the page uses the still)."""
    frames, fps = hero_sunset(secs=10.0 if animated else 0.1)
    still = os.path.join(IMAGES, "hero.jpg")
    frames[0].save(still, quality=92)
    print(f"hero: {still} ({os.path.getsize(still) / 1e6:.2f} MB)")
    if animated:
        out = os.path.join(IMAGES, "hero-anim.webp")
        frames[0].save(out, save_all=True, append_images=frames[1:], duration=int(1000 / fps), loop=0, quality=70, method=6)
        print(f"hero: {out} ({len(frames)} frames, {os.path.getsize(out) / 1e6:.1f} MB)")


def hero_reel():
    """The earlier four-shot banner (kept; not used on the page)."""
    frames, fps = hero_frames()
    out = os.path.join(IMAGES, "hero-reel.webp")
    frames[0].save(out, save_all=True, append_images=frames[1:], duration=int(1000 / fps), loop=0, quality=62, method=6)
    print(f"hero: {out} ({len(frames)} frames, {os.path.getsize(out) / 1e6:.1f} MB)")


# ---- teaser (1080p MP4, silent) ------------------------------------------------------------

def card(w, h, lines, alpha):
    f = Image.new("RGBA", (w, h), (6, 7, 8, 255))
    fs = font(int(h * 0.058), "SemiBold SemiCondensed")
    gap = int(fs.size * 1.5)
    y = h // 2 - gap * (len(lines) - 1) // 2
    for i, line in enumerate(lines):
        shadowed_text(f, (w // 2, y + gap * i), line, fs, fill=BONE, alpha=alpha, anchor="mm")
    return f


# (kind, seconds, args): "card" lines | "shot" name, caption lines, zoom from/to, drift from/to | "title"
TEASER = [
    ("card", 3.0, ["Most fights end the usual way."]),
    ("card", 3.4, ["But now and then,", "the man across the room has had enough."]),
    ("shot", 3.6, ("snow", ["He drops his gun.", "Hands up."], 1.0, 1.10, (0, 0.12), (0, 0.0))),
    ("shot", 3.6, ("ware", ["On your knees.", "Hands on your head."], 1.0, 1.12, (0, 0.10), (0, 0.0))),
    ("shot", 3.4, ("kneel", ["He does what he's told."], 1.02, 1.14, (0.16, 0.04), (0.10, 0.0))),
    ("card", 3.4, ["Look away, and he starts thinking", "about the gun at his feet."]),
    ("shot", 3.6, ("captor", ["Bots take prisoners too."], 1.0, 1.08, (-0.06, 0.02), (0.06, 0.0))),
    ("shot", 3.6, ("field", ["Not every captor", "is merciful."], 1.0, 1.10, (0, 0.04), (0, 0.0))),
    ("title", 5.0, None),
]


def teaser():
    w, h, fps = 1920, 1080, 30
    os.makedirs(DIST, exist_ok=True)
    out = os.path.join(DIST, "Mercy-teaser.mp4")
    cmd = ["ffmpeg", "-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{w}x{h}", "-r", str(fps),
           "-i", "-", "-c:v", "libx264", "-preset", "slow", "-crf", "17", "-pix_fmt", "yuv420p", "-movflags", "+faststart", out]
    p = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    black = Image.new("RGB", (w, h), (6, 7, 8))
    count = 0
    for si, (kind, secs, args) in enumerate(TEASER):
        n = int(secs * fps)
        for i in range(n):
            t, s = i / n, i / fps
            fade = min(ease(s / 0.5), 1 - ease((s - (secs - 0.5)) / 0.5))
            if kind == "card":
                f = card(w, h, args, ease((s - 0.2) / 0.6) * (1 - ease((s - (secs - 0.6)) / 0.5))).convert("RGB")
            elif kind == "shot":
                shot, lines, z0, z1, d0, d1 = args
                z = z0 + (z1 - z0) * t
                d = (d0[0] + (d1[0] - d0[0]) * t, d0[1] + (d1[1] - d0[1]) * t)
                f = finish(view(shot, w, h, z, d), seed=si * 10000 + i, bars=0.06).convert("RGBA")
                a = ease((s - 0.45) / 0.45) * (1 - ease((s - (secs - 0.55)) / 0.4))
                caption(f, lines, scale=0.62, alpha=a, bar=ease((s - 0.3) / 0.5))
                f = Image.blend(black, f.convert("RGB"), fade)
            else:
                base = finish(view("snow", w, h, 1.12, (0, 0.02)), seed=77).filter(ImageFilter.GaussianBlur(8))
                base = Image.blend(black, base, 0.35).convert("RGBA")
                wordmark(base, w // 2, int(h * 0.42), int(h * 0.17), alpha=ease((s - 0.2) / 0.8), sub="Bots can give up")
                shadowed_text(base, (w // 2, int(h * 0.80)), "SPT 4.1  \u00b7  REQUIRES BIGBRAIN", font(int(h * 0.026), "SemiBold SemiCondensed"),
                              fill=DIM, spacing=6, alpha=ease((s - 1.2) / 0.8), anchor="mm")
                f = Image.blend(black, base.convert("RGB"), 1 - ease((s - (secs - 0.8)) / 0.8))
            p.stdin.write(f.tobytes())
            count += 1
    p.stdin.close()
    p.wait()
    print(f"teaser: {out} ({count / fps:.1f} s, {os.path.getsize(out) / 1e6:.1f} MB)")


if __name__ == "__main__":
    what = sys.argv[1] if len(sys.argv) > 1 else "all"
    if what in ("all", "stills"):
        stills()
    if what in ("all", "hero"):
        hero()
    if what == "hero-anim":
        hero(animated=True)
    if what == "reel":
        hero_reel()
    if what in ("all", "teaser"):
        teaser()
