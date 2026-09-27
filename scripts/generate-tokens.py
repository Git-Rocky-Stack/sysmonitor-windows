#!/usr/bin/env python3
"""Writes the console's palette, src/SysMonitor.App/Styles/Console/Tokens.xaml, from System-X's values.

Every token is transcribed from system-x-app@eaba14b's src/styles.css (Night Ops :541-713, Day Shift :723-891),
and each one becomes a Color and a Brush of the same name in three theme dictionaries: Default (Night Ops),
Light (Day Shift) and HighContrast (the user's system colours, decoration dropped). The composed brushes - the
faceplate's face and key lights, caps, stripes, bolts, wells, lamps - are built here from the same values.

Inside Night Ops and Day Shift every brush and gradient stop writes its colour out. A brush that named its colour
from the same theme with {StaticResource} came out in Night Ops' colour on Day Shift when the shift was switched
while the app ran (measured by the UI smoke run), so the script never writes one; ConsoleDictionaryTests fails if
the file ever has one. High Contrast names the system colours, which is what it shows.

Tokens.xaml is only ever written by this script. CI runs it with --check and fails when the two disagree, so a
colour is changed here, the script run, and both committed.

Usage:
    python scripts/generate-tokens.py            # write Tokens.xaml
    python scripts/generate-tokens.py --check    # fail if Tokens.xaml is not what the script writes
"""

import argparse
import pathlib
import re
import sys

TOKENS = pathlib.Path(__file__).resolve().parent.parent / 'src' / 'SysMonitor.App' / 'Styles' / 'Console' / 'Tokens.xaml'

def rgba(r, g, b, a):
    spelled = f"{a:g}".lstrip("0") if a < 1 else "1"
    return (r, g, b, int(a * 255 + 0.5), f"rgba({r}, {g}, {b}, {spelled})")

def hexc(s):
    s = s.lstrip('#')
    if len(s) == 3: s = ''.join(c * 2 for c in s)
    return (int(s[0:2], 16), int(s[2:4], 16), int(s[4:6], 16), 255, None)

def lit(c):
    r, g, b, a = c[:4]
    return f"#{r:02X}{g:02X}{b:02X}" if a == 255 else f"#{a:02X}{r:02X}{g:02X}{b:02X}"

def css(c):
    return c[4]

def fade(c):
    return (c[0], c[1], c[2], 0, None)

def over(top, bottom):
    """top (translucent) composited over opaque bottom."""
    a = top[3] / 255
    return tuple(int(top[i] * a + bottom[i] * (1 - a) + 0.5) for i in range(3)) + (255, None)

W, WT, GT, HL, HLT, BF, BT, TR = ('SystemColorWindowColor', 'SystemColorWindowTextColor', 'SystemColorGrayTextColor',
    'SystemColorHighlightColor', 'SystemColorHighlightTextColor', 'SystemColorButtonFaceColor',
    'SystemColorButtonTextColor', 'Transparent')

