# Application logo

Original artwork for War Thunder Map Helper: an aircraft, course line, and radar/range ring in navy, gold, and mint.

- `logo.png`: unmodified original generated with the integrated Imagegen tool.
- `app-logo-128.png`: PNG used in the interface and window/taskbar icon.
- `app-logo-512.png`: PNG used by the Flatpak desktop launcher and app metadata.
- `app.ico`: Windows icon with sizes from 16 to 256 pixels for the executable, installer, and shortcuts.

Exports are generated from the original by `scripts/export-branding.py` using Pillow. Only format and size are converted. Normal builds use the checked-in assets and require neither image generation nor Python.

## Original generation prompt

Use case: logo-brand. Asset type: production desktop application icon and logo for War Thunder Map Helper, an independent tactical flight-map companion. Create ONE square 1024x1024 finished icon, no presentation sheet, no mockup, no text. A bold geometrically simplified aircraft silhouette pointing diagonally up-right, with one clean circular radar/range ring and a straight course line through the nose, forming a distinctive compact navigation emblem. Dark navy rounded-square tile (#101B29), warm pale gold aircraft (#F8D781), mint-teal radar ring (#78D8C7), restrained white highlight if needed. Flat crisp shapes, excellent negative space, strong contrast, professional clean utility-app identity. The aircraft and circle must remain recognizable at 32 pixels; avoid fine grids, decorative tick marks, multiple tiny contacts, gradients, 3D bevels, shadows, reflections, typography, slogans and existing War Thunder or Gaijin logos. Center the emblem, make it large within the tile with modest padding. Transparent canvas outside the rounded square; genuine alpha channel, no checkerboard. Render just the final app icon.
