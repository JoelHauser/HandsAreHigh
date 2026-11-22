"""
The mod page's section images, from Joel's own framed shots in art/screens/page (kept as he made
them: no grade, only resized and saved as JPEG for the page).

    python -I scripts/make_page_images.py
"""
import os

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "art", "screens", "page")
OUT = os.path.join(ROOT, "docs", "images")
WIDTH = 1600
GAP = 8                     # black line between stacked strips
INK = (10, 10, 12)


def load(n):
    return Image.open(os.path.join(SRC, f"page-{n}.png")).convert("RGB")


def fit(im, w=WIDTH):
    return im.resize((w, round(im.height * w / im.width)), Image.LANCZOS) if im.width > w else im


def save(im, name):
    path = os.path.join(OUT, name)
    im.save(path, quality=90, optimize=True, progressive=True)
    print(f"{name}: {im.size[0]}x{im.size[1]}, {os.path.getsize(path) / 1e6:.2f} MB")


if __name__ == "__main__":
    # Four thin hands-up strips stacked into one lineup, black lines between them.
    strips = [fit(load(n)) for n in (13, 14, 15, 16)]
    h = sum(s.height for s in strips) + GAP * (len(strips) - 1)
    lineup = Image.new("RGB", (WIDTH, h), INK)
    y = 0
    for s in strips:
        lineup.paste(s, (0, y))
        y += s.height + GAP
    save(lineup, "lineup.jpg")
    save(fit(load(18)), "orders.jpg")      # hands behind his head, warehouse fire
    save(fit(load(12)), "prisoner.jpg")    # bot captor and his kneeling prisoner
    save(fit(load(17)), "roadblock.jpg")   # kneeling at the roadblock
