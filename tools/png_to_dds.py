r"""Convert MapCreator PNG renders (z{id}.png) to DXT1 DDS maps for the TokaZerk UI.

Usage: python png_to_dds.py <render dir> <dds dir> [size]
Writes <dds dir>\zNNN.dds, optionally downscaled to <size> x <size>.
"""
import sys
from pathlib import Path
from PIL import Image


def main():
    source = Path(sys.argv[1])
    target = Path(sys.argv[2])
    size = int(sys.argv[3]) if len(sys.argv) > 3 else None
    target.mkdir(parents=True, exist_ok=True)

    pngs = sorted(source.glob("z*.png"))
    if not pngs:
        sys.exit(f"no z*.png in {source}")

    for png in pngs:
        with Image.open(png) as img:
            img = img.convert("RGB")
            if size and img.size != (size, size):
                img = img.resize((size, size), Image.LANCZOS)
            dds = target / (png.stem + ".dds")
            img.save(dds, "DDS", pixel_format="DXT1")
        print(f"{dds}  {img.size[0]}x{img.size[1]}  {dds.stat().st_size} bytes")


if __name__ == "__main__":
    main()
