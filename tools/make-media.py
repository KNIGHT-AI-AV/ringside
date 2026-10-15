# Builds docs/images/demo.gif from frames rendered by `Ringside.exe --render-demo <dir>`, and
# docs/images/social-card.png (1280x640) for GitHub's social preview.
# Usage: python make-media.py <frames dir> <repo root> "<tagline>"
import glob, os, sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

frames_dir, repo, tagline = sys.argv[1], sys.argv[2], sys.argv[3]
images = os.path.join(repo, "docs", "images")
BG_TOP, BG_BOTTOM = (24, 24, 30), (12, 12, 15)


def backdrop(w, h):
    bg = Image.new("RGB", (w, h))
    px = bg.load()
    for y in range(h):
        t = y / max(h - 1, 1)
        c = tuple(round(BG_TOP[i] + (BG_BOTTOM[i] - BG_TOP[i]) * t) for i in range(3))
        for x in range(w):
            px[x, y] = c
    # a faint warm and cool glow behind the card, echoing the two ring colours
    glow = Image.new("RGB", (w, h), (0, 0, 0))
    d = ImageDraw.Draw(glow)
    d.ellipse((w * 0.08, h * 0.05, w * 0.55, h * 0.95), fill=(70, 34, 22))
    d.ellipse((w * 0.45, h * 0.05, w * 0.92, h * 0.95), fill=(10, 52, 42))
    glow = glow.filter(ImageFilter.GaussianBlur(min(w, h) * 0.22))
    return Image.composite(Image.blend(bg, glow, 0.55), bg, Image.new("L", (w, h), 160))


# --- demo GIF ---
paths = sorted(glob.glob(os.path.join(frames_dir, "f*.png")))
frames = [Image.open(p).convert("RGBA") for p in paths]
W, H = 760, 470
base = backdrop(W, H)
out, durations = [], []
for i, f in enumerate(frames):
    canvas = base.copy()
    canvas.paste(f, ((W - f.width) // 2, (H - f.height) // 2), f)
    out.append(canvas.convert("P", palette=Image.ADAPTIVE, colors=255))
    last_fill, last_rise = 20, 36
    durations.append(1600 if i == last_fill else 1800 if i == last_rise else 2200 if i == len(frames) - 1 else 45)
gif = os.path.join(images, "demo.gif")
out[0].save(gif, save_all=True, append_images=out[1:], duration=durations, loop=0, optimize=True, disposal=2)
print(f"demo.gif: {len(out)} frames, {os.path.getsize(gif) // 1024} KB")

# --- social card ---
def font(names, size):
    roots = [os.path.join(os.environ["LOCALAPPDATA"], "Microsoft", "Windows", "Fonts"), r"C:\Windows\Fonts"]
    for n in names:
        for r in roots:
            p = os.path.join(r, n)
            if os.path.exists(p):
                return ImageFont.truetype(p, size)
    return ImageFont.load_default()

card = backdrop(1280, 640).convert("RGBA")
logo = Image.open(os.path.join(repo, "assets", "ringside.png")).convert("RGBA").resize((120, 120), Image.LANCZOS)
card.paste(logo, (90, 150), logo)
d = ImageDraw.Draw(card)
d.text((90, 300), "Ringside", font=font(["Inter-Bold.ttf", "segoeuib.ttf"], 84), fill=(244, 244, 246))
words, lines, line = tagline.split(), [], ""
tf = font(["Inter-Medium.ttf", "segoeui.ttf"], 34)
for w_ in words:
    test = (line + " " + w_).strip()
    if d.textlength(test, font=tf) > 560 and line:
        lines.append(line)
        line = w_
    else:
        line = test
lines.append(line)
for i, l in enumerate(lines):
    d.text((92, 410 + i * 46), l, font=tf, fill=(166, 166, 176))
widget = Image.open(os.path.join(images, "widget.png")).convert("RGBA")
scale = 560 / widget.width
widget = widget.resize((560, round(widget.height * scale)), Image.LANCZOS)
card.paste(widget, (1280 - 560 - 60, (640 - widget.height) // 2), widget)
card.convert("RGB").save(os.path.join(images, "social-card.png"), optimize=True)
print("social-card.png written")