# (group comment, [(name, night, day, high contrast, note)])
GROUPS = [
 ("Surfaces: the chassis, darkest first (:545-552, Day :726-732).", [
  ("Void", hexc('000000'), hexc('000000'), W),
  ("Carbon950", hexc('050505'), hexc('c8c8c4'), W),
  ("Carbon900", hexc('0d0d0d'), hexc('d2d2ce'), W),
  ("Carbon850", hexc('101010'), hexc('cecec9'), W),
  ("Carbon800", hexc('141414'), hexc('d8d8d4'), W),
  ("Carbon750", hexc('1a1a1a'), hexc('dededa'), W),
  ("Carbon700", hexc('262626'), hexc('b0b0ac'), W),
  ("Anthracite", hexc('3a3a3a'), hexc('96968f'), W),
 ]),
 ("Text, brightest first (:555-577, Day :734-738). SilverMute is set by the lightest ground it prints on, the\nstripe grain; Graphite only ever paints a lamp that is not lit.", [
  ("Platinum", hexc('f5f5f5'), hexc('1a1a1a'), WT),
  ("SilverBright", hexc('d1d1d1'), hexc('2e2e2c'), WT),
  ("Silver", hexc('b3b3b3'), hexc('44443f'), WT),
  ("SilverMute", hexc('9c9c9c'), hexc('4f4f4b'), WT),
  ("Graphite", hexc('808080'), hexc('96968f'), GT),
 ]),
 ("Armed red: LIVE, not error (:579-590, Day :766-770). Armed and ArmedDeep never change; ArmedLit deepens on\nDay Shift for contrast on silver; ArmedDisplay is the bright form for armed text on displays, which stay dark.", [
  ("Armed", hexc('aa2024'), hexc('aa2024'), HL),
  ("ArmedLit", hexc('e8343a'), hexc('a31e23'), HL),
  ("ArmedDisplay", hexc('e8343a'), hexc('e8343a'), HL),
  ("ArmedDeep", hexc('7f171a'), hexc('7f171a'), HL),
  ("ArmedEdge", rgba(232, 52, 58, .35), rgba(163, 30, 35, .45), HL),
  ("ArmedGlow", rgba(232, 52, 58, .22), rgba(163, 30, 35, .2), TR),
  ("ArmedSoft", rgba(170, 32, 36, .12), rgba(170, 32, 36, .12), TR),
  ("ArmedFg", hexc('fff3f0'), hexc('fff3f0'), HLT),
 ]),
 ("Chrome: the polished bits, used sparingly (:592-595, Day :774).", [
  ("Chrome", hexc('e6e6e6'), hexc('e6e6e6'), WT),
  ("ChromeEdge", rgba(230, 230, 230, .28), rgba(0, 0, 0, .3), WT),
  ("ChromeSoft", rgba(230, 230, 230, .1), rgba(230, 230, 230, .1), TR),
 ]),
 ("LEDs, the same in both shifts (:597-613). Each state is a word first; in High Contrast the word is all of it.", [
  ("LedGo", hexc('41e25e'), hexc('41e25e'), HL),
  ("LedHold", hexc('ffb000'), hexc('ffb000'), HL),
  ("LedWarn", hexc('ff4438'), hexc('ff4438'), HL),
  ("LedNoGo", hexc('c8453e'), hexc('c8453e'), HL),
  ("LedScope", hexc('58c4bc'), hexc('58c4bc'), HL),
  ("LedOrange", hexc('ff7a1a'), hexc('ff7a1a'), HL),
  ("GoSoft", rgba(65, 226, 94, .1), rgba(65, 226, 94, .1), TR),
  ("HoldSoft", rgba(255, 176, 0, .1), rgba(255, 176, 0, .1), TR),
  ("WarnSoft", rgba(255, 68, 56, .12), rgba(255, 68, 56, .12), TR),
  ("NoGoSoft", rgba(200, 69, 62, .12), rgba(200, 69, 62, .12), TR),
  ("ScopeSoft", rgba(88, 196, 188, .1), rgba(88, 196, 188, .1), TR),
  ("GoEdge", rgba(65, 226, 94, .35), rgba(65, 226, 94, .35), WT),
  ("HoldEdge", rgba(255, 176, 0, .35), rgba(255, 176, 0, .35), WT),
  ("WarnEdge", rgba(255, 68, 56, .4), rgba(255, 68, 56, .4), WT),
  ("NoGoEdge", rgba(200, 69, 62, .4), rgba(200, 69, 62, .4), WT),
  ("ScopeEdge", rgba(88, 196, 188, .32), rgba(88, 196, 188, .32), WT),
 ]),
 ("State as text: the LED colour, safe to print on the chassis (:615-635, Day :740-764). Day Shift darkens each\nalong its own hue to clear 4.5:1 on Carbon950; warn and nogo share one red there, which is why a warn rail\nblinks and the word is always printed. Inside a well or display the Default values apply: the LEDs.", [
  ("StateGo", hexc('41e25e'), hexc('135724'), HL),
  ("StateHold", hexc('ffb000'), hexc('6f4700'), HL),
  ("StateWarn", hexc('ff4438'), hexc('9e120a'), HL),
  ("StateNoGo", hexc('c8453e'), hexc('9e120a'), HL),
  ("StateExec", hexc('58c4bc'), hexc('1d5854'), HL),
 ]),
 ("Rails: the light pipe down a plate's mounting edge (:1149-1206), from the palette steps that survive Day Shift\n(Night :261-309, Day :825-874). Wired to the LEDs, a rail would put #41E25E on a near-white plate by day.", [
  ("RailGo", hexc('41e25e'), hexc('0f5c2c'), HL),
  ("RailHold", hexc('ffb000'), hexc('6f4700'), HL),
  ("RailWarn", hexc('ff4438'), hexc('a3140c'), HL),
  ("RailNoGo", hexc('c8453e'), hexc('870f09'), HL),
  ("RailExec", hexc('58c4bc'), hexc('1a524e'), HL),
 ]),
 ("Displays stay dark: defined once, the same in both shifts (:637-645).", [
  ("DisplayBgTop", hexc('040404'), hexc('040404'), W),
  ("DisplayBgBottom", hexc('070707'), hexc('070707'), W),
  ("DisplayBorder", rgba(0, 0, 0, .8), rgba(0, 0, 0, .8), WT),
  ("DisplayFg", hexc('b3b3b3'), hexc('b3b3b3'), WT),
  ("DisplayFgMute", hexc('8a8a86'), hexc('8a8a86'), WT),
  ("Phosphor", hexc('41e25e'), hexc('41e25e'), HL),
  ("PhosphorDim", hexc('2e5237'), hexc('2e5237'), GT),
  ("Scrim", rgba(0, 0, 0, .72), rgba(0, 0, 0, .72), W),
 ]),
 ("Hairlines (:647-649, Day :772-773).", [
  ("Hairline", rgba(255, 255, 255, .08), rgba(0, 0, 0, .1), WT),
  ("HairlineStrong", rgba(255, 255, 255, .16), rgba(0, 0, 0, .2), WT),
 ]),
 ("Faceplate and module plate: brushed aluminium, top to bottom, with its key light, edge, lip and underside\n(:651-659, Day :777-784).", [
  ("Plate1", hexc('1c1c1c'), hexc('f5f5f3'), W),
  ("Plate2", hexc('151515'), hexc('eaeae7'), W),
  ("Plate3", hexc('101010'), hexc('e0e0dc'), W),
  ("Plate4", hexc('0c0c0c'), hexc('d8d8d4'), W),
  ("PlateKey", rgba(255, 255, 255, .04), rgba(255, 255, 255, .55), TR),
  ("PlateEdge", rgba(255, 255, 255, .06), rgba(0, 0, 0, .12), WT),
  ("PlateLip", rgba(255, 255, 255, .1), rgba(255, 255, 255, .92), TR),
  ("PlateUnder", rgba(0, 0, 0, .7), rgba(0, 0, 0, .16), TR),
 ]),
 ("Machined caps: buttons, chips, switches (:661-665, Day :786-790).", [
  ("Cap1", hexc('1f1f1f'), hexc('f7f7f5'), BF),
  ("Cap2", hexc('171717'), hexc('ebebe8'), BF),
  ("Cap3", hexc('121212'), hexc('dededa'), BF),
  ("CapKey", rgba(255, 255, 255, .06), rgba(255, 255, 255, .75), TR),
  ("CapEdge", rgba(255, 255, 255, .04), rgba(0, 0, 0, .1), BT),
 ]),
 ("Brushed stripe header, and its grain: directional machine brushing on a 3px period (:667-672, Day :792-797).", [
  ("Stripe1", hexc('2a2a2a'), hexc('f0f0ed'), W),
  ("Stripe2", hexc('1f1f1f'), hexc('e0e0dc'), W),
  ("Stripe3", hexc('181818'), hexc('d5d5d1'), W),
  ("StripeGrainA", rgba(255, 255, 255, .035), rgba(0, 0, 0, .035), TR),
  ("StripeGrainB", rgba(255, 255, 255, .012), rgba(0, 0, 0, .012), TR),
  ("StripeGrainC", rgba(0, 0, 0, .06), rgba(255, 255, 255, .45), TR),
 ]),
 ("Edge light along a faceplate's top lip (:674-675, Day :799-800).", [
  ("EdgeLight", rgba(230, 230, 230, .18), rgba(255, 255, 255, .6), TR),
  ("EdgeLightMid", rgba(230, 230, 230, .32), rgba(255, 255, 255, .95), TR),
 ]),
 ("Hex socket-cap bolts, lit from the top left (:677-682, Day :802-807). Decoration: gone in High Contrast.", [
  ("Hex1", hexc('a4a4a4'), hexc('ffffff'), TR),
  ("Hex2", hexc('7c7c7c'), hexc('d8d8d4'), TR),
  ("Hex3", hexc('525252'), hexc('aaaaa4'), TR),
  ("Hex4", hexc('323232'), hexc('868680'), TR),
  ("Hex5", hexc('181818'), hexc('5c5c58'), TR),
  ("Hex6", hexc('0a0a0a'), hexc('44443f'), TR),
 ]),
 ("Selection, focus and the scrollbar thumb (:684-686, :710-712, Day :775, :809-810).", [
  ("SelectionBg", rgba(170, 32, 36, .35), rgba(170, 32, 36, .35), HL),
  ("Focus", hexc('e6e6e6'), hexc('2a2a2a'), WT),
  ("ScrollbarThumb", rgba(170, 32, 36, .58), rgba(170, 32, 36, .5), HL),
  ("ScrollbarThumbHover", rgba(232, 52, 58, .82), rgba(163, 30, 35, .8), HL),
 ]),
 ("Recipe shading: the edges and lines the recipes draw with, which change with the shift. A faceplate's side\nlight and shade (inset 1px, :1056-1057, Day :1068-1069); the seam under its stripe and the light below it\n(:1526-1529, Day :1532-1535); a bolt's drop shadows (:1579-1580, Day :1584-1585), which WinUI cannot blur, drawn\nas two copies 1px and 2px down whose alphas follow the blurred shadow's first rows.", [
  ("FaceplateSideLight", rgba(255, 255, 255, .025), rgba(255, 255, 255, .5), TR),
  ("FaceplateSideShade", rgba(0, 0, 0, .4), rgba(0, 0, 0, .08), TR),
  ("StripeSeam", rgba(0, 0, 0, .8), rgba(0, 0, 0, .18), WT),
  ("StripeSeamLight", rgba(255, 255, 255, .03), rgba(255, 255, 255, .4), TR),
  ("HexShadowNear", rgba(0, 0, 0, .7), rgba(0, 0, 0, .2), TR),
  ("HexShadowFar", rgba(0, 0, 0, .45), rgba(0, 0, 0, .15), TR),
 ]),
 ("A well's edge and the light catching its lower lip (:1454-1456), the same in both shifts: a well is dark in\nboth.", [
  ("WellEdge", rgba(0, 0, 0, .7), rgba(0, 0, 0, .7), WT),
  ("WellLip", rgba(255, 255, 255, .03), rgba(255, 255, 255, .03), TR),
 ]),
 ("A lamp's cap edge, the light along its top and the shade along its bottom, and the word an unlit lamp shows,\nwhich stays Night Ops graphite because a lamp is dark in both shifts (:1692-1697, :1265); a VU meter's unlit\nsegment (:1915).", [
  ("LampEdge", rgba(255, 255, 255, .06), rgba(255, 255, 255, .06), WT),
  ("LampLip", rgba(255, 255, 255, .06), rgba(255, 255, 255, .06), TR),
  ("LampUnder", rgba(0, 0, 0, .8), rgba(0, 0, 0, .8), TR),
  ("LampOffForeground", hexc('808080'), hexc('808080'), GT),
  ("VuSegmentOff", hexc('131313'), hexc('131313'), GT),
 ]),
]

