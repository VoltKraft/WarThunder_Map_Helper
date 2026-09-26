// SPDX-License-Identifier: MIT
// Adapted from SkiaSharp.Extended.Svg, Copyright (c) 2017 Xamarin, Inc.
// See README.md in this directory for the exact upstream source and local changes.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using SkiaSharp;

namespace MapHelper.Desktop;

/// <summary>
/// Renders a bounded, static SVG icon subset. Never resolves resources or executes content.
/// Unsupported constructs throw <see cref="InvalidDataException"/> with PNG migration guidance.
/// Each invocation owns its parser and Skia objects; the caller must dispose the returned picture.
/// </summary>
internal static class SvgIcon
{
    private const string SvgNamespace = "http://www.w3.org/2000/svg";
    private const int MaxCharacters = 4_000_000;
    private const int MaxElements = 2048;
    private const int MaxDepth = 32;
    private const int MaxPoints = 32768;
    private const string NumberPattern = @"[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?";
    private static readonly Regex Numbers = new(NumberPattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex TransformCalls = new(@"([A-Za-z]+)\s*\(([^()]*)\)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly HashSet<string> PaintAttributes = new(StringComparer.Ordinal)
    {
        "fill", "stroke", "color", "fill-rule", "opacity", "fill-opacity", "stroke-opacity",
        "stroke-width", "stroke-linecap", "stroke-linejoin", "stroke-miterlimit", "stroke-dasharray",
        "stroke-dashoffset", "display", "visibility"
    };
    private static readonly Dictionary<string, string[]> GeometryAttributes = new(StringComparer.Ordinal)
    {
        ["svg"] = ["width", "height", "viewBox", "preserveAspectRatio", "version"],
        ["g"] = [],
        ["path"] = ["d"],
        ["rect"] = ["x", "y", "width", "height", "rx", "ry"],
        ["circle"] = ["cx", "cy", "r"],
        ["ellipse"] = ["cx", "cy", "rx", "ry"],
        ["line"] = ["x1", "y1", "x2", "y2"],
        ["polygon"] = ["points"],
        ["polyline"] = ["points"],
        ["title"] = [],
        ["desc"] = []
    };

    internal static SKPicture Load(string xml, string currentColor)
    {
        if (xml.Length > MaxCharacters || Encoding.UTF8.GetByteCount(xml) > MaxCharacters)
            throw Invalid("SVG exceeds the 4 MB limit");
        xml = xml.TrimStart('\uFEFF');
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxCharacters
        });
        var document = XDocument.Load(reader);
        var root = document.Root ?? throw Invalid("SVG has no root element");
        if (root.Name.LocalName != "svg") throw Invalid("The root element must be svg");
        if (document.DescendantNodes().OfType<XProcessingInstruction>().Any()) throw Invalid("SVG processing instructions are not supported");
        var count = 0;
        ValidateElement(root, 0, ref count);
        var viewBox = ReadViewBox(root.Attribute("viewBox")?.Value);
        var width = ViewportLength(root.Attribute("width")?.Value, viewBox?.Width);
        var height = ViewportLength(root.Attribute("height")?.Value, viewBox?.Height);
        if (width <= 0 || height <= 0 || !float.IsFinite(width) || !float.IsFinite(height) || width > 1_000_000 || height > 1_000_000)
            throw Invalid("SVG requires bounded, positive width and height or a viewBox");
        var viewport = SKRect.Create(width, height);
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(viewport);
        // SVG 2 places the root transform outside the viewport and its viewBox transform.
        canvas.Concat(ReadTransform(root.Attribute("transform")?.Value));
        CheckMatrix(canvas.TotalMatrix);
        canvas.ClipRect(viewport);
        if (viewBox is { } box) ApplyViewBox(canvas, box, viewport, root.Attribute("preserveAspectRatio")?.Value);
        else if (root.Attribute("preserveAspectRatio") is { } ratio) ValidateAspectRatio(ratio.Value);
        var style = new Dictionary<string, string>(StringComparer.Ordinal) { ["color"] = currentColor, ["fill"] = "black", ["stroke"] = "none" };
        var points = 0;
        DrawElement(root, canvas, style, true, ref points);
        return recorder.EndRecording();
    }

