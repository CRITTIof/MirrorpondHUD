"""Build the light/dark brand PNG + multi-size ICO from the hand-tuned SVGs.

The two SVGs are Affinity exports: their artwork is an embedded PNG, already
tight-cropped to the logo silhouette.  Re-rendering the outer 1000x1000 SVG
would add the artboard padding back, so extract the embedded PNG and center it
in a square instead.  Every ICO size uses the same 256px master, matching the
rest of assets/.
"""
import base64
import io
import re
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "assets"
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

VARIANTS = {
    "MirrorpondHUDLogoYellow": "MirrorpondHUDLogoYellow",
    "MirrorpondHUDLogoDeepYellow": "MirrorpondHUDLogoDeepYellow",
}


def embedded_png(svg_path):
    text = svg_path.read_text(encoding="utf-8")
    match = re.search(r'xlink:href="data:image/png;base64,([^"]+)"', text)
    if match is None:
        raise SystemExit(f"no embedded PNG found in {svg_path}")
    return Image.open(io.BytesIO(base64.b64decode(match.group(1)))).convert("RGBA")


def squared(image):
    side = max(image.width, image.height)
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    x = (side - image.width) // 2
    y = (side - image.height) // 2
    canvas.paste(image, (x, y), image)
    return canvas


def main():
    for svg_name, out_name in VARIANTS.items():
        art = squared(embedded_png(ASSETS / f"{svg_name}.svg"))
        master = art.resize((256, 256), Image.LANCZOS)
        png_path = ASSETS / f"{out_name}.png"
        ico_path = ASSETS / f"{out_name}.ico"
        master.save(png_path)
        master.save(ico_path, format="ICO", sizes=[(s, s) for s in SIZES])
        print(f"wrote {png_path.name} and {ico_path.name} ({art.width}px master)")


if __name__ == "__main__":
    main()