def colours(theme):
    return {name: (night if theme == 'night' else day) for _, rows in GROUPS for name, night, day, hc in rows}

def hc_of():
    return {name: hc for _, rows in GROUPS for name, night, day, hc in rows}

# Composed brushes. Each returns XAML for a theme ('night'|'day'), given that theme's colours.
def lin(palette, key, stops, start='0,0', end='0,1', extra=''):
    head = f'<LinearGradientBrush x:Key="{key}" StartPoint="{start}" EndPoint="{end}"'
    if extra:
        lines = [head, ' ' * len('<LinearGradientBrush ') + extra.strip() + '>']
    else:
        lines = [head + '>']
    for off, col in stops:
        v = lit(palette[col]) if isinstance(col, str) else lit(col)
        lines.append(f'    <GradientStop Offset="{off}" Color="{v}"/>')
    lines.append('</LinearGradientBrush>')
    return lines

def rad(palette, key, center, rx, ry, stops):
    lines = [f'<RadialGradientBrush x:Key="{key}" Center="{center}" GradientOrigin="{center}"',
             ' ' * len('<RadialGradientBrush ') + f'RadiusX="{rx}" RadiusY="{ry}">']
    for off, col in stops:
        v = lit(palette[col]) if isinstance(col, str) else lit(col)
        lines.append(f'    <GradientStop Offset="{off}" Color="{v}"/>')
    lines.append('</RadialGradientBrush>')
    return lines

