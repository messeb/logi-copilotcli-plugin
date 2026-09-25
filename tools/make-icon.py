#!/usr/bin/env python3
"""Draws the plugin icon: src/package/metadata/Icon256x256.png.

Kept as a script rather than a committed binary nobody can edit, so the icon can be regenerated
after a palette change.

The mark is a shell prompt, `$>`, on a rounded dark square. It says what the plugin is about
without metaphor, and it borrows nothing from GitHub's own Copilot mark, which this plugin may
not use.

Four things are deliberate, each checked by looking at the result at 32 px and in a row of the
plugins it actually sits beside in Options+, not just large and alone:

- The corners are genuinely transparent, not filled with the field colour. Both neighbouring
  plugins here ship RGBA with real transparency, so a filled corner would be the odd one out
  against whatever background the host draws.
- The `$` is green. In white-on-black the icon merges into the Options+ list, which is dark;
  one coloured glyph fixes that while staying a shell prompt rather than becoming decoration.
- Menlo Bold, because the glyphs have to survive being 32 px wide - a regular weight thins out
  and the `$` loses its bar.
- The corner radius is the macOS proportion (22.37% of the width), so it sits correctly next to
  system-styled icons.
"""
import os

from PIL import Image, ImageDraw, ImageFont

SIZE = 256
SUPERSAMPLE = 4          # drawn large and downsampled, so the curves are smooth
OUT = os.path.join(os.path.dirname(__file__), "..", "src", "package", "metadata", "Icon256x256.png")

MENLO = "/System/Library/Fonts/Menlo.ttc"
MENLO_BOLD = 1           # face index inside the collection

FIELD = (0x18, 0x1B, 0x20)
DOLLAR = (0x4A, 0xD9, 0x6A)
CHEVRON = (0xFF, 0xFF, 0xFF)

N = SIZE * SUPERSAMPLE
C = N / 2
RADIUS = int(0.2237 * N)
FONT_SIZE = int(112 * SUPERSAMPLE)


def rounded_field():
    """The rounded square, with the corners left transparent rather than filled."""
    image = Image.new("RGBA", (N, N), (0, 0, 0, 0))

    mask = Image.new("L", (N, N), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, N - 1, N - 1], radius=RADIUS, fill=255)

    image.paste(Image.new("RGB", (N, N), FIELD), (0, 0), mask)
    return image


image = rounded_field()
draw = ImageDraw.Draw(image)
font = ImageFont.truetype(MENLO, FONT_SIZE, index=MENLO_BOLD)

# Measured as one string and drawn in two parts, so the pair stays optically centred rather than
# each glyph being centred on its own.
bounds = draw.textbbox((0, 0), "$>", font=font)
x = C - (bounds[2] - bounds[0]) / 2 - bounds[0]
y = C - (bounds[3] - bounds[1]) / 2 - bounds[1]

for fragment, colour in (("$", DOLLAR), (">", CHEVRON)):
    draw.text((x, y), fragment, font=font, fill=colour)
    x += draw.textlength(fragment, font=font)

image.resize((SIZE, SIZE), Image.LANCZOS).save(os.path.normpath(OUT))
print(f"wrote {os.path.normpath(OUT)}")
