"""Render logo-mono.svg geometry into multi-size .ico files (dark + light variants)."""
import os
from PIL import Image, ImageDraw

SCALE = 8  # 128 -> 1024
S = 128 * SCALE

DARK = (11, 18, 32)      # #0B1220
LIGHT = (242, 245, 250)  # #F2F5FA


def rr(draw, x, y, w, h, r, **kw):
    draw.rounded_rectangle([x * SCALE, y * SCALE, (x + w) * SCALE, (y + h) * SCALE],
                           radius=r * SCALE, **kw)


def render(color):
    # back panel: outlined rounded rect, cut away where the top panel sits
    back = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(back)
    rr(d, 30, 31, 82, 48, 13, outline=color + (102,), width=4 * SCALE)

    cut = Image.new("L", (S, S), 255)
    dc = ImageDraw.Draw(cut)
    rr(dc, 14, 44, 92, 54, 14, fill=0)
    back.putalpha(Image.composite(back.getchannel("A"), Image.new("L", (S, S), 0), cut))

    # top panel with the content lines knocked out
    top = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    dt = ImageDraw.Draw(top)
    rr(dt, 14, 44, 92, 54, 14, fill=color + (255,))
    for (x, y, w, h, r) in [(29, 56, 37, 8, 4), (78, 56, 15, 8, 4),
                            (29, 70, 57, 6.5, 3.25), (29, 82, 41, 6.5, 3.25)]:
        rr(dt, x, y, w, h, r, fill=(0, 0, 0, 0))

    out = Image.alpha_composite(back, top)
    return out


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    assets = os.path.join(root, "assets")
    os.makedirs(assets, exist_ok=True)

    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    for name, color in (("logo.ico", DARK), ("logo-light.ico", LIGHT)):
        image = render(color)
        image.save(os.path.join(assets, name), format="ICO", sizes=[(s, s) for s in sizes])
        image.resize((256, 256), Image.LANCZOS).save(
            os.path.join(assets, name.replace(".ico", "-preview.png")))
        print("wrote", os.path.join(assets, name))


if __name__ == "__main__":
    main()
