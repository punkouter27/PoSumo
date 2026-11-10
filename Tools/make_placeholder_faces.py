"""Fake a right-facing PROFILE head for each placeholder fighter from a front-on photo.

    python Tools/make_placeholder_faces.py [--src DIR] [--out DIR] [--sheet PNG]

Writes ONE image per fighter, `<Name>_Neutral.png`, into Assets/Art/Faces/.
Every other expression (Happy / Angry / Hurt / Insult, levels 1-5) is derived at
runtime from that neutral by `Systems_FaceMood` - tint, squash and head tilt -
unless a real image exists at `<Name>_<Mood>_<Level>.png`, which always wins.
So real art replaces placeholders one slot at a time with no code change.

THIS IS A FAKE, chosen knowingly (user, 2026-11-10). A true side view of a
person cannot be computed from a front photo - that needs a generative model.
What this does instead:

  1. crop the head out of the photo (boxes picked by eye, below);
  2. keep the subject's RIGHT half of the face (image-left, up to just past the
     nose), which already has the nose on its right edge - the direction every
     fighter's art faces;
  3. paint a back-of-skull behind it in the hair colour sampled off the crown,
     plus an ear in the cheek colour;
  4. cut the whole thing out with a profile silhouette (skull + face + neck).

At gameplay size (~100 px on a phone) it reads as a head facing right. Up close
it reads as what it is.

Crop boxes are in the coordinates of the photo after EXIF rotation, scaled so its
LONGER side is 300 px - what the contact sheet shows - so they survive a
re-export of the source at a different resolution.
"""
import argparse
import os
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageOps, ImageStat

DEFAULT_SRC = r"G:\My Drive\VAULT\_2DGFX\2DGFX_REFS"
DEFAULT_OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Art", "Faces")

THUMB = 300
WORK = 512        # crop is resampled to this square before the cut
OUT_H = 256       # final height; width follows the profile's aspect

# name -> (source file, centre x, centre y, side) in 300-px-thumbnail space.
# The centre is the NOSE BRIDGE, not the middle of the head: the mirror line
# is the crop's centre column, so it has to run down the nose.
FACES = {
    "Tong":    ("FACE_Tong.png",    163, 128, 190),
    "Alan":    ("FACE_Alan.png",    152,  78, 150),
    "Josh":    ("FACE_Josh.png",    127, 150, 280),
    "Maya":    ("FACE_Maya.JPG",    108, 150, 220),
    "Baily":   ("FACE_Baily.png",   190,  80, 130),
    "Porto":   ("FACE_Porto.jpg",   113, 133, 140),
    "Brendan": ("FACE_Brendan.png", 150, 113, 190),
    "Decoy":   ("FACE_Decoy.png",   186,  75,  80),
    "Sokol":   ("FACE_Sokol.webp",  120, 145, 185),
    "Dupee":   ("FACE_Dupee.png",   150, 135, 250),
    "Manzi":   ("FACE_Manzi.jpg",    98, 122, 210),
    "Velez":   ("FACE_Velez.jpg",   150, 130, 190),
}


def load(path):
    return ImageOps.exif_transpose(Image.open(path)).convert("RGBA")


def crop_head(image, cx, cy, side):
    scale = max(image.width, image.height) / float(THUMB)
    half = side * scale / 2.0
    box = (int(cx * scale - half), int(cy * scale - half),
           int(cx * scale + half), int(cy * scale + half))
    # crop() pads outside the photo with transparent black; the silhouette hides it.
    return image.crop(box).resize((WORK, WORK), Image.LANCZOS)


def mean_colour(image, box):
    region = image.crop(box).convert("RGB")
    return tuple(int(v) for v in ImageStat.Stat(region).median)