def composed(theme):
    c = colours(theme)
    night = theme == 'night'
    out = []
    def add(comment, lines):
        out.append(('comment', comment)); out.append(('lines', lines))
    add("Faceplate (:1037): the face, top to bottom, and its key light from above left; the second key light, over\nthe top right corner's 140 x 60 (::after, :1098); the edge light along the top lip, inset to clear the bolts\n(::before, :1078).",
        lin(c, 'FaceplateFaceBrush', [(0, 'Plate1'), (0.35, 'Plate2'), (0.7, 'Plate3'), (1, 'Plate4')])
        + rad(c, 'FaceplateKeyLightBrush', '0.3,-0.1', 0.8, 0.6, [(0, 'PlateKey'), (0.6, fade(c['PlateKey']))])
        + rad(c, 'FaceplateCornerLightBrush', '0.8,0', 1.131, 1.414, [(0, 'PlateKey'), (0.7, fade(c['PlateKey']))])
        + lin(c, 'FaceplateEdgeLightBrush', [(0, fade(c['EdgeLight'])), (0.2, 'EdgeLight'), (0.5, 'EdgeLightMid'), (0.8, 'EdgeLight'), (1, fade(c['EdgeLight']))], '0,0', '1,0'))
    armed = c['ArmedSoft']
    add("Module plate (:1136); armed, staged for a destructive run, lays ArmedSoft over the same face (:1206),\ncomposited here because one brush cannot layer two.",
        lin(c, 'PlateFaceBrush', [(0, 'Plate1'), (1, 'Plate3')])
        + lin(c, 'PlateArmedFaceBrush', [(0, over(armed, c['Plate1'])), (1, over(armed, c['Plate3']))]))
    add("Cap: the face and its key light (:1467, and every button, :2062), and a chip's flatter face (:2176).",
        lin(c, 'CapFaceBrush', [(0, 'Cap1'), (0.6, 'Cap2'), (1, 'Cap3')])
        + rad(c, 'CapKeyLightBrush', '0.5,0', 0.8, 0.5, [(0, 'CapKey'), (0.6, fade(c['CapKey']))])
        + lin(c, 'ChipFaceBrush', [(0, 'Cap1'), (1, 'Cap3')]))
    add("Stripe header (:1503): the face, and the grain as hard 1px bands repeating every 3px.",
        lin(c, 'StripeFaceBrush', [(0, 'Stripe1'), (0.55, 'Stripe2'), (1, 'Stripe3')])
        + lin(c, 'StripeGrainBrush', [(0, 'StripeGrainA'), (0.3333, 'StripeGrainA'), (0.3333, 'StripeGrainB'), (0.6667, 'StripeGrainB'), (0.6667, 'StripeGrainC'), (1, 'StripeGrainC')],
              '0,0', '3,0', ' MappingMode="Absolute" SpreadMethod="Repeat"'))
    sock = [hexc('242424'), hexc('0e0e0e'), hexc('000000')] if night else [hexc('8a8a84'), hexc('5c5c58'), hexc('3a3a36')]
    ring = rgba(255, 255, 255, .04)
    add("Hex bolt (:1557): the head, lit from 28% 22% out to the farthest corner; the faint machined ring round it,\nthe same in both shifts (:1565); and the socket, lit from the opposite side (::after, :1589, Day :1602).",
        rad(c, 'HexFaceBrush', '0.28,0.22', 1.061, 1.061, [(0, 'Hex1'), (0.12, 'Hex2'), (0.3, 'Hex3'), (0.55, 'Hex4'), (0.8, 'Hex5'), (1, 'Hex6')])
        + rad(c, 'HexRingBrush', '0.5,0.5', 0.707, 0.707, [(0.62, fade(ring)), (0.68, ring), (0.75, fade(ring))])
        + rad(c, 'HexSocketBrush', '0.72,0.78', 1.061, 1.061, [(0, sock[0]), (0.4, sock[1]), (1, sock[2])]))
    chrome = [hexc('fbfbfb'), hexc('e6e6e6'), hexc('c4c4c4')] if night else [hexc('303030'), hexc('1a1a1a'), hexc('0a0a0a')]
    add("Armed cap: consequential commands only, the same in both shifts (:2093). Chrome cap: the single polished\ncall to action, inverting to black gloss on Day Shift (:2116, Day :2131).",
        lin(c, 'ArmedCapFaceBrush', [(0, hexc('c8333a')), (0.55, 'Armed'), (1, 'ArmedDeep')])
        + lin(c, 'ChromeCapFaceBrush', [(0, chrome[0]), (0.55, chrome[1]), (1, chrome[2])]))
    add("Displays, wells and the annunciator strip: dark in both shifts (:1427, :1451, :1946).",
        lin(c, 'DisplayFaceBrush', [(0, 'DisplayBgTop'), (1, 'DisplayBgBottom')])
        + lin(c, 'WellFaceBrush', [(0, hexc('080808')), (1, hexc('0a0a0a'))])
        + lin(c, 'AnnunciatorFaceBrush', [(0, hexc('0e0e0e')), (1, hexc('090909'))]))
    shade, deep, side = rgba(0, 0, 0, .8), rgba(0, 0, 0, .9), rgba(0, 0, 0, .5)
    add("The recess: WinUI has no inset shadow, so a well's is drawn as gradients over its face - from the top edge\n(inset 0 2px 4px, :1455) and in from each side (inset 1px 0 2px, :1457-1458) - and a display's deeper one\nfrom its top (inset 0 2px 5px, :1845). Each runs across the band it is drawn in.",
        lin(c, 'WellShadeTopBrush', [(0, shade), (1, fade(shade))])
        + lin(c, 'WellShadeLeftBrush', [(0, side), (1, fade(side))], '0,0', '1,0')
        + lin(c, 'WellShadeRightBrush', [(0, side), (1, fade(side))], '1,0', '0,0')
        + lin(c, 'DisplayShadeTopBrush', [(0, deep), (1, fade(deep))]))
    glass = rgba(255, 255, 255, .05)
    add("An LCD's cover glass: a sheen from the top left, gone by 38% of the way across its 168 degree fall (:1852).",
        lin(c, 'LcdGlassBrush', [(0, glass), (0.38, fade(glass))], '0.396,0', '0.604,1'))
    add("The busy sweep: a band of armed glow down a dark well, brightest at its middle (.scanline::after, :2521).\nIt only runs inside a well, so it shows Night Ops' glow in both shifts.",
        lin(c, 'ScanBandBrush', [(0, fade(c['ArmedGlow'])), (0.5, 'ArmedGlow'), (1, fade(c['ArmedGlow']))]))
    lamps = [('LampFaceBrush', '131313', '0a0a0a'), ('LampGoFaceBrush', '0f1a12', '070b08'), ('LampHoldFaceBrush', '1a1508', '0b0905'),
             ('LampWarnFaceBrush', '1c0c0a', '0c0605'), ('LampNoGoFaceBrush', '1a0b0a', '0b0605'), ('LampExecFaceBrush', '08191a', '050b0b'),
             ('LampArmedFaceBrush', '1c0a0b', '0c0505')]
    lines = []
    for key, top, bottom in lamps:
        lines += lin(c, key, [(0, hexc(top)), (1, hexc(bottom))])
    add("Lamp caps, unlit and in each lit state, dark in both shifts (:1675, lit :1715-1779).", lines)
    return out

