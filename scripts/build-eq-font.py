"""Reconstruct the supplied lettering. Requires Pillow, numpy, vtracer, fonttools, brotli.

Usage: python scripts/build-eq-font.py --proof-dir <outside-checkout>
Source PNGs retain their original bytes. The separated letter strip supplies
all 52 letters; punctuation islands are explicitly assigned to their glyphs.
"""
from pathlib import Path
import argparse
import hashlib
import io
import itertools
import json
import xml.etree.ElementTree as ET

import numpy as np
from PIL import Image, ImageDraw, ImageFont
import vtracer
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.cu2quPen import Cu2QuPen
from fontTools.pens.recordingPen import RecordingPen
from fontTools.pens.transformPen import TransformPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.svgLib.path import parse_path
from fontTools.ttLib import TTFont
from fontTools.feaLib.builder import addOpenTypeFeaturesFromString

ROOT = Path(__file__).resolve().parents[1]
FAMILY = 'EQL Quest Display'
STEM = 'EqlQuestDisplay-Regular'
SOURCES = [
    ('eqfont_letters.png', 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz', 82),
    ('eqfont(2).png', '1234567890', 70),
    ('eqfont(3).png', '!@$%^&*()', 79),
    ('eqfont(4).png', '-=+_~', 80),
    ('eqfont(5).png', "[]{};':,.<>/?", 80),
]


def components(alpha):
    remaining = alpha > 128
    result = []
    for y, x in zip(*np.where(remaining)):
        if not remaining[y, x]:
            continue
        todo, points = [(y, x)], []
        remaining[y, x] = False
        while todo:
            yy, xx = todo.pop()
            points.append((yy, xx))
            for ny, nx in ((yy-1, xx), (yy+1, xx), (yy, xx-1), (yy, xx+1)):
                if 0 <= ny < alpha.shape[0] and 0 <= nx < alpha.shape[1] and remaining[ny, nx]:
                    remaining[ny, nx] = False
                    todo.append((ny, nx))
        if len(points) > 5:
            mask = np.zeros(alpha.shape, dtype=bool)
            yy, xx = zip(*points)
            mask[yy, xx] = True
            result.append(mask)
    return sorted(result, key=lambda mask: np.where(mask)[1].min())


def letter_masks(alpha):
    """A blank column separates every letter, including the complete i/j dots."""
    masks = []
    for ink, columns in itertools.groupby(range(alpha.shape[1]), key=lambda x: bool((alpha[:,x]>128).any())):
        if ink:
            columns = list(columns)
            mask = np.zeros(alpha.shape, dtype=bool)
            mask[:,columns[0]:columns[-1]+1] = alpha[:,columns[0]:columns[-1]+1]>128
            masks.append(mask)
    assert len(masks) == 52
    return masks


def group_masks(name, masks):
    if name == 'eqfont(3).png':
        assert len(masks) == 10
        return masks[:3] + [masks[3] | masks[4]] + masks[5:]
    if name == 'eqfont(4).png':
        assert len(masks) == 6
        return [masks[0], masks[1] | masks[2]] + masks[3:]
    if name == 'eqfont(5).png':
        assert len(masks) == 15
        return masks[:4] + [masks[4] | masks[5], masks[6], masks[7] | masks[8]] + masks[9:]
    return masks


def trace(alpha, mask, baseline):
    ys, xs = np.where(mask)
    x0, y0, x1, y1 = int(xs.min()), int(ys.min()), int(xs.max()+1), int(ys.max()+1)
    # Restore the antialiased edge around each assigned island.
    padded = np.pad(mask, 1)
    expanded = np.zeros_like(mask)
    for dy in range(3):
        for dx in range(3):
            expanded |= padded[dy:dy+mask.shape[0], dx:dx+mask.shape[1]]
    crop = np.where(expanded, alpha, 0)[max(0,y0-1):y1+1, max(0,x0-1):x1+1]
    cx, cy = max(0,x0-1), max(0,y0-1)
    ink = Image.fromarray(crop).resize((crop.shape[1]*4, crop.shape[0]*4), Image.Resampling.LANCZOS)
    raster = Image.fromarray(np.where(np.array(ink) > 127, 0, 255).astype('uint8'))
    raster = Image.fromarray(np.pad(np.array(raster), 8, constant_values=255))
    buffer = io.BytesIO()
    raster.save(buffer, format='PNG')
    svg = vtracer.convert_raw_image_to_svg(buffer.getvalue(), img_format='png', colormode='binary', mode='spline', filter_speckle=0, corner_threshold=60, length_threshold=4, splice_threshold=45, path_precision=3)
    pen = RecordingPen()
    # Source pixels map to 10 units; y points upwards in a font.
    transformed = TransformPen(pen, (2.5, 0, 0, -2.5, 35+(cx-x0)*10-20, (baseline-cy)*10+20))
    for path in ET.fromstring(svg).iter('{http://www.w3.org/2000/svg}path'):
        tx, ty = map(float, path.attrib['transform'].removeprefix('translate(').removesuffix(')').split(','))
        parse_path(path.attrib['d'], TransformPen(transformed, (1, 0, 0, 1, tx, ty)))
    return pen, (x1-x0)*10+70, [x0,y0,x1,y1]


def transformed(source, matrix):
    out = RecordingPen()
    source.replay(TransformPen(out, matrix))
    return out


def polygon(pen, points):
    pen.moveTo(points[0])
    for point in points[1:]:
        pen.lineTo(point)
    pen.closePath()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-dir', type=Path, default=ROOT/'design/font-sources/eq-display')
    parser.add_argument('--output-dir', type=Path, default=ROOT/'src/EqlTracker.App/Assets/Fonts')
    parser.add_argument('--proof-dir', required=True, type=Path)
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    args.proof_dir.mkdir(parents=True, exist_ok=True)
    outlines, widths, records = {}, {}, []
    for name, chars, baseline in SOURCES:
        path = args.source_dir/name
        alpha = np.array(Image.open(path).convert('RGBA'))[:, :, 3]
        masks = letter_masks(alpha) if name == 'eqfont_letters.png' else group_masks(name, components(alpha))
        assert len(masks) == len(chars), (name, len(masks), len(chars))
        glyphs = []
        for char, mask in zip(chars, masks):
            glyph_baseline = 83 if name == 'eqfont_letters.png' and char.islower() else baseline
            outlines[char], widths[char], bounds = trace(alpha, mask, glyph_baseline)
            glyphs.append(dict(Character=char, Bounds=bounds, Baseline=glyph_baseline))
        records.append(dict(File=name, Sha256=hashlib.sha256(path.read_bytes()).hexdigest(), Glyphs=glyphs))
    # Complete printable ASCII using only derivatives of the supplied strokes.
    outlines['"'] = RecordingPen()
    outlines["'"].replay(outlines['"'])
    transformed(outlines["'"], (1,0,0,1,150,0)).replay(outlines['"'])
    widths['"'] = widths["'"]+150
    outlines['\\'] = transformed(outlines['/'], (-1,0,0,1,widths['/'],0))
    widths['\\'] = widths['/']
    outlines['`'] = transformed(outlines["'"], (-1,0,0,1,widths["'"],100))
    widths['`'] = widths["'"]
    outlines['|'] = RecordingPen()
    polygon(outlines['|'], [(65,-220),(125,-220),(125,760),(65,760)])
    widths['|'] = 190
    outlines['#'] = RecordingPen()
    for points in [[(90,-10),(153,-10),(277,700),(214,700)],[(290,-10),(353,-10),(477,700),(414,700)],[(50,200),(510,200),(520,260),(60,260)],[(90,430),(550,430),(560,490),(100,490)]]:
        polygon(outlines['#'], points)
    widths['#'] = 610
    outlines[' '] = RecordingPen()
    widths[' '] = 300
    # Aliases/constructed punctuation keep ordinary app titles in one family.
    aliases = {'\u00a0':' ', '\u2010':'-', '\u2011':'-', '\u2018':"'", '\u2019':"'", '\u201c':'"', '\u201d':'"', '\u2212':'-'}
    for char, source in aliases.items():
        outlines[char], widths[char] = outlines[source], widths[source]
    for char, factor in [('\u2013',1.6),('\u2014',2.4)]:
        outlines[char] = transformed(outlines['-'], (factor,0,0,1,0,0))
        widths[char] = round(widths['-']*factor)
    outlines['\u2026'] = RecordingPen()
    for x in range(3):
        transformed(outlines['.'], (1,0,0,1,x*widths['.'],0)).replay(outlines['\u2026'])
    widths['\u2026'] = widths['.']*3
    missing = RecordingPen()
    polygon(missing, [(35,0),(35,700),(485,700),(485,0)])
    polygon(missing, [(95,60),(425,60),(425,640),(95,640)])
    names = {c:f'uni{ord(c):04X}' for c in outlines}
    glyphs = {}
    for char, outline in [('.notdef', missing), *outlines.items()]:
        pen = TTGlyphPen(None)
        outline.replay(Cu2QuPen(pen, max_err=1, reverse_direction=True))
        glyphs['.notdef' if char == '.notdef' else names[char]] = pen.glyph()
    fb = FontBuilder(1000, isTTF=True)
    fb.setupGlyphOrder(list(glyphs))
    fb.setupCharacterMap({ord(c):names[c] for c in outlines})
    fb.setupGlyf(glyphs)
    fb.setupHorizontalMetrics({name:(520 if name=='.notdef' else widths[next(c for c in outlines if names[c]==name)], min((p[0] for p in glyph.coordinates),default=0)) for name,glyph in glyphs.items()})
    fb.setupHorizontalHeader(ascent=840, descent=-260, lineGap=0)
    fb.setupNameTable(dict(familyName=FAMILY, styleName='Regular', uniqueFontIdentifier=f'{FAMILY};1.001', fullName=f'{FAMILY} Regular', psName=STEM, version='Version 1.001', copyright='Copyright 2026 EQL Catalyst contributors, to the extent of their rights.', licenseDescription='SIL Open Font License 1.1; contributor rights only. See the bundled provenance notice.', licenseInfoURL='https://openfontlicense.org', description='Reconstructed from user-supplied EverQuest lettering PNGs, using the separated strip for all letters. Five missing ASCII symbols are constructed; see the source manifest. Not an official game font.'))
    fb.setupOS2(version=4, sTypoAscender=840, sTypoDescender=-260, sTypoLineGap=0, usWinAscent=840, usWinDescent=260, sxHeight=550, sCapHeight=700, usWeightClass=400, fsType=0, fsSelection=0xC0)
    fb.setupPost()
    fb.setupMaxp()
    # Conservative spacing adjustments for common display pairs.
    pairs = {'AV':-50,'AW':-40,'AY':-40,'AT':-30,'FA':-25,'LT':-30,'PA':-25,'TA':-40,'To':-35,'Ta':-30,'Te':-25,'Tr':-20,'VA':-45,'Va':-30,'Vo':-30,'WA':-35,'Wa':-25,'Wo':-20,'YA':-40,'Yo':-35,'Ya':-35}
    rules = '\n'.join(f'pos {names[a]} {names[b]} {value};' for (a,b),value in pairs.items())
    addOpenTypeFeaturesFromString(fb.font, f'feature kern {{ {rules} }} kern;')
    # Fixed timestamps keep regeneration byte-for-byte reproducible.
    fb.font['head'].created = fb.font['head'].modified = 3875817600
    fb.font.recalcTimestamp = False
    ttf = args.output_dir/(STEM+'.ttf')
    fb.save(ttf)
    web = TTFont(ttf, recalcTimestamp=False)
    web.flavor = 'woff2'
    web.save(args.output_dir/(STEM+'.woff2'))
    loaded = TTFont(ttf, checkChecksums=2)
    assert all(c in loaded.getBestCmap() for c in range(32,127))
    proof = Image.new('RGB', (1500,1020), '#101922')
    draw = ImageDraw.Draw(proof)
    draw.text((40,28), FAMILY, font=ImageFont.truetype(str(ttf),56), fill='#E9B94F')
    lines = [('ABCDEFGHIJKLMNOPQRSTUVWXYZ',58),('abcdefghijklmnopqrstuvwxyz',58),('0123456789',58),('!@#$%^&*() -=+_~',48),("[]{};\"':,.<>/\\?|`",48),('EQL Catalyst / EverQuest',48),('Tracker   Combat Lab   Overview',36),('The quick brown fox jumps over the lazy dog.',28),('EQL Catalyst — Settings',20)]
    y=135
    for text,size in lines:
        draw.text((40,y),text,font=ImageFont.truetype(str(ttf),size),fill='#F5ECD6')
        y += 92
    proof.save(args.proof_dir/'font-specimen.png')
    manifest = dict(Family=FAMILY, Version='1.001', UnitsPerEm=1000, ReconstructedCharacters=''.join(c for _,cs,_ in SOURCES for c in cs), ConstructedAscii=['"','#','\\','`','|'], PrintableAscii=95, MappedCharacters=len(outlines), KerningPairs=pairs, Sources=records, Outputs={p.name:dict(Bytes=p.stat().st_size,Sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in [ttf,args.output_dir/(STEM+'.woff2')]})
    (args.output_dir/'EqlQuestDisplay.manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:manifest[k] for k in ['Family','PrintableAscii','MappedCharacters','Outputs']}))


if __name__ == '__main__':
    main()
