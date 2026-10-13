"""Generate HABITAT's reproducible status-dot application icons."""

from pathlib import Path

from PIL import Image, ImageColor, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src" / "Habitat.App" / "Assets"
ICONS = {
    "AppIcon.ico": "#8995FF",
    "HabitatDot.ico": "#8995FF",
    "StatusActive.ico": "#8995FF",
    "StatusSuccess.ico": "#55C79C",
    "StatusWarning.ico": "#E2A06B",
    "StatusDanger.ico": "#ED7181",
}
SIZES = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
PNG_ICONS = {
    "LockScreenLogo.scale-200.png": 48,
    "Square150x150Logo.scale-200.png": 300,
    "Square44x44Logo.scale-200.png": 88,
    "Square44x44Logo.targetsize-24_altform-unplated.png": 24,
    "Square44x44Logo.targetsize-48_altform-lightunplated.png": 48,
    "StoreLogo.png": 50,
}


def render(color: str, size: int = 1024) -> Image.Image:
    rgb = ImageColor.getrgb(color)
    # Keep the hidden RGB channels the same color as the dot. Some Windows
    # icon scaling paths otherwise reveal a dark fringe around transparent art.
    image = Image.new("RGBA", (size, size), (*rgb, 0))
    draw = ImageDraw.Draw(image)
    inset = round(size * 0.30)
    draw.ellipse((inset, inset, size - inset, size - inset), fill=(*rgb, 255))

    return image


if __name__ == "__main__":
    ASSETS.mkdir(parents=True, exist_ok=True)
    for filename, color in ICONS.items():
        output = ASSETS / filename
        render(color).save(output, format="ICO", sizes=SIZES)
        print(output)
    for filename, size in PNG_ICONS.items():
        output = ASSETS / filename
        render("#8995FF").resize((size, size), Image.Resampling.LANCZOS).save(output, format="PNG")
        print(output)