COMPOSED_HC = {  # key -> system colour (or Transparent) for the High Contrast theme
    'FaceplateFaceBrush': W, 'FaceplateKeyLightBrush': TR, 'FaceplateCornerLightBrush': TR, 'FaceplateEdgeLightBrush': TR,
    'PlateFaceBrush': W, 'PlateArmedFaceBrush': W,
    'CapFaceBrush': BF, 'CapKeyLightBrush': TR, 'ChipFaceBrush': BF,
    'StripeFaceBrush': W, 'StripeGrainBrush': TR,
    'HexFaceBrush': TR, 'HexRingBrush': TR, 'HexSocketBrush': TR,
    'ArmedCapFaceBrush': HL, 'ChromeCapFaceBrush': BF,
    'DisplayFaceBrush': W, 'WellFaceBrush': W, 'AnnunciatorFaceBrush': W,
    'WellShadeTopBrush': TR, 'WellShadeLeftBrush': TR, 'WellShadeRightBrush': TR, 'DisplayShadeTopBrush': TR,
    'LcdGlassBrush': TR, 'ScanBandBrush': TR,
    'LampFaceBrush': W, 'LampGoFaceBrush': W, 'LampHoldFaceBrush': W, 'LampWarnFaceBrush': W,
    'LampNoGoFaceBrush': W, 'LampExecFaceBrush': W, 'LampArmedFaceBrush': W,
}

