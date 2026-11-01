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

def saturate(c, amount=0.5):
    """CSS filter saturate(amount): the colour matrix the Filter Effects spec gives for it, alpha untouched."""
    r, g, b, a = c[:4]
    s = amount
    m = ((0.213 + 0.787 * s, 0.715 - 0.715 * s, 0.072 - 0.072 * s),
         (0.213 - 0.213 * s, 0.715 + 0.285 * s, 0.072 - 0.072 * s),
         (0.213 - 0.213 * s, 0.715 - 0.715 * s, 0.072 + 0.928 * s))
    out = [min(255, max(0, int(row[0] * r + row[1] * g + row[2] * b + 0.5))) for row in m]
    return (out[0], out[1], out[2], a, None)

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
 ("Selection, focus and the scrollbar thumb (:684-686, :710-712, Day :775, :809-810).", [
  ("SelectionBg", rgba(170, 32, 36, .35), rgba(170, 32, 36, .35), HL),
  ("Focus", hexc('e6e6e6'), hexc('2a2a2a'), WT),
  ("ScrollbarThumb", rgba(170, 32, 36, .58), rgba(170, 32, 36, .5), HL),
  ("ScrollbarThumbHover", rgba(232, 52, 58, .82), rgba(163, 30, 35, .8), HL),
 ]),
 ("Recipe shading: the edges and lines the recipes draw with, which change with the shift. A faceplate's side\nlight and shade (inset 1px, :1056-1057, Day :1068-1069); the seam under its stripe and the light below it\n(:1526-1529, Day :1532-1535); and the drop a screw's head casts into its counterbore (:1579, Day :1584), which\nWinUI cannot blur, drawn as a copy of the head a pixel down.", [
  ("FaceplateSideLight", rgba(255, 255, 255, .025), rgba(255, 255, 255, .5), TR),
  ("FaceplateSideShade", rgba(0, 0, 0, .4), rgba(0, 0, 0, .08), TR),
  ("StripeSeam", rgba(0, 0, 0, .8), rgba(0, 0, 0, .18), WT),
  ("StripeSeamLight", rgba(255, 255, 255, .03), rgba(255, 255, 255, .4), TR),
  ("HexShadowNear", rgba(0, 0, 0, .7), rgba(0, 0, 0, .2), TR),
 ]),
 ("A well's edge and the light catching its lower lip (:1454-1456), the same in both shifts: a well is dark in\nboth.", [
  ("WellEdge", rgba(0, 0, 0, .7), rgba(0, 0, 0, .7), WT),
  ("WellLip", rgba(255, 255, 255, .03), rgba(255, 255, 255, .03), TR),
 ]),
 ("The bat-lever switch's edge, and the light along the top of its thumb unlit and armed (:2330, :2345, :2360).\nThe same in both shifts: a switch is a recess, and a recess is dark in both.", [
  ("SwitchEdge", rgba(0, 0, 0, .8), rgba(0, 0, 0, .8), WT),
  ("SwitchThumbLip", rgba(255, 255, 255, .16), rgba(255, 255, 255, .16), TR),
  ("SwitchThumbArmedLip", rgba(255, 255, 255, .28), rgba(255, 255, 255, .28), TR),
 ]),
 ("A form field's edge, the light catching its lower lip, its words and its placeholder (:2286-2302). The same in\nboth shifts: the stylesheet says a field is a recessed dark well in both, so its words stay Night Ops white on\nDay Shift too.", [
  ("FieldEdge", rgba(0, 0, 0, .75), rgba(0, 0, 0, .75), WT),
  ("FieldLip", rgba(255, 255, 255, .03), rgba(255, 255, 255, .03), TR),
  ("FieldForeground", hexc('f5f5f5'), hexc('f5f5f5'), WT),
  ("FieldPlaceholder", hexc('6a6a6a'), hexc('6a6a6a'), GT),
 ]),
 ("A lamp's cap edge, the light along its top and the shade along its bottom, and the word an unlit lamp shows,\nwhich stays Night Ops graphite because a lamp is dark in both shifts (:1692-1697, :1265); a VU meter's unlit\nsegment (:1915).", [
  ("LampEdge", rgba(255, 255, 255, .06), rgba(255, 255, 255, .06), WT),
  ("LampLip", rgba(255, 255, 255, .06), rgba(255, 255, 255, .06), TR),
  ("LampUnder", rgba(0, 0, 0, .8), rgba(0, 0, 0, .8), TR),
  ("LampOffForeground", hexc('808080'), hexc('808080'), GT),
  ("VuSegmentOff", hexc('131313'), hexc('131313'), GT),
 ]),
 ("Shadows cast outside a panel, which composition draws (CastShadow.cs): the faceplate's four, lighter on Day\nShift (:1059-1062, Day :1071-1074); a module plate's, and its lift when a pressable one is hovered, the same in\nboth shifts (:1144, :1241); a lamp cap's (:1697). High Contrast has none.", [
  ("FaceplateShadowEdge", rgba(0, 0, 0, .9), rgba(0, 0, 0, .2), TR),
  ("FaceplateShadowContact", rgba(0, 0, 0, .7), rgba(0, 0, 0, .16), TR),
  ("FaceplateShadowDrop", rgba(0, 0, 0, .55), rgba(0, 0, 0, .14), TR),
  ("FaceplateShadowAmbient", rgba(0, 0, 0, .4), rgba(0, 0, 0, .1), TR),
  ("PlateShadow", rgba(0, 0, 0, .5), rgba(0, 0, 0, .5), TR),
  ("PlateShadowRaised", rgba(0, 0, 0, .55), rgba(0, 0, 0, .55), TR),
  ("LampShadow", rgba(0, 0, 0, .8), rgba(0, 0, 0, .8), TR),
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
    recess = rgba(0, 0, 0, .85)
    add("The bat-lever switch (:2323): the recessed track, the inset shade over its top edge that WinUI has no\ninset shadow for (inset 0 2px 4px, :2331), the armed track it shows when it is on (:2350), and the machined\nthumb unlit and armed (::after, :2343, :2358). Dark in both shifts, as the recess is.",
        lin(c, 'SwitchTrackBrush', [(0, hexc('070707')), (1, hexc('0c0c0c'))])
        + lin(c, 'SwitchTrackArmedBrush', [(0, hexc('3b0d0f')), (1, hexc('240809'))])
        + lin(c, 'SwitchShadeTopBrush', [(0, recess), (1, fade(recess))])
        + lin(c, 'SwitchThumbBrush', [(0, hexc('2a2a2a')), (1, hexc('161616'))])
        + lin(c, 'SwitchThumbArmedBrush', [(0, hexc('c8333a')), (1, 'ArmedDeep')]))
    add("A form field (:2286): its recessed face, and the inset shade its top edge casts into it, which WinUI has no\ninset shadow for (inset 0 2px 4px, :2293). Dark in both shifts.",
        lin(c, 'FieldFaceBrush', [(0, hexc('070707')), (1, hexc('0a0a0a'))])
        + lin(c, 'FieldShadeTopBrush', [(0, recess), (1, fade(recess))]))
    out.extend(hardware(c, night))
    return out

def hardware(c, night):
    """The hardware realism: brushed aluminium, stainless socket-cap screws, glass over every readout, LED lenses
    and the deflection of caps. Light falling on a surface, so every one is a gradient; all of it decoration, so
    High Contrast drops it (HardwareRealismTests)."""
    out = []
    def add(comment, lines):
        out.append(('comment', comment)); out.append(('lines', lines))
    clear = rgba(0, 0, 0, 0)
    W_ = lambda a: rgba(255, 255, 255, a)
    K_ = lambda a: rgba(0, 0, 0, a)
    hard = lambda bands: [(o, col) for start, end, col in bands for o in (start, end)]
    absolute_y = lambda period: ('0,0', f'0,{period}', ' MappingMode="Absolute" SpreadMethod="Repeat"')
    absolute_x = lambda period: ('0,0', f'{period},0', ' MappingMode="Absolute" SpreadMethod="Repeat"')

    # Brushed aluminium. Two grains of horizontal brushing, 7 and 3 pixels apart - periods with no common factor,
    # so the lines never fall into step and read as metal rather than ruling - and an anisotropic sheen across the
    # face, the long soft highlight a brushed panel throws along its grain. Faint by design: a texture over the
    # shift's own face colour, never a colour of its own. Day Shift's silver catches more light along the grain.
    hi, lo = (W_(.045), K_(.07)) if night else (W_(.09), K_(.035))
    fine_hi, fine_lo = (W_(.025), K_(.04)) if night else (W_(.06), K_(.02))
    y7, y7end, y_extra = absolute_y(7)
    y3, y3end, _ = absolute_y(3)
    sheen = [(0, clear), (0.16, W_(.035 if night else .3)), (0.34, clear), (0.6, W_(.02 if night else .18)),
             (0.78, clear), (1, K_(.06 if night else .04))]
    add("Brushed aluminium over every panel's face: two grains of horizontal brushing on coprime periods, 7 and 3,\nand the anisotropic sheen a brushed panel throws along its grain. A faint texture, never a colour.",
        lin(c, 'BrushedGrainBrush', hard([(0, 0.1429, hi), (0.1429, 0.4286, clear), (0.4286, 0.5714, lo),
                                          (0.5714, 0.7143, W_(hi[3] / 255 / 2)), (0.7143, 1, clear)]), y7, y7end, y_extra)
        + lin(c, 'BrushedGrainFineBrush', hard([(0, 0.3333, fine_hi), (0.3333, 0.6667, clear), (0.6667, 1, fine_lo)]),
              y3, y3end, y_extra)
        + lin(c, 'BrushedSheenBrush', sheen, '0,0', '1,0'))

    # Stainless socket-cap screws, the same steel in both shifts; only the counterbore they sit in is lighter on
    # Day Shift's silver. The head is lit from the top left; its chamfered rim bright where it faces the light; the
    # hex socket a dark floor inside a wall that is shadowed on the near side and lit on the far one.
    add("Stainless socket-cap screws (every panel's corners): the round head lit from the top left, its chamfered\nrim, the hex socket's floor and its wall, a polished specular, and the counterbore cut into the panel round it.",
        rad(c, 'StainlessHeadBrush', '0.36,0.3', 0.78, 0.78, [(0, hexc('f6f8fa')), (0.3, hexc('c9ced3')), (0.66, hexc('878d93')), (1, hexc('4a4f54'))])
        + lin(c, 'StainlessChamferBrush', [(0, hexc('ffffff')), (0.45, hexc('9aa0a6')), (1, hexc('2a2e32'))], '0.15,0', '0.85,1')
        + rad(c, 'StainlessSocketBrush', '0.6,0.62', 0.7, 0.7, [(0, hexc('2c3034')), (1, hexc('08090a'))])
        + lin(c, 'StainlessSocketWallBrush', [(0, hexc('040505')), (0.55, hexc('2b2e31')), (1, hexc('a7adb3'))], '0.2,0', '0.8,1')
        + rad(c, 'StainlessSpecularBrush', '0.32,0.26', 0.34, 0.3, [(0, W_(.9)), (0.45, W_(.35)), (1, W_(0))])
        + rad(c, 'BoltCounterboreBrush', '0.52,0.56', 0.5, 0.5, [(0, K_(.7 if night else .38)), (0.84, K_(.6 if night else .3)), (1, K_(0))]))

    # Glass over every readout, the same in both shifts: displays are dark in both, so the lens over them is too.
    add("Cover glass over every readout - LCDs, wells, displays, meters: a glare across the top of the curved lens,\nits lit edge, a vignette towards the bezel, and the LCD's pixel matrix showing through its reading.",
        lin(c, 'GlassGlareBrush', [(0, W_(.1)), (0.4, W_(.035)), (0.44, W_(0)), (1, W_(0))], '0.2,0', '0.45,1')
        + lin(c, 'GlassEdgeBrush', [(0, W_(.2)), (0.12, W_(.06)), (0.88, W_(.02)), (1, W_(.09))])
        + rad(c, 'GlassVignetteBrush', '0.5,0.45', 0.75, 0.9, [(0, K_(0)), (0.6, K_(0)), (1, K_(.42))])
        + lin(c, 'LcdPixelGridBrush', hard([(0, 0.6667, clear), (0.6667, 1, K_(.24))]), *absolute_y(3)))

    # LEDs: a lens held in a bezel. The bezel is dark plastic on Night Ops and bright chrome on Day Shift's silver;
    # the dome, the specular and the gloss are the lens, the same in both.
    bezel = [hexc('3c3c3c'), hexc('161616'), hexc('050505')] if night else [hexc('fbfbf9'), hexc('a3a39f'), hexc('5c5c58')]
    gap = (5, 5, 5, 235, None)
    x6, x6end, x_extra = absolute_x(6)
    add("LEDs: the bezel that holds each lens, the dome that shades it, its hot specular, the gloss over a lamp or a\nmeter's lens, and the dark gap between a meter's discrete LEDs, every 6 pixels.",
        lin(c, 'LedBezelBrush', [(0, bezel[0]), (0.5, bezel[1]), (1, bezel[2])])
        + rad(c, 'LedDomeBrush', '0.5,0.42', 0.55, 0.55, [(0, K_(0)), (0.55, K_(.05)), (1, K_(.6))])
        + rad(c, 'LedSpecularBrush', '0.34,0.26', 0.32, 0.26, [(0, W_(.92)), (0.5, W_(.38)), (1, W_(0))])
        + lin(c, 'LedLensBrush', [(0, W_(.24)), (0.46, W_(.06)), (0.5, W_(0)), (0.82, K_(.08)), (1, K_(.26))])
        + lin(c, 'LedSegmentGapBrush', hard([(0, 0.6667, clear), (0.6667, 1, gap)]), x6, x6end, x_extra))

    # Deflection. A raised cap casts a contact shadow on the panel 2 below it, its lower bevel turns from the light,
    # and pressed home it sits in the shade of its own well's rim. Day Shift's light is softer on silver.
    add("Deflection: a cap's contact shadow on the panel below it, its lower bevel, the shade of its well's rim when\nit is pressed home, and the grip knurled across the lever's thumb.",
        lin(c, 'CapContactShadowBrush', [(0, K_(.55 if night else .2)), (1, K_(.3 if night else .1))])
        + lin(c, 'CapUnderBrush', [(0, K_(0)), (0.55, K_(0)), (1, K_(.38 if night else .14))])
        + lin(c, 'CapPressedShadeBrush', [(0, K_(.6 if night else .22)), (0.4, K_(.14 if night else .06)), (1, K_(0))])
        + lin(c, 'SwitchThumbGripBrush', hard([(0, 0.3333, W_(.14)), (0.3333, 0.6667, K_(.5)), (0.6667, 1, clear)]),
              *absolute_x(3)))
    return out

HARDWARE_HC = [
    'BrushedGrainBrush', 'BrushedGrainFineBrush', 'BrushedSheenBrush',
    'StainlessHeadBrush', 'StainlessChamferBrush', 'StainlessSocketBrush', 'StainlessSocketWallBrush',
    'StainlessSpecularBrush', 'BoltCounterboreBrush',
    'GlassGlareBrush', 'GlassEdgeBrush', 'GlassVignetteBrush', 'LcdPixelGridBrush',
    'LedBezelBrush', 'LedDomeBrush', 'LedSpecularBrush', 'LedLensBrush', 'LedSegmentGapBrush',
    'CapContactShadowBrush', 'CapUnderBrush', 'CapPressedShadeBrush', 'SwitchThumbGripBrush',
]

COMPOSED_HC = {  # key -> system colour (or Transparent) for the High Contrast theme
    'FaceplateFaceBrush': W, 'FaceplateKeyLightBrush': TR, 'FaceplateCornerLightBrush': TR, 'FaceplateEdgeLightBrush': TR,
    'PlateFaceBrush': W, 'PlateArmedFaceBrush': W,
    'CapFaceBrush': BF, 'CapKeyLightBrush': TR, 'ChipFaceBrush': BF,
    'StripeFaceBrush': W, 'StripeGrainBrush': TR,
    'ArmedCapFaceBrush': HL, 'ChromeCapFaceBrush': BF,
    'DisplayFaceBrush': W, 'WellFaceBrush': W, 'AnnunciatorFaceBrush': W,
    'WellShadeTopBrush': TR, 'WellShadeLeftBrush': TR, 'WellShadeRightBrush': TR, 'DisplayShadeTopBrush': TR,
    'LcdGlassBrush': TR, 'ScanBandBrush': TR,
    'LampFaceBrush': W, 'LampGoFaceBrush': W, 'LampHoldFaceBrush': W, 'LampWarnFaceBrush': W,
    'LampNoGoFaceBrush': W, 'LampExecFaceBrush': W, 'LampArmedFaceBrush': W,
    # A switch has to read on and off in High Contrast, so its track takes Window and Highlight and its thumb
    # the text colour that sits on each. The inset shade is decoration and goes.
    'SwitchTrackBrush': W, 'SwitchTrackArmedBrush': HL, 'SwitchShadeTopBrush': TR,
    'SwitchThumbBrush': WT, 'SwitchThumbArmedBrush': HLT,
    # A field is the window colour with its edge in the text colour, as WinUI's own High Contrast text box is.
    'FieldFaceBrush': W, 'FieldShadeTopBrush': TR,
    # The hardware realism is decoration through and through - texture, screws, glass, lenses, deflection - so it
    # goes, as the key lights and bolts always have.
    **{key: TR for key in HARDWARE_HC},
}

# Solid pairs that are not palette tokens but recipe colours that change with the shift.
EXTRA = [("ChromeCapForeground", hexc('0a0a0a'), hexc('f5f5f5'), BT)]

I = '            '
def note(text):
    import textwrap
    lines = textwrap.wrap(text.replace('\n', ' '), width=118 - len(I) - 5 - 4)
    return f'{I}<!-- ' + ('\n' + I + '     ').join(lines) + ' -->'

# The locked treatment, per shift. CSS dims a disabled control to .42 and desaturates it, and writes that rule
# once for every button and then again for the switch, under a comment saying a locked control reads the same way
# everywhere rather than inventing a second idiom (:2016-2023, :2367-2369).
#
# High Contrast does not dim. The user chose those colours for their contrast and 42% of them is not the contrast
# they chose, so that shift carries the greying in the system's own GrayText and leaves the opacity at 1. That is
# why this is a resource per shift rather than a number in a template.
LOCKED = [
    ('ConsoleDisabledOpacity', '0.42', '0.42', '1'),
]

def locked_block(theme):
    out = [note("A locked control: dimmed to .42 in both working shifts, and in High Contrast not dimmed at all -\nthat shift greys the word with the user's own GrayText instead (:2016-2023, :2367-2369).")]
    for name, night, day, hc in LOCKED:
        value = hc if theme == 'hc' else (night if theme == 'night' else day)
        out.append(f'{I}<x:Double x:Key="{name}">{value}</x:Double>')

    # The word on a locked control. Unchanged in the working shifts, where the dimming above is the whole
    # treatment; GrayText in High Contrast, where it is the whole treatment instead.
    if theme == 'hc':
        out.append(f'{I}<SolidColorBrush x:Key="ConsoleDisabledForegroundBrush" Color="{{ThemeResource {GT}}}"/>')
    else:
        out.append(f'{I}<SolidColorBrush x:Key="ConsoleDisabledForegroundBrush" Color="{lit(colours(theme)["SilverBright"])}"/>')

    # The same word inside a form field, which is a dark well in both shifts. The chassis' locked word is dark on
    # Day Shift and would vanish into the well, so the field keeps its own white there - dimming is still the whole
    # treatment in the working shifts - and greys with GrayText in High Contrast like every other locked word.
    if theme == 'hc':
        out.append(f'{I}<SolidColorBrush x:Key="ConsoleDisabledFieldForegroundBrush" Color="{{ThemeResource {GT}}}"/>')
    else:
        out.append(f'{I}<SolidColorBrush x:Key="ConsoleDisabledFieldForegroundBrush" Color="{lit(colours(theme)["FieldForeground"])}"/>')

    # The armed faces, half-saturated, for a locked control. CSS desaturates a locked control with saturate(.5) as
    # well as dimming it (:2016-2023, :2367-2369); WinUI has no filter, so the faces that are not grey to begin
    # with - armed red, on a cap and on the lever - get a twin worked out the way the filter would. High Contrast
    # keeps the user's colours, as it does for every locked control.
    if theme == 'hc':
        for key, sysc in LOCKED_ARMED_HC.items():
            out.append(f'{I}<SolidColorBrush x:Key="{key}" Color="{{ThemeResource {sysc}}}"/>')
    else:
        c = colours(theme)
        half = lambda col: saturate(c[col] if isinstance(col, str) else col)
        lines = (lin(c, 'ArmedCapFaceLockedBrush', [(0, half(hexc('c8333a'))), (0.55, half('Armed')), (1, half('ArmedDeep'))])
                 + [f'<SolidColorBrush x:Key="ArmedEdgeLockedBrush" Color="{lit(half("ArmedEdge"))}"/>',
                    f'<SolidColorBrush x:Key="ArmedDeepLockedBrush" Color="{lit(half("ArmedDeep"))}"/>']
                 + lin(c, 'SwitchTrackArmedLockedBrush', [(0, half(hexc('3b0d0f'))), (1, half(hexc('240809')))])
                 + lin(c, 'SwitchThumbArmedLockedBrush', [(0, half(hexc('c8333a'))), (1, half('ArmedDeep'))]))
        out.extend(I + line for line in lines)
    return out

# The locked armed faces in High Contrast: the same system colours the unlocked ones take.
LOCKED_ARMED_HC = {
    'ArmedCapFaceLockedBrush': HL, 'ArmedEdgeLockedBrush': HL, 'ArmedDeepLockedBrush': HL,
    'SwitchTrackArmedLockedBrush': HL, 'SwitchThumbArmedLockedBrush': HLT,
}

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
    out.append('')
    out.extend(locked_block(theme))
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
