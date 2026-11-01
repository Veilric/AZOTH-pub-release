"""Rasterize official SVG sources with system librsvg; requires python3-gi/cairo/Pillow."""
from pathlib import Path
import argparse
import shutil
import gi
import cairo
from PIL import Image

gi.require_foreign('cairo')
gi.require_version('Rsvg', '2.0')
from gi.repository import Rsvg
root = Path(__file__).resolve().parents[1]
assets = root / 'src/AZOTH.App/Assets/Icons'
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--source-dir', type=Path, help='Optionally import the four original named SVGs before rendering; otherwise use the committed canonical SVGs.')
args = parser.parse_args()

def render(source, target, width, height):
    handle = Rsvg.Handle.new_from_file(str(source))
    surface = cairo.ImageSurface(cairo.FORMAT_ARGB32, width, height)
    viewport = Rsvg.Rectangle()
    viewport.x, viewport.y, viewport.width, viewport.height = 0, 0, width, height
    handle.render_document(cairo.Context(surface), viewport)
    surface.write_to_png(str(target))

for source, name, width, height in [
    ('AZOTH 1024x1024.svg', 'azoth', 1024, 1024),
    ('AZOTH 1024x1024-dark.svg', 'azoth-dark', 1024, 1024),
    ('Azoth-horizontal.svg', 'azoth-horizontal', 1200, 600),
    ('Azoth-horizontal-dark.svg', 'azoth-horizontal-dark', 1200, 600),
]:
    if args.source_dir is not None:
        shutil.copyfile(args.source_dir / source, assets / (name + '.svg'))
    render(assets / (name + '.svg'), assets / (name + '.png'), width, height)
Image.open(assets / 'azoth.png').save(assets / 'azoth.ico', sizes=[(n,n) for n in (16,24,32,48,64,128,256)])
