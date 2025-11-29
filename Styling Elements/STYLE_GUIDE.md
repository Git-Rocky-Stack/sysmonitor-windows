# System Monitor - Design Style Guide

This document provides a comprehensive styling reference for recreating the System Monitor app design on Windows or other platforms.

---

## Table of Contents
1. [Brand Identity](#brand-identity)
2. [Color Palette](#color-palette)
3. [Typography](#typography)
4. [Component Styles](#component-styles)
5. [Spacing & Layout](#spacing--layout)
6. [Animations](#animations)

---

## Brand Identity

### App Name
**System Monitor**

### Primary Brand Color
- **Brand Red**: `#AA2024` - The signature color used throughout the app
- This deep red is used for primary buttons, accents, progress bars, and branding elements

### Design Philosophy
- **Dark-first design**: The app uses a pure black AMOLED theme as the default
- **Modern/Industrial aesthetic**: Chrome bezel effects on buttons give a premium, technical feel
- **High contrast**: White text on dark backgrounds for excellent readability
- **Status-driven colors**: Color coding for system health (green = good, red = critical)

---

## Color Palette

### Primary Brand Colors
| Name | Hex Code | RGB | Usage |
|------|----------|-----|-------|
| Brand Red | `#AA2024` | rgb(170, 32, 36) | Primary actions, accents, branding |
| Brand Red Dark | `#8A1A1D` | rgb(138, 26, 29) | Pressed states, darker variants |
| Brand Red Light | `#C93437` | rgb(201, 52, 55) | Hover states, gradients |
| Brand Accent | `#FF7043` | rgb(255, 112, 67) | Secondary accents |

### Accent Colors
| Name | Hex Code | RGB | Usage |
|------|----------|-----|-------|
| Accent Blue | `#2196F3` | rgb(33, 150, 243) | Info, links, secondary actions |
| Accent Green | `#4CAF50` | rgb(76, 175, 80) | Success, positive status |
| Accent Orange | `#FF9800` | rgb(255, 152, 0) | Warnings, caution |
| Accent Purple | `#9C27B0` | rgb(156, 39, 176) | Special features |
| Accent Teal | `#00BCD4` | rgb(0, 188, 212) | Highlights |

### Background Colors (Dark Mode - Primary)
| Name | Hex Code | Usage |
|------|----------|-------|
| Background Primary | `#000000` | Main app background (pure black) |
| Background Secondary | `#0F0F0F` | Slightly elevated areas |
| Background Card | `#1A1A1A` | Card backgrounds |
| Background Card Elevated | `#252525` | Elevated cards, modals |
| Background Dark | `#0A0A0A` | Deepest backgrounds |

### Background Colors (Light Mode)
| Name | Hex Code | Usage |
|------|----------|-------|
| Background Primary | `#FFFFFF` | Main app background |
| Background Secondary | `#F5F5F5` | Slightly lowered areas |
| Background Card | `#FFFFFF` | Card backgrounds |
| Background Card Elevated | `#FAFAFA` | Elevated cards |

### Surface Colors (Dark Mode)
| Name | Hex Code | Usage |
|------|----------|-------|
| Surface | `#1A1A1A` | Default surface |
| Surface Dark | `#121212` | Darker surface |
| Surface Medium | `#1E1E1E` | Medium surface |
| Surface Light | `#2A2A2A` | Lighter surface |
| Surface Elevated | `#2C2C2C` | Elevated elements |

### Text Colors (Dark Mode)
| Name | Hex Code | Opacity | Usage |
|------|----------|---------|-------|
| Text Primary | `#FFFFFF` | 100% | Headings, important text |
| Text Secondary | `#B3B3B3` | 70% | Body text, descriptions |
| Text Tertiary | `#808080` | 50% | Hints, less important |
| Text Disabled | `#4D4D4D` | 30% | Disabled states |

### Text Colors (Light Mode)
| Name | Hex Code | Usage |
|------|----------|-------|
| Text Primary | `#212121` | Headings, important text |
| Text Secondary | `#757575` | Body text, descriptions |
| Text Tertiary | `#9E9E9E` | Hints, less important |
| Text Disabled | `#BDBDBD` | Disabled states |

### Status Colors (Universal)
| Name | Hex Code | Usage |
|------|----------|-------|
| Status Excellent | `#4CAF50` | Perfect/optimal status |
| Status Good | `#8BC34A` | Good status |
| Status Fair | `#FFC107` | Acceptable, monitor |
| Status Warning | `#FF9800` | Needs attention |
| Status Critical | `#F44336` | Critical, action needed |

### Chart Colors
| Name | Hex Code | Usage |
|------|----------|-------|
| Chart Red | `#AA2024` | CPU, main metrics |
| Chart Orange | `#FF7043` | Memory |
| Chart Amber | `#FFA726` | Storage |
| Chart Yellow | `#FFCA28` | Network |
| Chart Blue | `#2196F3` | Battery, info |
| Chart Cyan | `#00BCD4` | Secondary data |
| Chart Green | `#4CAF50` | Positive trends |
| Chart Purple | `#9C27B0` | Special data |

### Divider Colors (Dark Mode)
| Name | Hex Code | Usage |
|------|----------|-------|
| Divider | `#333333` | Standard dividers |
| Divider Subtle | `#1A1A1A` | Subtle separation |
| Divider Strong | `#4D4D4D` | Strong separation |

---

## Typography

### Font Family
- **Primary Font**: Roboto Medium (`sans-serif-medium`)
- **Fallback**: System sans-serif font
- **Windows Equivalent**: Segoe UI Semibold or Roboto Medium

### Font Sizes
| Style | Size | Line Height | Weight | Usage |
|-------|------|-------------|--------|-------|
| Display Large | 57sp | 64sp | Normal | Hero numbers |
| Display Medium | 45sp | 52sp | Normal | Large stats |
| Display Small | 36sp | 44sp | Normal | Section headers |
| Headline Large | 32sp | 40sp | Normal | Page titles |
| Headline Medium | 28sp | 36sp | Normal | Card titles |
| Headline Small | 24sp | 32sp | Normal | Subsections |
| Title Large | 22sp | 28sp | Normal | Major labels |
| Title Medium | 16sp | 24sp | Medium | Card headers |
| Title Small | 14sp | 20sp | Medium | Labels |
| Body Large | 16sp | 24sp | Normal | Primary content |
| Body Medium | 14sp | 20sp | Normal | Secondary content |
| Body Small | 12sp | 16sp | Normal | Captions |
| Label Large | 14sp | 20sp | Medium | Button text |
| Label Medium | 12sp | 16sp | Medium | Tags, badges |
| Label Small | 11sp | 16sp | Medium | Hints |

### Letter Spacing
- Body text: 0.5sp
- Labels: 0.1sp
- Titles: 0sp

---

## Component Styles

### Cards
```
Background: #1A1A1A (dark) / #FFFFFF (light)
Corner Radius: 16dp (16px)
Elevation: Subtle shadow or none
Padding: 16dp internal
```

### Primary Buttons (Chrome Bezel Style)
The app uses a distinctive "chrome bezel" button style with layered gradients:

**Outer Bezel (Chrome Effect)**
```
Layer 1 - Outer Chrome:
  Gradient: 135° linear
  Start: #E8E8E8
  Center: #A0A0A0
  End: #606060
  Corner Radius: 14dp

Layer 2 - Inner Highlight (1dp inset):
  Gradient: 315° linear
  Start: #FFFFFF
  Center: #C8C8C8
  End: #888888
  Corner Radius: 13dp

Layer 3 - Dark Edge (2dp inset):
  Gradient: 135° linear
  Start: #505050
  Center: #383838
  End: #282828
  Corner Radius: 12dp

Layer 4 - Content (3dp inset):
  Gradient: 90° linear
  Start: #AA2024 (Brand Red)
  End: #C93437 (Brand Red Light)
  Corner Radius: 10dp
```

### Secondary Buttons (Outline Style)
```
Same chrome bezel as primary, but:
Layer 4 - Content:
  Background: #1A1A1A (solid)
  Border: 2dp #AA2024
  Corner Radius: 10dp
```

### Progress Bars
```
Track Background: #2A2A2A
Progress Fill: #AA2024 (or status color)
Corner Radius: 4dp
Height: 4-8dp
```

### Badges
```
Background: #AA2024
Text Color: #FFFFFF
Corner Radius: 4dp
Padding: 4dp horizontal, 2dp vertical
Font: Label Small (11sp)
```

### Input Fields
```
Background: #1A1A1A
Border: 1dp #333333
Border Focus: 2dp #AA2024
Corner Radius: 8dp
Text Color: #FFFFFF
Placeholder: #808080
```

### Dialogs
```
Background: #1A1A1A (dark) / #FFFFFF (light)
Overlay: rgba(0, 0, 0, 0.5)
Corner Radius: 16dp
Title: Text Primary
Button Color: #AA2024
```

---

## Spacing & Layout

### Spacing Scale
| Name | Value | Usage |
|------|-------|-------|
| xs | 4dp | Minimal spacing |
| sm | 8dp | Tight spacing |
| md | 16dp | Standard spacing |
| lg | 24dp | Generous spacing |
| xl | 32dp | Section spacing |
| xxl | 48dp | Major sections |

### Common Patterns
- Card padding: 16dp
- List item padding: 16dp horizontal, 12dp vertical
- Section margins: 16dp
- Icon sizes: 24dp (standard), 48dp (large), 16dp (small)

---

## Animations

### Duration
| Type | Duration |
|------|----------|
| Fast | 150ms |
| Normal | 300ms |
| Slow | 500ms |

### Button Click
```
Scale down to 95% over 100ms
Scale back to 100% over 100ms
```

### Fade In
```
Opacity: 0 to 1
Duration: 200ms
Easing: ease-out
```

### Slide Up
```
TranslateY: 100% to 0
Duration: 300ms
Easing: ease-out
```

### Pulse (for alerts)
```
Scale: 1.0 → 1.05 → 1.0
Duration: 600ms
Repeat: infinite
```

---

## Icon Style

- **Style**: Outlined/Line icons (not filled)
- **Stroke Width**: 2dp
- **Color**: Text Primary or Brand Red for active states
- **Size**: 24dp standard

---

## Gradient Reference

### Header Gradient
```
Type: Linear
Angle: 135°
Start: #AA2024
End: #FF5252
```

### Card Gradient (Subtle)
```
Type: Linear
Angle: 180°
Start: rgba(170, 32, 36, 0.1)
End: transparent
```

---

## Quick Reference - Essential Colors

For Windows development, these are the most frequently used colors:

```css
/* Core Colors */
--brand-primary: #AA2024;
--background: #000000;
--surface: #1A1A1A;
--text-primary: #FFFFFF;
--text-secondary: #B3B3B3;

/* Status */
--success: #4CAF50;
--warning: #FF9800;
--error: #F44336;
--info: #2196F3;

/* UI Elements */
--divider: #333333;
--disabled: #4D4D4D;
--card-bg: #1A1A1A;
```

---

## Files Included in This Package

1. `STYLE_GUIDE.md` - This document
2. `colors-dark-mode.css` - CSS variables for dark mode
3. `colors-light-mode.css` - CSS variables for light mode
4. `colors.json` - Complete color palette in JSON format
5. `typography.css` - Typography styles
6. `components.css` - Component style examples

---

*Last updated: November 2024*
*App Version: 2.0.3*
