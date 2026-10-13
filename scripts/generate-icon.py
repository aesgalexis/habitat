"""Generate HABITAT's reproducible feather application icon."""

from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src" / "Habitat.App" / "Assets" / "AppIcon.ico"


def render(size: int = 1024) -> Image.Image:
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    scale = size / 256
    point = lambda x, y: (round(x * scale), round(y * scale))

    draw.rounded_rectangle(
        (0, 0, size - 1, size - 1),
        radius=round(58 * scale),
        fill=(102, 116, 244, 255),
    )

    feather = [
        point(48, 216), point(61, 172), point(82, 119), point(116, 70),
        point(164, 34), point(213, 28), point(209, 75), point(183, 124),
        point(145, 164), point(98, 194),
    ]
    draw.polygon(feather, fill=(246, 248, 255, 255))

    ink = (102, 116, 244, 255)
    width = max(3, round(7 * scale))
    draw.line([point(48, 220), point(202, 42)], fill=(246, 248, 255, 255), width=width)
    for start, end in [
        ((70, 174), (137, 151)), ((80, 148), (153, 123)),
        ((96, 117), (171, 91)), ((117, 86), (188, 62)),
        ((101, 190), (94, 146)), ((135, 165), (123, 113)),
        ((166, 132), (151, 83)), ((191, 92), (174, 55)),
    ]:
        draw.line([point(*start), point(*end)], fill=ink, width=width)

    return image


if __name__ == "__main__":
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    render().save(
        OUTPUT,
        format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )
    print(OUTPUT)
