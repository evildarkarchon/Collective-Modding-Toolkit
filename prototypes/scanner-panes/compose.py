"""PROTOTYPE helper (throwaway): builds shots/compare-<pick>.png = [python | A | B | C], each labelled.

Run with any Python that has Pillow (e.g. the Tk 8.6 scratch venv from reference/capture-python.ps1):
    python compose.py
"""

from pathlib import Path

from PIL import Image, ImageDraw

HERE = Path(__file__).parent
PICKS = {"racesubgraph": "python-scanner-racesubgraph.png", "invalidarchive": "python-scanner-invalidarchive.png"}

for pick, ref in PICKS.items():
	panels = [("Python (Tk 8.6)", Image.open(HERE / "reference" / ref))]
	panels += [(f"Avalonia {v}", Image.open(HERE / "shots" / f"avalonia-{v}-{pick}.png")) for v in "ABC"]
	w, h = panels[0][1].size
	out = Image.new("RGB", (w * len(panels), h + 24), "black")
	draw = ImageDraw.Draw(out)
	for i, (label, im) in enumerate(panels):
		out.paste(im.convert("RGB"), (i * w, 24))
		draw.text((i * w + 8, 5), label, fill="magenta")
	out.save(HERE / "shots" / f"compare-{pick}.png")
	print("->", f"shots/compare-{pick}.png")
