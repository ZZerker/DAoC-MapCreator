r"""Convert MapCreator PNG renders (z{id}.png) to DXT1 DDS maps for the TokaZerk UI.

Usage: python png_to_dds.py <render dir> <dds dir> [size] [--no-labels] [--uncompressed]
Writes <dds dir>\zNNN.dds, optionally downscaled to <size> x <size>.
Labels from zNNN.labels.json (written by MapCreator) are drawn after scaling, so text stays sharp at the final size.
"""
import json
import subprocess
import sys
import tempfile
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

FONTS = Path(r"C:\Windows\Fonts")

# DirectXTex texconv (github.com/microsoft/DirectXTex releases) encodes DXT1 with far less error than Pillow
TEXCONV = Path(__file__).parent / "bin" / "texconv.exe"


def save_dxt1(img, dds):
    if not TEXCONV.exists():
        img.save(dds, "DDS", pixel_format="DXT1")
        return
    with tempfile.TemporaryDirectory() as temp:
        png = Path(temp) / (dds.stem + ".png")
        img.save(png)
        subprocess.run([str(TEXCONV), "-nologo", "-y", "-f", "BC1_UNORM", "-m", "1", "-bc", "xu", "-o", str(dds.parent), str(png)], check=True, stdout=subprocess.DEVNULL)

# Muted colors that sit in the map instead of on top of it
TEXT = (238, 230, 207)
HALO = (16, 12, 8, 255)
REALM_TEXT = {1: (232, 168, 152), 2: (170, 198, 232), 3: (170, 214, 150)}
NEIGHBOR_TEXT = (228, 204, 140)
ICON_FILL = {"entrance": (70, 58, 44), "portal": (60, 70, 96), "boss": (150, 60, 40), "dock": (60, 80, 100)}

# Pixel sizes at 512; smaller maps scale down and drop labels with a higher priority number
STYLE = {
    "keep": ("tahomabd.ttf", 11),
    "place": ("tahomabd.ttf", 10),
    "neighbor": ("tahomabd.ttf", 11),
    "entrance": ("tahomabd.ttf", 10),
    "portal": ("tahomabd.ttf", 10),
    "boss": ("tahomabd.ttf", 10),
    "dock": ("tahomabd.ttf", 9),
}


def font(kind, size):
    name, points = STYLE.get(kind, ("tahomabd.ttf", 10))
    return ImageFont.truetype(str(FONTS / name), max(8, round(points * size / 512)))


def overlaps(box, boxes):
    return any(box[0] < b[2] and box[2] > b[0] and box[1] < b[3] and box[3] > b[1] for b in boxes)