def fake_profile(head, seed):
    w = h = WORK

    # The subject's RIGHT half of the face is image-LEFT in a front photo, and it
    # already has the nose on its right edge - exactly the direction every
    # fighter's art faces. 0.58 keeps the nose tip and a sliver past it.
    half_w = int(w * 0.58)
    half = head.crop((0, 0, half_w, h))

    # How far the back of the skull extends behind the cheek line. The cheek
    # outline sits ~0.12 w in from the crop edge, so the skull overlaps it.
    skull_w = int(w * 0.34)
    cw = skull_w + half_w
    canvas = Image.new("RGBA", (cw, h), (0, 0, 0, 0))

    # Sampled INSIDE the face, not off the crop's corners - those are the
    # photo's background (a kitchen, a bar, a police backdrop).
    hair = mean_colour(head, (int(w * 0.30), int(h * 0.10), int(w * 0.46), int(h * 0.17)))
    skin = mean_colour(head, (int(w * 0.24), int(h * 0.50), int(w * 0.34), int(h * 0.62)))
    dark = tuple(max(0, c - 45) for c in skin)

    # Neck first (skin), under everything.
    draw = ImageDraw.Draw(canvas)
    draw.rounded_rectangle((cw * 0.33, h * 0.66, cw * 0.70, h * 1.05), radius=int(w * 0.06), fill=skin + (255,))

    # Back of the skull: hair colour with grain so it is not a flat plastic dome.
    skull_mask = Image.new("L", (cw, h), 0)
    ImageDraw.Draw(skull_mask).ellipse((cw * 0.06, h * 0.07, cw * 0.76, h * 0.70), fill=255)
    skull_mask = skull_mask.filter(ImageFilter.GaussianBlur(w * 0.008))
    grain = Image.effect_noise((cw, h), 26).convert("L").filter(ImageFilter.GaussianBlur(1.0))
    skull = Image.merge("RGBA", [Image.new("L", (cw, h), c) for c in hair] + [skull_mask])
    shade = Image.new("RGBA", (cw, h), tuple(max(0, c - 28) for c in hair) + (0,))
    shade.putalpha(ImageChops.multiply(grain, skull_mask).point(lambda v: v // 3))
    canvas.alpha_composite(skull)
    canvas.alpha_composite(shade)

    # The half-face, masked to the face itself so none of the background survives.
    face_mask = Image.new("L", (half_w, h), 0)
    ImageDraw.Draw(face_mask).ellipse((w * 0.11, h * 0.06, half_w - w * 0.01, h * 0.95), fill=255)
    face_mask = face_mask.filter(ImageFilter.GaussianBlur(w * 0.018))
    face = half.copy()
    face.putalpha(ImageChops.multiply(half.getchannel("A"), face_mask))
    canvas.alpha_composite(face, (skull_w, 0))

    # Ear, where the cheek line meets the skull.
    draw = ImageDraw.Draw(canvas)
    ex, ey = skull_w + w * 0.10, h * 0.50
    erx, ery = w * 0.050, h * 0.080
    draw.ellipse((ex - erx, ey - ery, ex + erx, ey + ery), fill=skin + (255,), outline=dark + (255,), width=4)
    draw.arc((ex - erx * 0.55, ey - ery * 0.6, ex + erx * 0.45, ey + ery * 0.6), 100, 300, fill=dark + (255,), width=3)

    # Trim to the drawn pixels so the sprite fits the head hitbox on real content.
    bbox = canvas.getchannel("A").point(lambda v: 255 if v > 8 else 0).getbbox()
    if bbox:
        canvas = canvas.crop(bbox)
    out_w = int(canvas.width * OUT_H / canvas.height)
    return canvas.resize((out_w, OUT_H), Image.LANCZOS)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--src", default=DEFAULT_SRC)
    parser.add_argument("--out", default=DEFAULT_OUT)
    parser.add_argument("--sheet", default=None, help="also write a contact sheet of every result")
    parser.add_argument("--only", default=None, help="comma-separated names")
    args = parser.parse_args()

    os.makedirs(args.out, exist_ok=True)
    wanted = set(args.only.split(",")) if args.only else None
    done = []
    for index, (name, (file_name, cx, cy, side)) in enumerate(FACES.items()):
        if wanted and name not in wanted:
            continue
        path = os.path.join(args.src, file_name)
        if not os.path.exists(path):
            print(f"[SKIP] {name}: {path} not found", file=sys.stderr)
            continue
        profile = fake_profile(crop_head(load(path), cx, cy, side), seed=index)
        profile.save(os.path.join(args.out, f"{name}_Neutral.png"))
        done.append((name, profile))
        print(f"[FACE] {name}: {name}_Neutral.png from {file_name}")

    if args.sheet and done:
        cell = 200
        cols = 6
        rows = (len(done) + cols - 1) // cols
        sheet = Image.new("RGBA", (cell * cols, cell * rows), (60, 150, 60, 255))
        for i, (name, image) in enumerate(done):
            thumb = image.copy()
            thumb.thumbnail((cell - 8, cell - 20))
            x, y = (i % cols) * cell, (i // cols) * cell
            sheet.alpha_composite(thumb, (x + 4, y + 16))
            ImageDraw.Draw(sheet).text((x + 4, y + 2), name, fill=(255, 255, 255, 255))
        sheet.save(args.sheet)
    print(f"FACE RESULT: {len(done)} placeholder profiles written to {os.path.abspath(args.out)}")


if __name__ == "__main__":
    main()
