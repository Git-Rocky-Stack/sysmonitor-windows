#!/usr/bin/env python3
"""Builds the console faces SysMonitor ships from System-X's web fonts.

System-X serves its four faces as WOFF2 Latin subsets, Archivo and Public Sans as variable fonts. WinUI loads
TrueType, and a XAML FontFamily names one family in one file, so every face the design uses becomes a static
TrueType file whose family name is its own:

    PublicSans-Regular.ttf       "Public Sans"             wght 400
    PublicSans-Medium.ttf        "Public Sans Medium"      wght 500
    PublicSans-SemiBold.ttf      "Public Sans SemiBold"    wght 600
    PublicSans-Bold.ttf          "Public Sans Bold"        wght 700
    Archivo-W118-ExtraBold.ttf   "Archivo W118 ExtraBold"  wght 800, wdth 118  view titles, wordmark, ON AIR
    Archivo-W118-Bold.ttf        "Archivo W118 Bold"       wght 700, wdth 118  placards
    Archivo-W112-Bold.ttf        "Archivo W112 Bold"       wght 700, wdth 112  cap buttons
    Archivo-W110-Bold.ttf        "Archivo W110 Bold"       wght 700, wdth 110  lamps
    DepartureMono-Regular.ttf    "Departure Mono"          converted, names untouched
    Iosevka-Regular.ttf          "Iosevka"                 converted, names untouched

Name IDs 1, 16 and 21 carry the family and 2, 17 and 22 the face, so "#Family" resolves whichever of them
DirectWrite reads. OS/2 keeps each instance's real weight, so a SemiBold face asked for at Normal weight is
drawn as it is rather than thickened. The Latin subsets are what ship; DirectWrite falls back for the rest.

None of the four families names a Reserved Font Name after its copyright notice - the script checks, and stops
if one ever does - so the instances may carry these names under the SIL Open Font License 1.1. An OFL file per
family, with the copyright notice read from the font itself, is written beside the fonts.

Usage:
    python scripts/build-fonts.py [--source ../system-x-app/src/assets/fonts]

Needs fontTools and brotli (pip install fonttools brotli).
"""

import argparse
import pathlib
import sys

from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

REPO = pathlib.Path(__file__).resolve().parent.parent
OUTPUT = REPO / "src" / "SysMonitor.App" / "Assets" / "Fonts"
DEFAULT_SOURCE = REPO.parent / "system-x-app" / "src" / "assets" / "fonts"

# (source woff2, output file, family name, {axis: value})
INSTANCES = [
    ("public-sans-latin.woff2", "PublicSans-Regular.ttf", "Public Sans", {"wght": 400}),
    ("public-sans-latin.woff2", "PublicSans-Medium.ttf", "Public Sans Medium", {"wght": 500}),
    ("public-sans-latin.woff2", "PublicSans-SemiBold.ttf", "Public Sans SemiBold", {"wght": 600}),
    ("public-sans-latin.woff2", "PublicSans-Bold.ttf", "Public Sans Bold", {"wght": 700}),
    ("archivo-latin.woff2", "Archivo-W118-ExtraBold.ttf", "Archivo W118 ExtraBold", {"wght": 800, "wdth": 118}),
    ("archivo-latin.woff2", "Archivo-W118-Bold.ttf", "Archivo W118 Bold", {"wght": 700, "wdth": 118}),
    ("archivo-latin.woff2", "Archivo-W112-Bold.ttf", "Archivo W112 Bold", {"wght": 700, "wdth": 112}),
    ("archivo-latin.woff2", "Archivo-W110-Bold.ttf", "Archivo W110 Bold", {"wght": 700, "wdth": 110}),
]

# (source woff2, output file) - converted to TrueType, nothing else changed.
CONVERSIONS = [
    ("departure-mono.woff2", "DepartureMono-Regular.ttf"),
    ("iosevka-latin.woff2", "Iosevka-Regular.ttf"),
]

