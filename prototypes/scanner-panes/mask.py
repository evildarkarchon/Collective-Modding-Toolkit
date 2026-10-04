"""PROTOTYPE helper (throwaway): blacks out everything in a capture that isn't one of the app's windows.

Screen grabs pick up whatever sits behind the windows. Every capture here uses the same framing (region origin =
client origin - (20, 50)), so the window rects are fixed in image space. Run before compose.py.
"""

from pathlib import Path

from PIL import Image, ImageDraw

HERE = Path(__file__).parent
MAIN = (12, 19, 12 + 776, 19 + 489)  # outer frame, client at (20, 50)
SIDE = (780, 90, 980, 495)  # client (760, 40), 200x405
DETAILS = (20, 500, 780, 700)  # client (0, 450), 760x200
GROWN = (12, 19, 12 + 976, 19 + 689)  # variant B: 960x650 client + frame

for png in [*HERE.glob("shots/avalonia-*.png"), *HERE.glob("shots/drag-*.png"), *HERE.glob("reference/*.png")]:
	keep = [GROWN] if "-B-" in png.name else [MAIN] if "-C-" in png.name else [MAIN, SIDE, DETAILS]
	im = Image.open(png).convert("RGB")
	mask = Image.new("L", im.size, 0)
	draw = ImageDraw.Draw(mask)
	for box in keep:
		draw.rectangle((box[0], box[1], box[2] - 1, box[3] - 1), fill=255)
	Image.composite(im, Image.new("RGB", im.size, "black"), mask).save(png)
	print("masked", png.relative_to(HERE))
