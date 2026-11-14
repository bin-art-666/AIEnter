"""Encode the supplied transparent artwork as PNG and multi-resolution ICO.
Requires Pillow. Run from any working directory; source and outputs stay in repo.
"""
from pathlib import Path
from PIL import Image

assets = Path(__file__).resolve().parents[1] / "src" / "AIEnter" / "Assets"
with Image.open(assets / "source-icon.webp") as source:
    image = source.convert("RGBA")
    image.save(assets / "AIEnter.png")
    image.save(assets / "AIEnter.ico", sizes=[(n, n) for n in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
with Image.open(assets / "AIEnter.png") as png:
    assert png.getpixel((0, 0))[3] == 0, "Transparent corner was lost"
with Image.open(assets / "AIEnter.ico") as icon:
    print("Icon sizes:", sorted(icon.ico.sizes()))
print("Encoded original artwork without redrawing:", image.size)
