"""Encode the supplied transparent artwork as PNG and multi-resolution ICO.
Requires Pillow. Run from any working directory; source and outputs stay in repo.
"""
from pathlib import Path
from PIL import Image

assets = Path(__file__).resolve().parents[1] / "src" / "AIEnter" / "Assets"
with Image.open(assets / "source-icon.webp") as source:
    image = source.convert("RGBA")
    image.save(assets / "AIEnter.png")
    # Keep the taskbar extension's PNG untouched; enlarge the visible artwork
    # in the window/executable ICO while preserving its original alpha channel.
    # The source includes almost invisible alpha specks beyond the 240px sphere.
    # Ignore these when measuring the visible artwork; retain the original alpha
    # pixels inside the crop so its antialiased edge stays smooth.
    bounds = image.getchannel("A").point(lambda alpha: 255 if alpha >= 32 else 0).getbbox()
    if not bounds:
        raise ValueError("Source icon is completely transparent")
    cropped = image.crop(bounds)
    side = max(cropped.size)
    canvas_side = round(side / 0.94)
    icon_image = Image.new("RGBA", (canvas_side, canvas_side))
    icon_image.paste(cropped, ((canvas_side - cropped.width) // 2, (canvas_side - cropped.height) // 2))
    # Pillow only emits frames no larger than the source canvas.
    icon_image = icon_image.resize((256, 256), Image.Resampling.LANCZOS)
    icon_image.save(assets / "AIEnter.ico", sizes=[(n, n) for n in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
    icon_image.save(assets / "AIEnter.Window.ico", sizes=[(n, n) for n in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
with Image.open(assets / "AIEnter.png") as png:
    assert png.getpixel((0, 0))[3] == 0, "Transparent corner was lost"
with Image.open(assets / "AIEnter.ico") as icon:
    print("Icon sizes:", sorted(icon.ico.sizes()))
print("Encoded original artwork without redrawing:", image.size)
