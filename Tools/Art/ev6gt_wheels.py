"""Redraws the wheels of Assets/kia_ev6_gt_pixel.png as the EV6 GT's 21-inch wheels.

Reference: Kia's EV6 GT wheel close-ups. Five straight, bold spokes that kink sharply near the rim, gloss black with a
crisp machined silver stripe along one side; a bright machined rim lip; big openings showing a light brake disc and the
neon caliper; a small black hub with lug nuts. No Kia logo.

Each wheel is drawn flat with straight-edged polygons, squashed into its oval in the three-quarter picture, rendered
at half size and scaled up so it keeps the picture's pixel look. Colors stay neutral (never pale blue) so
Assets/Paint.gdshader doesn't paint them as body.

    .venv/bin/python Tools/Art/ev6gt_wheels.py <out.png>      # preview
    .venv/bin/python Tools/Art/ev6gt_wheels.py --apply        # overwrite the picture
"""
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
PICTURE = ROOT / "Assets" / "kia_ev6_gt_pixel.png"
# Rim ovals in the picture: center x, y and radii (measured against the original art).
WHEELS = [(937, 618, 98, 126), (1626, 614, 78, 112)]
PIXEL = 2            # the picture's chunky pixel size
N = 1200             # flat drawing size
R = 590              # rim radius in the flat drawing

BARREL = (16, 17, 19)
DISC = (78, 80, 82)
DISC_SHADE = (36, 38, 40)
HAT = (36, 38, 40)
LIME = (196, 234, 54)
LIME_SHADE = (128, 160, 30)
SPOKE = (14, 15, 17)
SPOKE_SHEEN = (34, 36, 39)
SILVER = (226, 228, 229)
SILVER_SHADE = (150, 153, 155)
LIP = (214, 217, 218)
LIP_SHADE = (120, 123, 125)
HUB = (10, 11, 12)
NUT = (92, 95, 97)

SPOKES = 5
FIRST = -90          # direction of the first spoke, degrees (0 = right, 90 = down)
KINK = -34           # how far each spoke's tip turns at the kink, degrees
CALIPER = 152        # the caliper's position, degrees (front of the wheel: left in the picture)
CENTER = (N / 2, N / 2)


def P(r, deg):
    a = math.radians(deg)
    return (N / 2 + R * r * math.cos(a), N / 2 + R * r * math.sin(a))


def add(p, q):
    return (p[0] + q[0], p[1] + q[1])


def vec(deg, length=1.0):
    a = math.radians(deg)
    return (R * length * math.cos(a), R * length * math.sin(a))


def disc(draw, r, color):
    c = N / 2
    draw.ellipse((c - R * r, c - R * r, c + R * r, c + R * r), fill=color)


def sector(draw, r_in, r_out, a0, a1, color, steps=24):
    pts = [P(r_out, a0 + (a1 - a0) * i / steps) for i in range(steps + 1)]
    pts += [P(r_in, a1 - (a1 - a0) * i / steps) for i in range(steps + 1)]
    draw.polygon(pts, fill=color)


# One spoke as (radius, degrees from the spoke's direction) outlines: a wedge that flares (counterclockwise) toward
# the rim. Its front is a machined silver V-shaped frame around a recessed black triangle; along the trailing edge is
# the spoke's dark side wall, seen at an angle. The leading edge kinks near the rim.
SPOKE_OUTLINE = [(0.16, -9), (0.62, -14), (0.93, -34), (0.93, 14), (0.16, 9)]
SIDE_WALL = [(0.16, 4), (0.93, 7), (0.93, 14), (0.16, 9)]
POCKET = [(0.33, -3), (0.62, -8), (0.91, -24), (0.91, -3), (0.33, 0)]


def spoke(draw, deg):
    """One spoke: silver V frame, black recessed triangle, dark side wall."""
    def pts(outline):
        return [P(r, deg + off) for r, off in outline]
    draw.polygon(pts(SPOKE_OUTLINE), fill=SILVER)
    # The frame darkens toward the hub, where it turns away from the light.
    draw.polygon(pts([(0.16, -9), (0.30, -10), (0.30, 4), (0.16, 4)]), fill=SILVER_SHADE)
    draw.polygon(pts(SIDE_WALL), fill=SPOKE_SHEEN)
    draw.polygon(pts(POCKET), fill=SPOKE)