# Solid pairs that are not palette tokens but recipe colours that change with the shift.
EXTRA = [("ChromeCapForeground", hexc('0a0a0a'), hexc('f5f5f5'), BT)]

I = '            '
def note(text):
    import textwrap
    lines = textwrap.wrap(text.replace('\n', ' '), width=118 - len(I) - 5 - 4)
    return f'{I}<!-- ' + ('\n' + I + '     ').join(lines) + ' -->'

def theme_block(theme):
    out = []
    groups = GROUPS + [("Chrome cap text: black on the polished face, platinum on Day Shift's black gloss (:2125, Day :2135).", EXTRA)]
    for comment, rows in groups:
        out.append(note(comment))
        for name, night, day, hc in rows:
            if theme == 'hc':
                if hc == TR:
                    out.append(f'{I}<Color x:Key="{name}Color">Transparent</Color>')
                    out.append(f'{I}<SolidColorBrush x:Key="{name}Brush" Color="Transparent"/>')
                else:
                    out.append(f'{I}<StaticResource x:Key="{name}Color" ResourceKey="{hc}"/>')
                    opacity = ' Opacity="0.72"' if name == 'Scrim' else ''
                    out.append(f'{I}<SolidColorBrush x:Key="{name}Brush" Color="{{ThemeResource {hc}}}"{opacity}/>')
            else:
                col = night if theme == 'night' else day
                trail = f' <!-- {css(col)} -->' if css(col) else ''
                out.append(f'{I}<Color x:Key="{name}Color">{lit(col)}</Color>{trail}')
                out.append(f'{I}<SolidColorBrush x:Key="{name}Brush" Color="{lit(col)}"/>')
        out.append('')
    if theme == 'hc':
        out.append(note("Composed faces become solid system colours, and decoration - key lights, grain, edge light, bolts - goes."))
        for key, sysc in COMPOSED_HC.items():
            if sysc == TR:
                out.append(f'{I}<SolidColorBrush x:Key="{key}" Color="Transparent"/>')
            else:
                out.append(f'{I}<SolidColorBrush x:Key="{key}" Color="{{ThemeResource {sysc}}}"/>')
    else:
        items = composed(theme)
        keys = []
        for kind, val in items:
            if kind == 'comment':
                out.append(note(val))
            else:
                for line in val:
                    out.append(I + line)
                    m = re.search(r'x:Key="([^"]+)"', line)
                    if m: keys.append(m.group(1))
                out.append('')
        assert keys == list(COMPOSED_HC), (keys, list(COMPOSED_HC))
        if out[-1] == '': out.pop()
    return out

