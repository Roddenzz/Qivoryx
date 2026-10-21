"""Rebuild the Qivoryx mark without touching the application catalogue."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
image = Image.new('RGBA', (1024, 1024))
draw = ImageDraw.Draw(image)
draw.rounded_rectangle((32, 32, 992, 992), radius=230, fill='#161616', outline='#404040', width=8)
draw.ellipse((226, 194, 778, 746), outline='#f4f4f4', width=92)
draw.line((598, 632, 805, 839), fill='#f4f4f4', width=92)
draw.line((670, 703, 720, 753), fill='#161616', width=20)
assets = root / 'Tweaker/Assets'
assets.mkdir(exist_ok=True)
image.save(assets / 'app.ico', sizes=[(n, n) for n in (16, 24, 32, 48, 64, 128, 256)])
image.resize((256, 256), Image.Resampling.LANCZOS).save(assets / 'brand.png')
