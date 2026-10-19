"""Render HABITAT's existing dot identity and the bundled privacy page."""
from pathlib import Path
import html
import re
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
EXTENSION = ROOT / "extension"
STORE = ROOT / "packaging" / "chrome-store"


def inline(text):
    text = html.escape(text)
    text = re.sub(r"\[([^\]]+)\]\((https://[^)]+)\)", r'<a href="\2" target="_blank" rel="noopener">\1</a>', text)
    text = re.sub(r"\*\*([^*]+)\*\*", r"<strong>\1</strong>", text)
    return re.sub(r"`([^`]+)`", r"<code>\1</code>", text)


def privacy_html():
    lines = (ROOT / "docs" / "EXTENSION_PRIVACY.md").read_text(encoding="utf-8").splitlines()
    blocks = []
    in_list = False
    for line in lines:
        if not line:
            if in_list:
                blocks.append("</ul>")
                in_list = False
            continue
        if line.startswith("- "):
            if not in_list:
                blocks.append("<ul>")
                in_list = True
            blocks.append("<li>" + inline(line[2:]) + "</li>")
        elif line.startswith("## "):
            blocks.append("<h2>" + inline(line[3:]) + "</h2>")
        elif line.startswith("# "):
            blocks.append("<h1>" + inline(line[2:]) + "</h1>")
        else:
            blocks.append("<p>" + inline(line) + "</p>")
    if in_list:
        blocks.append("</ul>")
    return '''<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>HABITAT Browser Bridge privacy policy</title><link rel="stylesheet" href="welcome.css">
<style>h1{font-size:36px}h2{margin-top:30px}main>p{line-height:1.65;color:#cbd0e1}main{max-width:840px}</style></head>
<body><main>''' + "\n".join(blocks) + '<footer><a href="welcome.html">Connection settings</a></footer></main></body></html>\n'


def main():
    (EXTENSION / "icons").mkdir(exist_ok=True)
    STORE.mkdir(parents=True, exist_ok=True)
    for size in (16, 32, 48, 128):
        scale = 4
        icon = Image.new("RGBA", (size * scale, size * scale), (137, 149, 255, 0))
        draw = ImageDraw.Draw(icon)
        inset = round(size * scale / 16)
        draw.ellipse((inset, inset, size * scale - inset - 1, size * scale - inset - 1), fill="#8995ff")
        icon.resize((size, size), Image.Resampling.LANCZOS).save(EXTENSION / "icons" / f"icon{size}.png")
    promo = Image.new("RGB", (880, 560), "#10131c")
    draw = ImageDraw.Draw(promo)
    draw.rounded_rectangle((80, 190, 300, 390), radius=24, outline="#626b92", width=4, fill="#191e2d")
    draw.rounded_rectangle((580, 170, 800, 370), radius=24, outline="#626b92", width=4, fill="#191e2d")
    draw.line((300, 290, 580, 270), fill="#8995ff", width=8)
    draw.ellipse((338, 178, 542, 382), fill="#8995ff")
    draw.ellipse((383, 223, 497, 337), fill="#10131c")
    for row in range(3):
        y = 235 + row * 42
        draw.rounded_rectangle((116, y, 260 - row * 22, y + 12), radius=6, fill="#aab4d3")
        draw.rounded_rectangle((620, y - 20, 760 - row * 22, y - 8), radius=6, fill="#aab4d3")
    promo.resize((440, 280), Image.Resampling.LANCZOS).save(STORE / "promo-440x280.png")
    (EXTENSION / "privacy.html").write_text(privacy_html(), encoding="utf-8", newline="\n")
    # Store screenshots require 24-bit PNG. Preserve the native screenshot
    # pixels; only remove the unused alpha channel from the submission copy.
    for screenshot in STORE.glob("*-1280x800.png"):
        with Image.open(screenshot) as capture:
            opaque = capture.convert("RGB")
        opaque.save(screenshot)
    print("Chrome icons, promotional image and bundled privacy page generated.")


if __name__ == "__main__":
    main()
