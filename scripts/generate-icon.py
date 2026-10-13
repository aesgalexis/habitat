"""Generate HABITAT's reproducible status-dot application icons."""

from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src" / "Habitat.App" / "Assets"
ICONS = {
    "AppIcon.ico": "#8995FF",
    "StatusActive.ico": "#8995FF",
    "StatusSuccess.ico": "#55C79C",
    "StatusWarning.ico": "#E2A06B",
    "StatusDanger.ico": "#ED7181",
}
SIZES = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]


def render(color: str, size: int = 1024) -> Image.Image:
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    inset = round(size * 0.205)
    draw.ellipse((inset, inset, size - inset, size - inset), fill=color)

    return image


if __name__ == "__main__":
    ASSETS.mkdir(parents=True, exist_ok=True)
    for filename, color in ICONS.items():
        output = ASSETS / filename
        render(color).save(output, format="ICO", sizes=SIZES)
        print(output)