    private static InvalidDataException Invalid(string message) => new(message + ". Export the icon as a PNG or simplify it to the supported SVG icon subset.");

    private static void ValidateElement(XElement element, int depth, ref int count)
    {
        if (++count > MaxElements || depth > MaxDepth) throw Invalid("SVG is too complex");
        var name = element.Name.LocalName;
        if (element.Name.NamespaceName is not ("" or SvgNamespace) || !GeometryAttributes.TryGetValue(name, out var geometry))
            throw Invalid("Unsupported SVG element: " + name);
        if (depth > 0 && name == "svg") throw Invalid("Nested SVG viewports are not supported");
        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration) continue;
            var attributeName = attribute.Name.LocalName;
            if (attribute.Name.NamespaceName.Length != 0 || !(attributeName is "id" or "style" or "transform"
                || PaintAttributes.Contains(attributeName) || geometry.Contains(attributeName)))
                throw Invalid("Unsupported SVG attribute: " + attributeName);
        }
        _ = LocalStyle(element);
        foreach (var child in element.Elements())
        {
            if (name is not ("svg" or "g")) throw Invalid("Only svg and g elements may contain graphic children");
            ValidateElement(child, depth + 1, ref count);
        }
        if (name is not ("title" or "desc") && element.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
            throw Invalid("SVG text is not supported");
    }

    private static Dictionary<string, string> LocalStyle(XElement element)
    {
        var result = element.Attributes().Where(a => PaintAttributes.Contains(a.Name.LocalName))
            .ToDictionary(a => a.Name.LocalName, a => a.Value.Trim(), StringComparer.Ordinal);
        if (element.Attribute("style") is { } attribute)
            foreach (var declaration in attribute.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var pair = declaration.Split(':', 2, StringSplitOptions.TrimEntries);
                if (pair.Length != 2 || !PaintAttributes.Contains(pair[0]) || pair[1].Length == 0)
                    throw Invalid("Unsupported SVG style declaration");
                result[pair[0]] = pair[1];
            }
        return result;
    }

    private static void DrawElement(XElement element, SKCanvas canvas, Dictionary<string, string> inherited, bool visible, ref int points)
    {
        if (element.Name.LocalName is "title" or "desc") return;
        var local = LocalStyle(element);
        var style = new Dictionary<string, string>(inherited, StringComparer.Ordinal);
        foreach (var pair in local)
            if (pair.Key is not ("opacity" or "display"))
            {
                if (pair.Value == "inherit") continue;
                // currentColor on the color property inherits the parent's color.
                if (pair.Key == "color" && pair.Value.Equals("currentColor", StringComparison.OrdinalIgnoreCase)) continue;
                style[pair.Key] = pair.Value;
            }
        var display = local.GetValueOrDefault("display", "inline");
        if (display is not ("inline" or "none")) throw Invalid("Unsupported display value");
        visible &= display != "none";
        var visibility = style.GetValueOrDefault("visibility", "visible");
        if (visibility is not ("visible" or "hidden" or "collapse")) throw Invalid("Unsupported visibility value");
        var opacity = Opacity(local.GetValueOrDefault("opacity", "1"));
        using var fill = Paint(style, false);
        using var stroke = Paint(style, true);
        canvas.Save();
        if (element.Name.LocalName != "svg") canvas.Concat(ReadTransform(element.Attribute("transform")?.Value));
        CheckMatrix(canvas.TotalMatrix);
        using var opacityPaint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(opacity * 255)) };
        if (opacity < 1) canvas.SaveLayer(opacityPaint);
        if (element.Name.LocalName is "svg" or "g")
        {
            foreach (var child in element.Elements()) DrawElement(child, canvas, style, visible, ref points);
        }
        else
        {
            using var path = ReadPath(element);
            points += path.PointCount;
            if (points > MaxPoints || !FiniteRect(path.Bounds) || !FiniteRect(canvas.TotalMatrix.MapRect(path.Bounds)))
                throw Invalid("SVG geometry exceeds the supported bounds");
            path.FillType = style.GetValueOrDefault("fill-rule", "nonzero") == "evenodd" ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
            if (visible && visibility == "visible")
            {
                if (fill != null && element.Name.LocalName != "line") canvas.DrawPath(path, fill);
                if (stroke != null) canvas.DrawPath(path, stroke);
            }
        }
        if (opacity < 1) canvas.Restore();
        canvas.Restore();
    }

    private static SKPaint? Paint(Dictionary<string, string> style, bool stroke)
    {
        var value = style.GetValueOrDefault(stroke ? "stroke" : "fill", stroke ? "none" : "black");
        var color = SvgColors.Parse(style.GetValueOrDefault("color", "black"));
        if (value != "none" && !value.Equals("currentColor", StringComparison.OrdinalIgnoreCase)) color = SvgColors.Parse(value);
        if (style.GetValueOrDefault("fill-rule", "nonzero") is not ("evenodd" or "nonzero")) throw Invalid("Unsupported fill rule");
        var opacity = Opacity(style.GetValueOrDefault(stroke ? "stroke-opacity" : "fill-opacity", "1"));
        var width = NonNegative(style.GetValueOrDefault("stroke-width", "1"));
        var cap = style.GetValueOrDefault("stroke-linecap", "butt") switch
        { "butt" => SKStrokeCap.Butt, "round" => SKStrokeCap.Round, "square" => SKStrokeCap.Square, _ => throw Invalid("Unsupported stroke line cap") };
        var join = style.GetValueOrDefault("stroke-linejoin", "miter") switch
        { "miter" => SKStrokeJoin.Miter, "round" => SKStrokeJoin.Round, "bevel" => SKStrokeJoin.Bevel, _ => throw Invalid("Unsupported stroke line join") };
        var miter = Length(style.GetValueOrDefault("stroke-miterlimit", "4"));
        if (miter < 1) throw Invalid("Stroke miter limit must be at least 1");
        var dashText = style.GetValueOrDefault("stroke-dasharray", "none");
        var dashes = dashText == "none" ? [] : NumberList(dashText);
        if (dashes.Any(d => d < 0) || (dashes.Length > 0 && dashes.All(d => d == 0))) throw Invalid("Invalid stroke dash array");
        var dashOffset = Length(style.GetValueOrDefault("stroke-dashoffset", "0"));
        if (value == "none" || stroke && width == 0) return null;
        var paint = new SKPaint
        {
            IsAntialias = true,
            Style = stroke ? SKPaintStyle.Stroke : SKPaintStyle.Fill,
            Color = color.WithAlpha((byte)Math.Round(color.Alpha * opacity)),
            StrokeWidth = width,
            StrokeCap = cap,
            StrokeJoin = join,
            StrokeMiter = miter
        };
        if (stroke && dashes.Length > 0)
        {
            if (dashes.Length % 2 != 0) dashes = [.. dashes, .. dashes];
            using var dash = SKPathEffect.CreateDash(dashes, dashOffset);
            paint.PathEffect = dash;
        }
        return paint;
    }

    private static SKPath ReadPath(XElement element)
    {
        float Read(string name) => Length(element.Attribute(name)?.Value ?? "0");
        float Positive(string name) => NonNegative(element.Attribute(name)?.Value ?? "0");
        var path = new SKPath();
        try
        {
            switch (element.Name.LocalName)
            {
                case "rect":
                    var x = Read("x"); var y = Read("y");
                    var width = Positive("width"); var height = Positive("height");
                    var rx = NonNegative(element.Attribute("rx")?.Value ?? element.Attribute("ry")?.Value ?? "0");
                    var ry = NonNegative(element.Attribute("ry")?.Value ?? element.Attribute("rx")?.Value ?? "0");
                    // Empty SVG shapes must not become stroked degenerate paths in Skia.
                    if (width == 0 || height == 0) break;
                    path.AddRoundRect(SKRect.Create(x, y, width, height), Math.Min(rx, width / 2), Math.Min(ry, height / 2));
                    break;
                case "ellipse":
                    var cx = Read("cx"); var cy = Read("cy"); var radiusX = Positive("rx"); var radiusY = Positive("ry");
                    if (radiusX == 0 || radiusY == 0) break;
                    path.AddOval(new(cx - radiusX, cy - radiusY, cx + radiusX, cy + radiusY));
                    break;
                case "circle":
                    var centerX = Read("cx"); var centerY = Read("cy"); var radius = Positive("r");
                    if (radius > 0) path.AddCircle(centerX, centerY, radius);
                    break;
                case "line": path.MoveTo(Read("x1"), Read("y1")); path.LineTo(Read("x2"), Read("y2")); break;
                case "path":
                    var data = element.Attribute("d")?.Value ?? "";
                    if (data.Length > 131072) throw Invalid("SVG path is too complex");
                    foreach (Match match in Numbers.Matches(data)) _ = Scalar(match.Value);
                    if (data.Length == 0) break;
                    using (var parsed = SKPath.ParseSvgPathData(data) ?? throw Invalid("Invalid SVG path")) path.AddPath(parsed);
                    break;
                case "polygon":
                case "polyline":
                    var coordinates = NumberList(element.Attribute("points")?.Value ?? "");
                    if (coordinates.Length % 2 != 0) throw Invalid("Point coordinates require x/y pairs");
                    for (var i = 0; i < coordinates.Length; i += 2)
                        if (i == 0) path.MoveTo(coordinates[i], coordinates[i + 1]); else path.LineTo(coordinates[i], coordinates[i + 1]);
                    if (element.Name.LocalName == "polygon") path.Close();
                    break;
            }
            return path;
        }
        catch { path.Dispose(); throw; }
    }

    private static SKMatrix ReadTransform(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return SKMatrix.Identity;
        var matrix = SKMatrix.Identity;
        var offset = 0;
        var count = 0;
        foreach (Match match in TransformCalls.Matches(raw))
        {
            if (++count > 64 || raw[offset..match.Index].Trim(' ', '\t', '\r', '\n', ',').Length != 0) throw Invalid("Invalid SVG transform");
            var values = NumberList(match.Groups[2].Value);
            var operation = match.Groups[1].Value;
            var next = operation switch
            {
                "matrix" when values.Length == 6 => new SKMatrix(values[0], values[2], values[4], values[1], values[3], values[5], 0, 0, 1),
                "translate" when values.Length is 1 or 2 => SKMatrix.CreateTranslation(values[0], values.Length == 2 ? values[1] : 0),
                "scale" when values.Length is 1 or 2 => SKMatrix.CreateScale(values[0], values.Length == 2 ? values[1] : values[0]),
                "rotate" when values.Length == 1 => SKMatrix.CreateRotationDegrees(values[0]),
                "rotate" when values.Length == 3 => SKMatrix.CreateRotationDegrees(values[0], values[1], values[2]),
                "skewX" when values.Length == 1 => SKMatrix.CreateSkew((float)Math.Tan(values[0] * Math.PI / 180), 0),
                "skewY" when values.Length == 1 => SKMatrix.CreateSkew(0, (float)Math.Tan(values[0] * Math.PI / 180)),
                _ => throw Invalid("Unsupported SVG transform or argument count: " + operation)
            };
            matrix = SKMatrix.Concat(matrix, next);
            CheckMatrix(matrix);
            offset = match.Index + match.Length;
        }
        if (count == 0 || !string.IsNullOrWhiteSpace(raw[offset..])) throw Invalid("Invalid SVG transform");
        return matrix;
    }

    private static SKRect? ReadViewBox(string? raw)
    {
        if (raw == null) return null;
        var values = NumberList(raw);
        if (values.Length != 4 || values[2] <= 0 || values[3] <= 0) throw Invalid("Invalid SVG viewBox");
        return SKRect.Create(values[0], values[1], values[2], values[3]);
    }

    private static string[] ValidateAspectRatio(string raw)
    {
        var values = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (values.Length == 1 && values[0] == "none") return values;
        if (values.Length is < 1 or > 2 || values.Length == 2 && values[1] is not ("meet" or "slice")
            || !Regex.IsMatch(values[0], "^x(Min|Mid|Max)Y(Min|Mid|Max)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw Invalid("Unsupported preserveAspectRatio value");
        return values;
    }

    private static void ApplyViewBox(SKCanvas canvas, SKRect box, SKRect viewport, string? raw)
    {
        var values = ValidateAspectRatio(raw ?? "xMidYMid meet");
        var scaleX = viewport.Width / box.Width; var scaleY = viewport.Height / box.Height;
        var translateX = 0f; var translateY = 0f;
        if (values[0] != "none")
        {
            scaleX = scaleY = values.Length == 2 && values[1] == "slice" ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
            translateX = (viewport.Width - box.Width * scaleX) * (values[0][1..4] switch { "Min" => 0, "Max" => 1, _ => .5f });
            translateY = (viewport.Height - box.Height * scaleY) * (values[0][5..8] switch { "Min" => 0, "Max" => 1, _ => .5f });
        }
        canvas.Translate(translateX, translateY);
        canvas.Scale(scaleX, scaleY);
        canvas.Translate(-box.Left, -box.Top);
        CheckMatrix(canvas.TotalMatrix);
    }

    private static float ViewportLength(string? raw, float? reference)
    {
        if (raw == null) return reference ?? 0;
        raw = raw.Trim();
        return raw.EndsWith('%') ? Scalar(raw[..^1]) / 100 * (reference ?? throw Invalid("Percentage viewport dimensions require a viewBox")) : Length(raw);
    }

    private static float Length(string raw)
    {
        raw = raw.Trim();
        var multiplier = 1f;
        if (raw.EndsWith("px", StringComparison.Ordinal)) raw = raw[..^2];
        else
            foreach (var (unit, factor) in new[] { ("pt", 96f / 72), ("pc", 16f), ("in", 96f), ("cm", 96f / 2.54f), ("mm", 96f / 25.4f) })
                if (raw.EndsWith(unit, StringComparison.Ordinal)) { raw = raw[..^2]; multiplier = factor; break; }
        var value = Scalar(raw) * multiplier;
        if (!float.IsFinite(value) || Math.Abs(value) > 1_000_000) throw Invalid("SVG length exceeds the supported bounds");
        return value;
    }

    private static float Scalar(string raw)
    {
        if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value) || Math.Abs(value) > 1_000_000)
            throw Invalid("Invalid or unbounded SVG number");
        return value;
    }

    private static float NonNegative(string raw) { var value = Length(raw); return value >= 0 ? value : throw Invalid("SVG dimensions cannot be negative"); }
    private static float Opacity(string raw) => Math.Clamp(Scalar(raw), 0, 1);
    private static bool FiniteRect(SKRect rect) => new[] { rect.Left, rect.Top, rect.Right, rect.Bottom }.All(v => float.IsFinite(v) && Math.Abs(v) <= 1_000_000_000);
    private static void CheckMatrix(SKMatrix matrix)
    {
        if (matrix.Values.Any(v => !float.IsFinite(v) || Math.Abs(v) > 1_000_000_000)) throw Invalid("SVG transform exceeds the supported bounds");
    }

    private static float[] NumberList(string raw)
    {
        var values = new List<float>();
        var offset = 0;
        foreach (Match match in Numbers.Matches(raw))
        {
            if (values.Count >= MaxPoints * 2 || raw[offset..match.Index].Trim(' ', '\t', '\r', '\n', ',').Length != 0)
                throw Invalid("Invalid SVG number list");
            if (offset == match.Index && offset > 0 && raw[offset] is not ('+' or '-')) throw Invalid("SVG numbers require separators");
            values.Add(Scalar(match.Value));
            offset = match.Index + match.Length;
        }
        if (raw[offset..].Trim(' ', '\t', '\r', '\n', ',').Length != 0) throw Invalid("Invalid SVG number list");
        return values.ToArray();
    }
}