# (source woff2, licence file) - one per family, whichever source file carries its copyright notice.
LICENCES = [
    ("public-sans-latin.woff2", "OFL-PublicSans.txt"),
    ("archivo-latin.woff2", "OFL-Archivo.txt"),
    ("departure-mono.woff2", "OFL-DepartureMono.txt"),
    ("iosevka-latin.woff2", "OFL-Iosevka.txt"),
]

WINDOWS = (3, 1, 0x409)  # platform, encoding, language: what DirectWrite reads

# OS/2 fsSelection bits.
FS_ITALIC = 1 << 0
FS_BOLD = 1 << 5
FS_REGULAR = 1 << 6
FS_WWS = 1 << 8

# SIL Open Font License 1.1, verbatim (https://openfontlicense.org).
OFL = """SIL OPEN FONT LICENSE Version 1.1 - 26 February 2007
-----------------------------------------------------------

PREAMBLE
The goals of the Open Font License (OFL) are to stimulate worldwide
development of collaborative font projects, to support the font
creation efforts of academic and linguistic communities, and to
provide a free and open framework in which fonts may be shared and
improved in partnership with others.

The OFL allows the licensed fonts to be used, studied, modified and
redistributed freely as long as they are not sold by themselves. The
fonts, including any derivative works, can be bundled, embedded,
redistributed and/or sold with any software provided that any reserved
names are not used by derivative works. The fonts and derivatives,
however, cannot be released under any other type of license. The
requirement for fonts to remain under this license does not apply to
any document created using the fonts or their derivatives.

DEFINITIONS
"Font Software" refers to the set of files released by the Copyright
Holder(s) under this license and clearly marked as such. This may
include source files, build scripts and documentation.

"Reserved Font Name" refers to any names specified as such after the
copyright statement(s).

"Original Version" refers to the collection of Font Software
components as distributed by the Copyright Holder(s).

"Modified Version" refers to any derivative made by adding to,
deleting, or substituting -- in part or in whole -- any of the
components of the Original Version, by changing formats or by porting
the Font Software to a new environment.

"Author" refers to any designer, engineer, programmer, technical
writer or other person who contributed to the Font Software.

PERMISSION & CONDITIONS
Permission is hereby granted, free of charge, to any person obtaining
a copy of the Font Software, to use, study, copy, merge, embed,
modify, redistribute, and sell modified and unmodified copies of the
Font Software, subject to the following conditions:

1) Neither the Font Software nor any of its individual components, in
Original or Modified Versions, may be sold by itself.

2) Original or Modified Versions of the Font Software may be bundled,
redistributed and/or sold with any software, provided that each copy
contains the above copyright notice and this license. These can be
included either as stand-alone text files, human-readable headers or
in the appropriate machine-readable metadata fields within text or
binary files as long as those fields can be easily viewed by the user.

3) No Modified Version of the Font Software may use the Reserved Font
Name(s) unless explicit written permission is granted by the
corresponding Copyright Holder. This restriction only applies to the
primary font name as presented to the users.

4) The name(s) of the Copyright Holder(s) or the Author(s) of the Font
Software shall not be used to promote, endorse or advertise any
Modified Version, except to acknowledge the contribution(s) of the
Copyright Holder(s) and the Author(s) or with their explicit written
permission.

5) The Font Software, modified or unmodified, in part or in whole,
must be distributed entirely under this license, and must not be
distributed under any other license. The requirement for fonts to
remain under this license does not apply to any document created using
the Font Software.

TERMINATION
This license becomes null and void if any of the above conditions are
not met.

DISCLAIMER
THE FONT SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO ANY WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT
OF COPYRIGHT, PATENT, TRADEMARK, OR OTHER RIGHT. IN NO EVENT SHALL THE
COPYRIGHT HOLDER BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
INCLUDING ANY GENERAL, SPECIAL, INDIRECT, INCIDENTAL, OR CONSEQUENTIAL
DAMAGES, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF THE USE OR INABILITY TO USE THE FONT SOFTWARE OR FROM
OTHER DEALINGS IN THE FONT SOFTWARE.
"""


