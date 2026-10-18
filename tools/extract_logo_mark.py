"""Extract the white and cyan mark from the original navy app icon.

Requires Pillow. Run from any directory: python tools/extract_logo_mark.py
"""

from pathlib import Path

from PIL import Image


ASSETS = Path(__file__).resolve().parents[1] / "src" / "CodexMonitor.App" / "Assets"
SOURCE = ASSETS / "CodexMonitorInk.png"
OUTPUT = ASSETS / "CodexMonitorMark.png"


def main() -> None:
    source = Image.open(SOURCE).convert("RGBA")
    output = Image.new("RGBA", source.size)

    # The source's navy is approximately (5, 23, 55). Both the white and
    # cyan strokes have a blue channel near 253, which gives a stable alpha
    # estimate at their anti-aliased edges without moving any brushwork.
    pixels = []
    for red, green, blue, original_alpha in source.get_flattened_data():
        # Ignore compression and paper-grain variations in the navy field.
        if blue < 100:
            pixels.append((0, 0, 0, 0))
            continue
        stroke_alpha = max(0.0, min(1.0, (blue - 55) / 198))
        alpha = round(original_alpha * stroke_alpha)
        if alpha == 0:
            pixels.append((0, 0, 0, 0))
            continue

        # Remove the navy color mixed into partially covered edge pixels.
        red = round((red - 5 * (1 - stroke_alpha)) / stroke_alpha)
        green = round((green - 23 * (1 - stroke_alpha)) / stroke_alpha)
        blue = round((blue - 55 * (1 - stroke_alpha)) / stroke_alpha)
        pixels.append(tuple(max(0, min(255, value)) for value in (red, green, blue)) + (alpha,))

    output.putdata(pixels)
    output.save(OUTPUT)
    print(OUTPUT)


if __name__ == "__main__":
    main()