def draw_icon(draw, kind, x, y, r):
    if kind in ("entrance", "dock"):
        draw.ellipse((x - r, y - r, x + r, y + r), fill=ICON_FILL[kind], outline=TEXT, width=max(1, r // 3))
    elif kind in ("portal", "boss"):
        draw.polygon([(x, y - r - 1), (x + r + 1, y), (x, y + r + 1), (x - r - 1, y)], fill=ICON_FILL[kind], outline=TEXT)


def draw_arrow(draw, edge, x, y, r, color):
    points = {
        "north": [(x, y - r), (x + r, y + r * 0.6), (x - r, y + r * 0.6)],
        "south": [(x, y + r), (x + r, y - r * 0.6), (x - r, y - r * 0.6)],
        "west": [(x - r, y), (x + r * 0.6, y - r), (x + r * 0.6, y + r)],
        "east": [(x + r, y), (x - r * 0.6, y - r), (x - r * 0.6, y + r)],
    }[edge]
    draw.polygon(points, fill=color, outline=HALO)


# Small text stays crisp only when drawn at its final pixel size with a hard outline
SUPERSAMPLE = 1


def draw_labels(img, labels):
    max_priority = 1 if img.size[0] < 512 else 2
    size = img.size[0] * SUPERSAMPLE
    stroke = 1
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    halo = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    halo_draw = ImageDraw.Draw(halo)
    boxes = []
    margin = max(4, size // 96)

    order = {"neighbor": 0, "keep": 1, "portal": 2, "entrance": 3, "boss": 4, "place": 5, "dock": 6}
    for label in sorted(labels, key=lambda l: (l["priority"], order.get(l["kind"], 9))):
        if label["priority"] > max_priority or label["kind"] == "tower":
            continue

        kind = label["kind"]
        f = font(kind, size)
        text = label["text"]
        x, y = label["x"] * size, label["y"] * size
        color = REALM_TEXT.get(label["realm"], TEXT) if kind == "keep" else NEIGHBOR_TEXT if kind == "neighbor" else TEXT

        if kind == "neighbor":
            edge = label["edge"]
            w, h = draw.textbbox((0, 0), text, font=f, stroke_width=stroke)[2:]
            arrow = round(h * 0.7)
            if edge in ("north", "south"):
                cx = min(max(x - (w + arrow) / 2, margin), size - w - arrow - margin)
                cy = margin if edge == "north" else size - h - margin
            else:
                cx = margin if edge == "west" else size - w - arrow - margin
                cy = min(max(y - h / 2, margin), size - h - margin)
            candidates = [(cx, cy)]
        else:
            w, h = draw.textbbox((0, 0), text, font=f, stroke_width=stroke)[2:]
            # Keeps are labeled clear of their walls, other points right next to their icon
            gap = size / 512 * (14 if kind == "keep" else 6)
            candidates = [(x - w / 2, y + gap), (x - w / 2, y - gap - h), (x + gap, y - h / 2), (x - gap - w, y - h / 2)]

        for cx, cy in candidates:
            box = (cx, cy, cx + w, cy + h)
            if box[0] < 0 or box[1] < 0 or box[2] > size or box[3] > size or overlaps(box, boxes):
                continue
            if kind == "neighbor":
                box = (cx, cy, cx + w + arrow, cy + h)
                text_x = cx + arrow if edge in ("north", "west") else cx
                arrow_x = cx if edge in ("north", "west") else cx + w
                draw_arrow(draw, edge, arrow_x + arrow / 2, cy + h / 2, arrow / 2 - 1, color)
                cx = text_x
            elif kind not in ("keep", "place"):
                draw_icon(draw, kind, x, y, max(2, round(3 * size / 512)))
            halo_draw.text((cx, cy), text, font=f, fill=HALO, stroke_width=stroke, stroke_fill=HALO)
            draw.text((cx, cy), text, font=f, fill=color)
            boxes.append(box)
            break

    labels_layer = Image.alpha_composite(halo, layer)
    return Image.alpha_composite(img.convert("RGBA"), labels_layer).convert("RGB")


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    labels_on = "--no-labels" not in sys.argv
    # DXT1 blurs fine text into 4x4 blocks; uncompressed is 6 times larger
    uncompressed = "--uncompressed" in sys.argv
    source = Path(args[0])
    target = Path(args[1])
    size = int(args[2]) if len(args) > 2 else None
    target.mkdir(parents=True, exist_ok=True)

    pngs = sorted(source.glob("z*.png"))
    if not pngs:
        sys.exit(f"no z*.png in {source}")

    for png in pngs:
        with Image.open(png) as img:
            img = img.convert("RGB")
            if size and img.size != (size, size):
                img = img.resize((size, size), Image.LANCZOS)
            labels_file = png.with_suffix(".labels.json")
            if labels_on and labels_file.exists():
                img = draw_labels(img, json.loads(labels_file.read_text(encoding="utf-8"))["labels"])
            dds = target / (png.stem + ".dds")
            if uncompressed:
                img.save(dds, "DDS")
            else:
                save_dxt1(img, dds)
        print(f"{dds}  {img.size[0]}x{img.size[1]}  {dds.stat().st_size} bytes")


if __name__ == "__main__":
    main()
