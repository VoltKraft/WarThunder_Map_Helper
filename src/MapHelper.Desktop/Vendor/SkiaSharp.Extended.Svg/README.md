# SVG icon renderer provenance

`SvgIcon.cs` and `SvgColors.cs` are a deliberately bounded adaptation of
SkiaSharp.Extended.Svg's managed SVG reader and color table.

Upstream:

- Repository: [mono/SkiaSharp.Extended](https://github.com/mono/SkiaSharp.Extended)
- Version tag: `v1.60.0`
- Exact commit: `f9c6376f4b0af24c0aebd6d51d9ff5bbfc8b118f`
- [Original SVG reader](https://github.com/mono/SkiaSharp.Extended/blob/f9c6376f4b0af24c0aebd6d51d9ff5bbfc8b118f/SkiaSharp.Extended.Svg/source/SkiaSharp.Extended.Svg.Shared/SKSvg.cs)
- [Original color helper](https://github.com/mono/SkiaSharp.Extended/blob/f9c6376f4b0af24c0aebd6d51d9ff5bbfc8b118f/SkiaSharp.Extended.Svg/source/SkiaSharp.Extended.Svg.Shared/ColorHelper.cs)
- [Upstream MIT license](https://github.com/mono/SkiaSharp.Extended/blob/f9c6376f4b0af24c0aebd6d51d9ff5bbfc8b118f/LICENSE)

The upstream copyright is **Copyright (c) 2017 Xamarin, Inc.** The complete
license is retained in
[Assets/licenses/SkiaSharp.Extended.Svg-MIT.txt](../../Assets/licenses/SkiaSharp.Extended.Svg-MIT.txt).
The desktop project's `Assets/**` content rule copies it into application
builds and published packages. This adaptation remains MIT-licensed; the
surrounding application is AGPL-3.0-only.

## Local changes

- Retain primitive/path construction, the transform approach, and all 148
  upstream named colors; adapt them to SkiaSharp 3 APIs and disposable
  per-render resources.
- Replace permissive parsing with an explicit element, attribute, and inline
  paint-style allowlist. Unsupported forms fail with PNG conversion guidance.
- Reject resource references, CSS rules/classes, active content, nested
  viewports, and processing instructions; prohibit DTD/entity resolution.
  No renderer path opens a file, URL, font, or image resource.
- Support a nonzero viewBox origin, all standard alignment combinations with
  `meet`/`slice`, and `preserveAspectRatio="none"`; apply viewport
  clipping. Use 96 pixels per inch for physical lengths.
- Implement inherited solid paints, SVG RGBA hexadecimal order, group
  compositing, fill rules, line caps/joins, dashes, and bounded transforms.
- Bound input to 4 MB, 2,048 elements, nesting depth 32, and 32,768 path
  points. Individual path data is limited to 131,072 characters; each
  transform list is limited to 64 operations. Reject non-finite and
  oversized geometry. These limits protect icon rendering, not arbitrary
  document processing.
- Remove upstream image, text, definition/reference, clip-path, and gradient
  code. Its incomplete advanced-SVG behavior is not silently retained.

The supported subset and migration guidance are documented in the
[user guide](../../../../docs/USAGE.md#svg-compatibility-in-020).
Native rasterization continues to use the existing SkiaSharp package and its
Windows/Linux x64/ARM64 assets. No new native toolchain or SVG NuGet dependency
is required.

`tests/MapHelper.UiTests/IconTests.cs` verifies pixels for bundled icons,
viewBox alignment, transformation order, opacity and fill rules; it also
covers rejected SVG/resource constructs, bounded malformed input, PNG
preservation, portal stream imports, and fallback diagnostics.
