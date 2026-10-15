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
]
for name in STILLS:
    src = os.path.join(renders, name + ".png")
    if os.path.exists(src):
        shutil.copyfile(src, os.path.join(images, name + ".png"))

# --- demo GIF: identical neighbours merge into one longer frame -----------------------
paths = sorted(glob.glob(os.path.join(renders, "frames", "f*.png")))
frames, durations = [], []
STEP = 55
for p in paths:
    im = Image.open(p).convert("RGB")
    if frames and ImageChops.difference(im, frames[-1]).getbbox() is None:
        durations[-1] += STEP
        continue
    frames.append(im)
    durations.append(STEP)
if frames:
    W = 760
    scaled = [f.resize((W, round(f.height * W / f.width)), Image.LANCZOS) for f in frames]
    pal = [f.convert("P", palette=Image.ADAPTIVE, colors=255) for f in scaled]
    durations[-1] = max(durations[-1], 1600)
    gif = os.path.join(images, "demo.gif")
    pal[0].save(gif, save_all=True, append_images=pal[1:], duration=durations, loop=0, optimize=True, disposal=2)
    print(f"demo.gif: {len(pal)} frames, {sum(durations) / 1000:.1f} s, {os.path.getsize(gif) // 1024} KB")


# --- social card ----------------------------------------------------------------------
def font(names, size):
    roots = [os.path.join(os.environ.get("LOCALAPPDATA", ""), "Microsoft", "Windows", "Fonts"), r"C:\Windows\Fonts"]
    for n in names:
        for r in roots:
            p = os.path.join(r, n)
            if os.path.exists(p):
                return ImageFont.truetype(p, size)
    return ImageFont.load_default()


def backdrop(w, h):
    top, bottom = (42, 37, 64), (14, 16, 20)
    bg = Image.new("RGB", (w, h))
    d = ImageDraw.Draw(bg)
    for y in range(h):
        t = y / max(h - 1, 1)
        d.line([(0, y), (w, y)], fill=tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(3)))
    glow = Image.new("RGB", (w, h), (0, 0, 0))
    g = ImageDraw.Draw(glow)
    g.ellipse((w * 0.52, h * 0.0, w * 1.05, h * 1.0), fill=(70, 52, 140))
    glow = glow.filter(ImageFilter.GaussianBlur(min(w, h) * 0.25))
    return Image.blend(bg, ImageChops.add(bg, glow), 0.6)


card = backdrop(1280, 640).convert("RGBA")
logo = Image.open(os.path.join(repo, "assets", "ringside.png")).convert("RGBA").resize((104, 104), Image.LANCZOS)
card.paste(logo, (84, 130), logo)
d = ImageDraw.Draw(card)
d.text((80, 262), "Ringside", font=font(["Inter-Bold.ttf", "segoeuib.ttf"], 82), fill=(244, 244, 246))
words, lines, line = tagline.split(), [], ""
f = font(["Inter-Regular.ttf", "segoeui.ttf"], 31)
for w_ in words:
    trial = (line + " " + w_).strip()
    if d.textlength(trial, font=f) > 520:
        lines.append(line)
        line = w_
    else:
        line = trial
lines.append(line)
for i, l in enumerate(lines):
    d.text((84, 380 + i * 44), l, font=f, fill=(176, 176, 188))
card_src, orb_src = os.path.join(renders, "card-clear.png"), os.path.join(renders, "orb-clear.png")
if os.path.exists(card_src) and os.path.exists(orb_src):
    shot = Image.open(card_src).convert("RGBA")
    shot = shot.resize((round(shot.width * 330 / shot.height), 330), Image.LANCZOS)
    card.alpha_composite(shot, (1280 - shot.width - 40, 60))
    orb = Image.open(orb_src).convert("RGBA")
    orb = orb.resize((300, round(orb.height * 300 / orb.width)), Image.LANCZOS)
    card.alpha_composite(orb, (1280 - shot.width - 120, 640 - orb.height - 20))
card.convert("RGB").save(os.path.join(images, "social-card.png"), optimize=True)
print("social-card.png")
