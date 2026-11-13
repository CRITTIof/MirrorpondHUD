"""从 assets/MirrorpondHUDLogoBlack.svg 重新生成黑色图标。

旧的黑色 ICO 是 256px 单档缩小图，16/20/24px 下描边发糊、边缘发灰。这里改为：
  1. 用本机 Edge/Chrome 无头渲染 SVG（透明底、1000×1000），保留矢量边缘；
  2. 按 alpha 包围盒裁剪、方形补齐（保持光学尺寸一致）；
  3. 从 256px 母版生成 16–256 全部 ICO 尺寸，同时刷新浅色任务栏用的 logo.ico。
运行：python tools/make_black_ico.py
"""
import subprocess
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "assets"
SVG = ASSETS / "MirrorpondHUDLogoBlack.svg"
RENDER = ROOT / "black-render.png"

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
EDGE_CANDIDATES = [
    Path(r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"),
    Path(r"C:\Program Files\Microsoft\Edge\Application\msedge.exe"),
    Path(r"C:\Program Files\Google\Chrome\Application\chrome.exe"),
    Path(r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"),
]


def render_svg():
    if not SVG.exists():
        sys.exit(f"missing {SVG}")
    browser = next((p for p in EDGE_CANDIDATES if p.exists()), None)
    if browser is None:
        sys.exit("Edge/Chrome not found; install one or render the SVG manually")

    subprocess.run(
        [
            str(browser),
            "--headless=new",
            "--disable-gpu",
            "--hide-scrollbars",
            "--default-background-color=00000000",
            "--window-size=1000,1000",
            f"--screenshot={RENDER}",
            SVG.as_uri(),
        ],
        check=True,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    if not RENDER.exists():
        sys.exit("browser did not write the screenshot")
    return Image.open(RENDER).convert("RGBA")


def squared(image):
    bbox = image.getbbox()
    if bbox is None:
        sys.exit("rendered SVG is fully transparent")
    art = image.crop(bbox)
    side = max(art.width, art.height)
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.paste(art, ((side - art.width) // 2, (side - art.height) // 2), art)
    return canvas


def main():
    master = squared(render_svg()).resize((256, 256), Image.LANCZOS)

    png_path = ASSETS / "MirrorpondHUDLogoBlack.png"
    ico_path = ASSETS / "MirrorpondHUDLogoBlack.ico"
    master.save(png_path)
    master.save(ico_path, format="ICO", sizes=[(s, s) for s in SIZES])

    # 浅色任务栏 / 窗口图标一直用这张黑色图；直接刷新，避免两套黑图不一致。
    master.save(ASSETS / "logo.ico", format="ICO", sizes=[(s, s) for s in SIZES])
    master.save(ASSETS / "logo-black.png")

    RENDER.unlink(missing_ok=True)
    print(f"wrote {png_path.name}, {ico_path.name}, logo.ico, logo-black.png "
          f"({len(SIZES)} sizes, 256px master)")


if __name__ == "__main__":
    main()