HEADER = '''<?xml version="1.0" encoding="utf-8"?>
<!--
    The Command Console's palette, transcribed from System-X (system-x-app@eaba14b, src/styles.css; the line
    numbers below are that file's).

    Every token is a Color and a Brush of the same name, in each of three themes:

      Default       Night Ops, the dark shift. Dark lookups fall back to it, and so does everything inside a Well
                    or Display, which set their own theme to Dark - that is how displays stay dark on Day Shift.
      Light         Day Shift, silver anodised: a designed second skin, not an inversion. Only the chassis
                    material changes; LEDs, displays, armed red and geometry are the same.
      HighContrast  the user's system colours. Surfaces take the window colour, text the window text colour,
                    anything lit or armed the highlight colour, and decoration goes.

    Written by scripts/generate-tokens.py, which is where a colour changes; CI fails when this file and the
    script disagree. Colours are written here and nowhere else (ConsoleRecipeTests). Consumers ask with
    {ThemeResource}, so an element follows the theme it is shown in, and every theme defines the same keys
    (ThemeDictionaryParityTests). Inside a theme each brush writes its colour out rather than naming the Color
    beside it with {StaticResource}: brushes that named their colours came out in Night Ops' on Day Shift when
    the shift was switched while the app ran, where brushes that wrote them out followed it (the UI smoke run
    measured both). Code cannot follow a theme this way - Application.Current.Resources answers for the
    application's theme - so colours code needs live in one C# palette.
-->
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <ResourceDictionary.ThemeDictionaries>
'''