def flat_wheel():
    img = Image.new("RGB", (N, N), BARREL)
    draw = ImageDraw.Draw(img)
    # Brake disc, lit at the top, with its darker hat.
    disc(draw, 0.86, DISC_SHADE)
    sector(draw, 0.36, 0.86, 200, 320, DISC)
    disc(draw, 0.34, HAT)
    # Caliper: a big block at the front of the wheel.
    sector(draw, 0.48, 0.90, CALIPER - 26, CALIPER + 26, LIME)
    sector(draw, 0.48, 0.58, CALIPER - 26, CALIPER + 26, LIME_SHADE)
    for k in range(SPOKES):
        spoke(draw, FIRST + k * 360 / SPOKES)
    # The machined lip, lit from the upper left, with a dark line where it meets the spokes.
    c = N / 2
    lip = Image.new("L", (N, N), 0)
    ImageDraw.Draw(lip).ellipse((c - R, c - R, c + R, c + R), fill=255)
    ImageDraw.Draw(lip).ellipse((c - R * 0.905, c - R * 0.905, c + R * 0.905, c + R * 0.905), fill=0)
    yy, xx = np.mgrid[0:N, 0:N]
    t = np.clip(0.55 + 0.45 * (-(xx - c) * 0.45 - (yy - c) * 0.9) / R, 0, 1)[..., None]
    lip_color = (np.array(LIP_SHADE) + (np.array(LIP) - np.array(LIP_SHADE)) * t).astype(np.uint8)
    img = Image.composite(Image.fromarray(lip_color), img, lip)
    draw = ImageDraw.Draw(img)
    draw.ellipse((c - R * 0.905, c - R * 0.905, c + R * 0.905, c + R * 0.905), outline=(6, 6, 7), width=int(R * 0.03))
    # Hub: black cap and lug nuts.
    disc(draw, 0.20, HUB)
    for k in range(5):
        x, y = P(0.135, FIRST + 36 + k * 72)
        draw.ellipse((x - R * 0.03, y - R * 0.03, x + R * 0.03, y + R * 0.03), fill=NUT)
    disc(draw, 0.075, (22, 23, 25))
    mask = Image.new("L", (N, N), 0)
    ImageDraw.Draw(mask).ellipse((c - R, c - R, c + R, c + R), fill=255)
    return img, mask


def draw_wheels(picture: Image.Image) -> Image.Image:
    out = picture.convert("RGB").copy()
    flat, mask = flat_wheel()
    for cx, cy, rx, ry in WHEELS:
        # Fit the flat wheel to its oval at half size, then scale up for the chunky pixels.
        w, h = round(2 * rx / PIXEL), round(2 * ry / PIXEL)
        small = flat.crop((N / 2 - R, N / 2 - R, N / 2 + R, N / 2 + R)).resize((w, h), Image.LANCZOS)
        small_mask = mask.crop((N / 2 - R, N / 2 - R, N / 2 + R, N / 2 + R)).resize((w, h), Image.LANCZOS)
        small = small.resize((w * PIXEL, h * PIXEL), Image.NEAREST)
        small_mask = small_mask.resize((w * PIXEL, h * PIXEL), Image.NEAREST)
        out.paste(small, (round(cx - w * PIXEL / 2), round(cy - h * PIXEL / 2)), small_mask)
    return out


# From a straight-on wheel photo: crop the face, reduce it to a few flat colors (pixel-art style), fit it to each
# wheel's oval. Keeps the real spoke shapes and the caliper peeking out on both sides of a spoke. The hub logo is
# blacked out. Photo center and rim radius are in the photo's pixels.
PALETTE = np.array([(10, 10, 12), (34, 35, 38), (70, 72, 75), (128, 128, 128), (176, 176, 176), (222, 224, 225),
                    (196, 234, 54), (132, 164, 34)], dtype=np.float32)


def photo_wheel(path, center, radius, size):
    """The photo's wheel at `size` (w, h) as flat-color pixels, and its round mask."""
    from PIL import ImageFilter
    photo = Image.open(path).convert("RGB")
    cx, cy = center
    face = photo.crop((cx - radius, cy - radius, cx + radius, cy + radius)).filter(ImageFilter.MedianFilter(9))
    small = np.asarray(face.resize(size, Image.LANCZOS), dtype=np.float32)
    # Nearest palette color; lime needs a clearly green pixel, so a bright gray never turns lime.
    # Grays by brightness; the caliper by hue (clearly green, even in shade), lit or shaded lime by brightness.
    gray = PALETTE[:6]
    dist = ((small[:, :, None, :] - gray[None, None]) ** 2).sum(-1)
    out = gray[dist.argmin(-1)]
    r, g, b = small[..., 0], small[..., 1], small[..., 2]
    green = (g > r + 10) & (g > b + 22) & (g > 48)
    out[green & (g > 140)] = PALETTE[6]
    out[green & (g <= 140)] = PALETTE[7]
    h, w = out.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w]
    u, v = (xx + 0.5) / w * 2 - 1, (yy + 0.5) / h * 2 - 1
    out[np.hypot(u, v) < 0.13] = PALETTE[0]          # hub cap without the logo
    mask = (np.hypot(u, v) <= 1.0).astype(np.uint8) * 255
    return Image.fromarray(out.astype(np.uint8)), Image.fromarray(mask)


def draw_from_photo(picture: Image.Image, path, center, radius) -> Image.Image:
    out = picture.convert("RGB").copy()
    for cx, cy, rx, ry in WHEELS:
        w, h = round(2 * rx / PIXEL), round(2 * ry / PIXEL)
        face, mask = photo_wheel(path, center, radius, (w, h))
        face = face.resize((w * PIXEL, h * PIXEL), Image.NEAREST)
        mask = mask.resize((w * PIXEL, h * PIXEL), Image.NEAREST)
        out.paste(face, (round(cx - w * PIXEL / 2), round(cy - h * PIXEL / 2)), mask)
    return out


if __name__ == "__main__":
    target = PICTURE if "--apply" in sys.argv else Path(next(a for a in sys.argv[1:] if not a.startswith("--")))
    draw_wheels(Image.open(PICTURE)).save(target)
    print(f"Wrote {target}")
