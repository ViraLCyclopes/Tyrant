# Makes studio/src-tauri/icons/source/{tyrant-logo.png, icon-source.png} from a logo render (a silhouette on transparent):
#   python scripts/make-icon-source.py <logo.png>
# then `npx tauri icon src-tauri/icons/source/icon-source.png` (and delete the android/ and ios/ folders it adds).
import sys
from pathlib import Path
from PIL import Image, ImageDraw

S = 1024
out = Path(__file__).resolve().parent.parent / 'src-tauri' / 'icons' / 'source'
src = Image.open(sys.argv[1]).convert('RGBA')
rex = src.crop(src.getchannel('A').getbbox())

def placed(share):
    k = (S * share) / max(rex.size)
    return rex.resize((round(rex.width * k), round(rex.height * k)), Image.LANCZOS)

logo = Image.new('RGBA', (S, S), (0, 0, 0, 0))
art = placed(0.9)
logo.alpha_composite(art, ((S - art.width) // 2, (S - art.height) // 2))
logo.save(out / 'tyrant-logo.png')

# The tile: a rounded square, vertical gradient from the app's panel-2 green (#24493a) to its background (#12261c).
top, bottom = (0x24, 0x49, 0x3a), (0x12, 0x26, 0x1c)
gradient = Image.new('RGBA', (S, S))
for y in range(S):
    t = y / (S - 1)
    gradient.paste(tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,), (0, y, S, y + 1))
mask = Image.new('L', (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle((32, 32, S - 32, S - 32), radius=200, fill=255)
icon = Image.new('RGBA', (S, S), (0, 0, 0, 0))
icon.paste(gradient, (0, 0), mask)
art = placed(0.70)
icon.alpha_composite(art, ((S - art.width) // 2, (S - art.height) // 2))
icon.save(out / 'icon-source.png')
print(f'wrote {out / "tyrant-logo.png"} and {out / "icon-source.png"}')
