"""Builds the README media from the headless renders (tools/Ringside.Render).

  docs/images/demo.gif         the animated tour, from renders/frames/f*.png
  docs/images/social-card.png  1280x640 for GitHub's social preview
  docs/images/*.png            the stills, copied as rendered (2x, shown at half width)

Usage: python -I make-media.py <renders dir> <repo root> "<tagline>"
"""
import glob
import os
import shutil
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

renders, repo, tagline = sys.argv[1], sys.argv[2], sys.argv[3]
images = os.path.join(repo, "docs", "images")
os.makedirs(images, exist_ok=True)

STILLS = [
    "views", "used-vs-left", "colors", "split", "widget-card", "widget-bars-left", "widget-alert",
    "widget-pill", "widget-orb", "toast", "quick-panel",
    "onboarding-1", "onboarding-3", "onboarding-4", "onboarding-5",
    "settings-look", "settings-colors", "settings-alerts",
    "live-limits", "onboarding-2", "onboarding-8", "settings-claudecode",
]
for name in STILLS:
    src = os.path.join(renders, name + ".png")
    if os.path.exists(src):
        shutil.copyfile(src, os.path.join(images, name + ".png"))

# --- demo GIF: identical neighbours merge into one longer frame -----------------------
paths = sorted(glob.glob(os.path.join(renders, "frames", "f*.png")))
frames, durations = [], []
STEP, HOLD = 55, 190  # an animated step, and each repeat of a held frame
for p in paths:
    im = Image.open(p).convert("RGB")
    if frames and ImageChops.difference(im, frames[-1]).getbbox() is None:
        durations[-1] += HOLD
        continue
    frames.append(im)
    durations.append(STEP)
if frames:
    W = 760
    scaled = [f.resize((W, round(f.height * W / f.width)), Image.LANCZOS) for f in frames]
    pal = [f.convert("P", palette=Image.ADAPTIVE, colors=255) for f in scaled]
    durations[-1] = max(durations[-1], 2200)
    gif = os.path.join(images, "demo.gif")
    pal[0].save(gif, save_all=True, append_images=pal[1:], duration=durations, loop=0, optimize=True, disposal=2)
    print(f"demo.gif: {len(pal)} frames, {sum(durations) / 1000:.1f} s, {os.path.getsize(gif) // 1024} KB")


# --- social card: drawn by the renderer from the real controls -------------------------
src = os.path.join(renders, "social-card.png")
if os.path.exists(src):
    Image.open(src).convert("RGB").save(os.path.join(images, "social-card.png"), optimize=True)
    print("social-card.png")