FOOTER = '''    </ResourceDictionary.ThemeDictionaries>

    <!-- Machined radii, the same in every shift (:697-702): plates and panels, insets, and the caps a lamp, chip or
         button is cut from - WinUI's ControlCornerRadius, restated so the instruments' dictionary needs nothing
         from WinUI's own to resolve. -->
    <CornerRadius x:Key="PlateCornerRadius">2</CornerRadius>
    <CornerRadius x:Key="InsetCornerRadius">3</CornerRadius>
    <CornerRadius x:Key="CapCornerRadius">4</CornerRadius>
    <CornerRadius x:Key="PillCornerRadius">9999</CornerRadius>

    <!-- Spacing, base 4 and Fibonacci-flavoured (:688-695). -->
    <x:Double x:Key="Space1">4</x:Double>
    <x:Double x:Key="Space2">8</x:Double>
    <x:Double x:Key="Space3">12</x:Double>
    <x:Double x:Key="Space4">20</x:Double>
    <x:Double x:Key="Space5">32</x:Double>
    <x:Double x:Key="Space6">52</x:Double>
    <x:Double x:Key="Space7">84</x:Double>

    <!-- A faceplate's body clears the corner bolts: 24 at the sides, deeper at the foot (:1117). Its stripe header
         is 36 high, padded 30 to clear the bolts (:1503). -->
    <Thickness x:Key="FaceplateBodyPadding">24,16,24,24</Thickness>
    <x:Double x:Key="StripeHeight">36</x:Double>
    <Thickness x:Key="StripePadding">30,0,30,0</Thickness>
</ResourceDictionary>
'''

def render():
    parts = [HEADER]
    parts.append('        <!-- ============================================================ NIGHT OPS -->\n        <ResourceDictionary x:Key="Default">\n')
    parts.append('\n'.join(theme_block('night')) + '\n')
    parts.append('        </ResourceDictionary>\n\n')
    parts.append('        <!-- ============================================================ DAY SHIFT -->\n        <ResourceDictionary x:Key="Light">\n')
    parts.append('\n'.join(theme_block('day')) + '\n')
    parts.append('        </ResourceDictionary>\n\n')
    parts.append('        <!-- ============================================================ HIGH CONTRAST -->\n        <ResourceDictionary x:Key="HighContrast">\n')
    parts.append('\n'.join(theme_block('hc')) + '\n')
    parts.append('        </ResourceDictionary>\n')
    parts.append(FOOTER)
    return ''.join(parts)


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    parser.add_argument('--check', action='store_true',
                        help='fail if the committed file is not what this script writes, instead of writing it')
    parser.add_argument('--out', type=pathlib.Path, default=TOKENS, help='where the palette goes (default: %(default)s)')
    args = parser.parse_args()

    text = render()
    if args.check:
        current = args.out.read_text(encoding='utf-8') if args.out.exists() else None
        if current != text:
            print(f'{args.out} is not what scripts/generate-tokens.py writes. Change the palette in the script, '
                  'run it, and commit both.', file=sys.stderr)
            return 1
        print(f'{args.out.name} matches its generator.')
        return 0

    args.out.write_text(text, encoding='utf-8', newline='\n')
    print(args.out, len(text.splitlines()), 'lines')
    return 0


if __name__ == '__main__':
    sys.exit(main())
