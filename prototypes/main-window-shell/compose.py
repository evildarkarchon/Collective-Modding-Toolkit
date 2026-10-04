"""PROTOTYPE: builds shots/compare-<scenario>-<variant>.png = [python | avalonia | 50/50 blend] from capture.ps1 output.

Run with any Python that has Pillow: python compose.py
The blend makes vertical/horizontal drift obvious: aligned text looks crisp, drift looks doubled.
"""

from pathlib import Path

from PIL import Image, ImageDraw

HERE = Path(__file__).parent
SHOTS = HERE / "shots"


def main() -> None:
    """Compose every avalonia-*.png in shots/ against python-overview.png."""
    python = Image.open(HERE / "python-overview.png").convert("RGB")
    for shot in sorted(SHOTS.glob("avalonia-*.png")):
        ava = Image.open(shot).convert("RGB").resize(python.size)
        blend = Image.blend(python, ava, 0.5)
        w, h = python.size
        out = Image.new("RGB", (w * 3 + 20, h + 24), "#000000")
        draw = ImageDraw.Draw(out)
        for i, (img, label) in enumerate([(python, "python (Tk 8.6)"), (ava, shot.stem), (blend, "50/50 blend")]):
            out.paste(img, (i * (w + 10), 24))
            draw.text((i * (w + 10) + 4, 4), label, fill="#ff00ff")
        out.save(SHOTS / shot.name.replace("avalonia-", "compare-"))


if __name__ == "__main__":
    main()
