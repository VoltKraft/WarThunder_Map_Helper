"""Export app-sized PNG and multi-resolution ICO from the unchanged generated logo.

Requires Pillow. Only format/size conversion; the artwork itself is not edited.
The committed exports let normal .NET builds run without Python.
"""
from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parents[1]
branding = root / "src" / "MapHelper.Desktop" / "Assets" / "branding"
with Image.open(branding / "logo.png") as original:
    assert original.width == original.height, "Logo must be square"
    logo = original.convert("RGBA")
    logo.resize((128, 128), Image.Resampling.LANCZOS).save(branding / "app-logo-128.png")
    logo.resize((512, 512), Image.Resampling.LANCZOS).save(branding / "app-logo-512.png")
    logo.save(branding / "app.ico", format="ICO", sizes=[(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
    print(f"Logo: {original.size}, alpha: {logo.getextrema()[3]}")
with Image.open(branding / "app.ico") as icon:
    sizes = sorted(icon.ico.sizes())
    assert (16, 16) in sizes and (256, 256) in sizes
    print(f"ICO sizes: {sizes}")