def ascii_only(text):
    """Notices ship as plain ASCII, like the rest of the published text."""
    return text.replace("–", "-").replace("—", "-").encode("ascii", "replace").decode("ascii")


def copyright_of(font):
    return font["name"].getDebugName(0) or ""


def refuse_reserved_names(source, font):
    """The OFL reserves a name only where one follows the copyright notice. None does today; stop if one ever does."""
    notice = copyright_of(font)
    if "reserved font name" in notice.lower():
        sys.exit(f"{source} declares a Reserved Font Name ({notice!r}); its instances would need new names.")


def set_names(font, family, postscript):
    """Family in IDs 1, 16 and 21, face in 2, 17 and 22, on Windows and Mac records alike."""
    name = font["name"]
    version = (name.getDebugName(5) or "Version 1.0").replace("Version ", "").split(";")[0].strip()

    # Axis and instance names belong to the variable font this no longer is.
    name.names = [record for record in name.names if record.nameID < 256]
    for record_id in (1, 2, 3, 4, 6, 16, 17, 21, 22, 25):
        name.removeNames(nameID=record_id)

    values = {
        1: family,
        2: "Regular",
        3: f"{version};STX1;{postscript}",
        4: family,
        6: postscript,
        16: family,
        17: "Regular",
        21: family,
        22: "Regular",
    }
    for record_id, value in values.items():
        name.setName(value, record_id, *WINDOWS)
        name.setName(value, record_id, 1, 0, 0)


def build_instance(source_dir, source, output, family, location):
    font = TTFont(source_dir / source, recalcTimestamp=False)
    refuse_reserved_names(source, font)

    static = instancer.instantiateVariableFont(font, location, inplace=False, updateFontNames=False)
    static.flavor = None

    width = location.get("wdth", 100)
    static["OS/2"].usWeightClass = location["wght"]
    # Nearest CSS stretch class; each face is a family of one, so this only has to be sensible.
    static["OS/2"].usWidthClass = 5 if width < 106 else 6 if width < 119 else 7

    # One face per family: it is that family's regular face, whatever its weight. Names 21 and 22 are
    # written explicitly, so the WWS bit, which says they are not needed, is cleared.
    selection = static["OS/2"].fsSelection & ~(FS_ITALIC | FS_BOLD | FS_WWS)
    static["OS/2"].fsSelection = selection | FS_REGULAR
    static["head"].macStyle = 0

    set_names(static, family, pathlib.Path(output).stem)
    static.save(OUTPUT / output, reorderTables=True)
    return output


def convert(source_dir, source, output):
    font = TTFont(source_dir / source, recalcTimestamp=False)
    refuse_reserved_names(source, font)
    font.flavor = None
    font.save(OUTPUT / output, reorderTables=True)
    return output


def write_licence(source_dir, source, output):
    font = TTFont(source_dir / source)
    notice = ascii_only(copyright_of(font))
    text = (f"{notice}\n\n"
            "This Font Software is licensed under the SIL Open Font License, Version 1.1.\n"
            "This license is copied below, and is also available with a FAQ at:\n"
            "https://openfontlicense.org\n\n\n"
            "-----------------------------------------------------------\n" + OFL)
    (OUTPUT / output).write_text(text, encoding="ascii", newline="\n")
    return output


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--source", type=pathlib.Path, default=DEFAULT_SOURCE,
                        help="System-X's src/assets/fonts folder")
    args = parser.parse_args()

    if not args.source.is_dir():
        sys.exit(f"No fonts at {args.source}; pass --source with System-X's src/assets/fonts folder.")

    OUTPUT.mkdir(parents=True, exist_ok=True)
    written = [build_instance(args.source, *instance) for instance in INSTANCES]
    written += [convert(args.source, *conversion) for conversion in CONVERSIONS]
    written += [write_licence(args.source, *licence) for licence in LICENCES]

    for name in written:
        print(f"wrote {name:30} {(OUTPUT / name).stat().st_size:>8} bytes")


if __name__ == "__main__":
    main()
