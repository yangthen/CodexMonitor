"""Build Windows icon sizes from the generated Codex Monitor artwork.

Requires Pillow: python -m pip install Pillow
Run from any directory: python tools/build_icon_assets.py
"""

from pathlib import Path
import math

from PIL import Image


ASSETS = Path(__file__).resolve().parents[1] / "src" / "CodexMonitor.App" / "Assets"
SOURCE = ASSETS / "CodexMonitorMark.png"


def resized(source: Image.Image, size: int) -> Image.Image:
    return source.resize((size, size), Image.Resampling.LANCZOS)


def centered(source: Image.Image, width: int, height: int, icon_size: int) -> Image.Image:
    canvas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    icon = resized(source, icon_size)
    canvas.alpha_composite(icon, ((width - icon_size) // 2, (height - icon_size) // 2))
    return canvas


def tray_mark(source: Image.Image) -> Image.Image:
    # Ignore near-transparent extraction noise, then fill about 88% of the tray canvas.
    visible = source.getchannel("A").point(lambda alpha: 255 if alpha >= 4 else 0)
    bounds = visible.getbbox()
    if bounds is None:
        raise ValueError("The tray artwork is empty")
    left, top, right, bottom = bounds
    side = math.ceil(max(right - left, bottom - top) / 0.88)
    center_x = (left + right) / 2
    center_y = (top + bottom) / 2
    crop_left = round(center_x - side / 2)
    crop_top = round(center_y - side / 2)
    return source.crop((crop_left, crop_top, crop_left + side, crop_top + side))


def main() -> None:
    source = Image.open(SOURCE).convert("RGBA")
    source.save(
        ASSETS / "AppIcon.ico",
        format="ICO",
        sizes=[(size, size) for size in (16, 20, 24, 32, 40, 48, 64, 128, 256)],
    )
    tray_mark(source).save(
        ASSETS / "TrayIcon.ico",
        format="ICO",
        sizes=[(size, size) for size in (16, 20, 24, 32, 40, 48, 64)],
    )
    sizes = {
        "LockScreenLogo.scale-200.png": 48,
        "Square150x150Logo.scale-200.png": 300,
        "Square44x44Logo.scale-200.png": 88,
        "Square44x44Logo.targetsize-24_altform-unplated.png": 24,
        "Square44x44Logo.targetsize-48_altform-lightunplated.png": 48,
        "StoreLogo.png": 50,
    }
    for name, size in sizes.items():
        resized(source, size).save(ASSETS / name, format="PNG")
    centered(source, 1240, 600, 280).save(ASSETS / "SplashScreen.scale-200.png", format="PNG")
    centered(source, 620, 300, 260).save(ASSETS / "Wide310x150Logo.scale-200.png", format="PNG")


if __name__ == "__main__":
    main()
