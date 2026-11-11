"""Builds Assets/gtctrl_icon.png (1024x1024), the gtCTRL app icon, from the EV6 GT pixel art.

The body is repainted the way Assets/Paint.gdshader does it (pale-blue body pixels only), the backdrop around the
car is turned graphite (GT Neon theme), and a pixel "GT" mark is drawn in the neon accent. No maker logos.

    python3 -m venv .venv && .venv/bin/pip install pillow numpy
    .venv/bin/python Tools/Icon/make_icon.py [paint hex]
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "Assets" / "kia_ev6_gt_pixel.png"
OUT = ROOT / "Assets" / "gtctrl_icon.png"
PAINT = sys.argv[1] if len(sys.argv) > 1 else "525252"  # Interstellar Gray, the default paint
BACK = (13, 15, 16)          # GT Neon Back (0d0f10)
GLOW = (26, 30, 32)          # a little lighter behind the car
NEON = (212, 245, 60)        # GT Neon accent (d4f53c)


def hexrgb(h):
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], dtype=np.float32) / 255


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def repaint(rgb):
    """Assets/Paint.gdshader, in numpy."""
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    bright = rgb.max(axis=2)
    body = smoothstep(0.065, 0.13, b - r) * smoothstep(0.28, 0.48, bright) * (1 - smoothstep(0.05, 0.13, g - b))
    shade = np.clip(bright / 0.94, 0, 1.15)[..., None]
    tinted = hexrgb(PAINT) * shade
    tinted = tinted + (0.92 - tinted) * (smoothstep(0.91, 1.0, bright) * 0.27)[..., None]
    return rgb + (tinted - rgb) * body[..., None]


def silhouette(rgb):
    """Core/Silhouette.cs: everything between the outermost car pixels of each row and of each column."""
    back = np.median(np.concatenate([rgb[:8].reshape(-1, 3), rgb[-8:].reshape(-1, 3)]), axis=0)
    car = np.abs(rgb - back).max(axis=2) > 0.06
    h, w = car.shape
    rows = np.zeros_like(car)
    cols = np.zeros_like(car)
    for y in range(h):
        xs = np.flatnonzero(car[y])
        if xs.size:
            rows[y, xs[0]:xs[-1] + 1] = True
    for x in range(w):
        ys = np.flatnonzero(car[:, x])
        if ys.size:
            cols[ys[0]:ys[-1] + 1, x] = True
    return rows & cols


def pixel_text(text, block):
    """A 5x7 pixel font, just the letters needed."""
    glyphs = {
        "G": ["01110", "10001", "10000", "10111", "10001", "10001", "01111"],
        "T": ["11111", "00100", "00100", "00100", "00100", "00100", "00100"],
    }
    width = len(text) * 6 - 1
    img = Image.new("RGBA", (width * block, 7 * block), (0, 0, 0, 0))
    px = img.load()
    for i, ch in enumerate(text):
        for y, row in enumerate(glyphs[ch]):
            for x, bit in enumerate(row):
                if bit == "1":
                    for dy in range(block):
                        for dx in range(block):
                            px[(i * 6 + x) * block + dx, y * block + dy] = NEON + (255,)
    return img


def main():
    src = np.asarray(Image.open(SOURCE).convert("RGB"), dtype=np.float32) / 255
    painted = repaint(src)
    inside = silhouette(src)
    out = np.where(inside[..., None], painted, np.array(GLOW, dtype=np.float32) / 255)
    car = Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8)).convert("RGBA")
    car.putalpha(Image.fromarray((inside * 255).astype(np.uint8)))

    icon = Image.new("RGBA", (1024, 1024), BACK + (255,))
    # A soft lighter band behind the car.
    band = Image.new("RGBA", (1024, 520), GLOW + (255,))
    mask = Image.linear_gradient("L").resize((1024, 520))
    mask = Image.fromarray((np.minimum(np.asarray(mask), 255 - np.asarray(mask)) * 2).astype(np.uint8))
    icon.paste(band, (0, 330), mask)

    width = 1000
    height = round(car.height * width / car.width)
    scaled = car.resize((width, height), Image.BOX)
    icon.alpha_composite(scaled, (12, 600 - height // 2))

    mark = pixel_text("GT", 22)
    icon.alpha_composite(mark, (96, 120))
    # Neon underline, like the GT's lower trim stripe.
    bar = Image.new("RGBA", (mark.width, 10), NEON + (255,))
    icon.alpha_composite(bar, (96, 120 + mark.height + 22))

    icon.convert("RGB").save(OUT)
    print(f"Wrote {OUT.relative_to(ROOT)} (paint #{PAINT})")


def splash():
    """Assets/gtctrl_splash.png: the launch screen. The car on GT Neon graphite with the GT mark, portrait."""
    src = np.asarray(Image.open(SOURCE).convert("RGB"), dtype=np.float32) / 255
    inside = silhouette(src)
    car = Image.fromarray((np.clip(repaint(src), 0, 1) * 255).astype(np.uint8)).convert("RGBA")
    car.putalpha(Image.fromarray((inside * 255).astype(np.uint8)))
    w, h = 1290, 2796  # iPhone portrait, centered and scaled to fit on smaller phones
    screen = Image.new("RGBA", (w, h), BACK + (255,))
    width = 1150
    height = round(car.height * width / car.width)
    screen.alpha_composite(car.resize((width, height), Image.BOX), ((w - width) // 2, h // 2 - height // 2))
    mark = pixel_text("GT", 18)
    screen.alpha_composite(mark, ((w - mark.width) // 2, h // 2 + height // 2 + 90))
    screen.convert("RGB").save(ROOT / "Assets" / "gtctrl_splash.png")
    print("Wrote Assets/gtctrl_splash.png")


# Kia presets after the first (the main icon). Keep in step with Core/CarPaints.cs.
ALTERNATES = {"Yacht Blue": "2b4f86", "Runway Red": "a3222b", "Moonscape": "7a7f78",
              "Aurora Black Pearl": "1c1f24", "Snow White Pearl": "e9ecee"}


def alternates():
    """Native/iOS/AppIcons/AppIcon-<Paint>.png: gtCTRL's icon in each other Kia paint (iOS alternate icons)."""
    global PAINT, OUT
    folder = ROOT / "Native" / "iOS" / "AppIcons"
    folder.mkdir(exist_ok=True)
    (folder / ".gdignore").touch()
    keep = (PAINT, OUT)
    for name, hexcode in ALTERNATES.items():
        PAINT, OUT = hexcode, folder / ("AppIcon-" + "".join(c for c in name if c.isalnum()) + ".png")
        main()
    PAINT, OUT = keep


def tinted():
    """Native/iOS/AppIcons/Tinted.png: the icon for the Tinted and Clear home screens. iOS reads it as brightness
    only and adds the user's tint, so it is grayscale on black: the original pale art (light body, dark outlines and
    glass) and a white GT mark. One for every paint, since the tint replaces the paint anyway."""
    src = np.asarray(Image.open(SOURCE).convert("RGB"), dtype=np.float32) / 255
    inside = silhouette(src)
    lum = src[..., 0] * 0.299 + src[..., 1] * 0.587 + src[..., 2] * 0.114
    lum = np.clip((lum - 0.08) / 0.84, 0, 1) ** 0.9  # full range: outlines near black, body near white
    gray = (np.repeat(lum[..., None], 3, axis=2) * 255).astype(np.uint8)
    car = Image.fromarray(gray).convert("RGBA")
    car.putalpha(Image.fromarray((inside * 255).astype(np.uint8)))
    icon = Image.new("RGBA", (1024, 1024), (0, 0, 0, 255))
    width = 1000
    height = round(car.height * width / car.width)
    icon.alpha_composite(car.resize((width, height), Image.BOX), (12, 600 - height // 2))
    mark = pixel_text("GT", 22)
    white = Image.new("RGBA", mark.size, (255, 255, 255, 255))
    icon.paste(white, (96, 120), mark)
    icon.paste(Image.new("RGBA", (mark.width, 10), (255, 255, 255, 255)), (96, 120 + mark.height + 22))
    out = ROOT / "Native" / "iOS" / "AppIcons" / "Tinted.png"
    icon.convert("RGB").save(out)
    print(f"Wrote {out.relative_to(ROOT)}")


if __name__ == "__main__" and sys.argv[1:] == ["--tinted"]:
    tinted()
    sys.exit()
if __name__ == "__main__":
    main()
    splash()
    alternates()
    tinted()
