using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace MapHelper.Desktop;

// Read only resources exposed by the game's own web page; never execute its JavaScript or unpack game archives.
internal static class OriginalIconImporter
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    { "Player", "Fighter", "Bomber", "Assault", "Airdefence", "MediumTank", "HeavyTank", "LightTank", "TankDestroyer", "Ship", "Destroyer", "Cruiser", "Structure", "waypoint", "capture_zone", "bombing_point", "defending_point", "respawn_base_fighter", "respawn_base_bomber", "respawn_base_tank" };
    public static async Task<int> ImportAsync(Uri address, CancellationToken ct)
    {
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
        var documents = new List<(Uri Uri, string Text)>();
        async Task<byte[]> Fetch(Uri uri)
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            using var data = new MemoryStream(); var buffer = new byte[8192]; int n;
            while ((n = await input.ReadAsync(buffer, ct)) > 0)
            {
                if (data.Length + n > 4_000_000) throw new InvalidDataException("Icon resource is too large.");
                data.Write(buffer, 0, n);
            }
            return data.ToArray();
        }
        bool Local(Uri uri) => uri.Scheme == address.Scheme && uri.Host == address.Host && uri.Port == address.Port;
        var root = Encoding.UTF8.GetString(await Fetch(address)); documents.Add((address, root));
        var links = Regex.Matches(root, "(?:src|href)\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase)
            .Select(m => new Uri(address, WebUtility.HtmlDecode(m.Groups[1].Value)))
            .Where(u => Local(u) && Path.GetExtension(u.AbsolutePath) is ".js" or ".css").Distinct().Take(12);
        foreach (var uri in links)
            try { documents.Add((uri, Encoding.UTF8.GetString(await Fetch(uri)))); }
            catch (HttpRequestException) { }
        var mapping = new Dictionary<string, string>();
        var glyphs = new Dictionary<string, string>();
        var fonts = new HashSet<Uri>();
        foreach (var (uri, text) in documents)
        {
            foreach (Match m in Regex.Matches(text, "[\"']?([A-Za-z_][A-Za-z_0-9-]*)[\"']?\\s*:\\s*[\"']\\\\u([0-9a-fA-F]{4})[\"']"))
                if (Names.Contains(m.Groups[1].Value)) glyphs[m.Groups[1].Value] = char.ConvertFromUtf32(Convert.ToInt32(m.Groups[2].Value, 16));
            foreach (Match m in Regex.Matches(text, "[\"'(]([^\"')\\s]+\\.(?:svg|png|ttf|otf|woff2?))(?:[?#][^\"')]*)?[\"')]", RegexOptions.IgnoreCase))
            {
                if (!Uri.TryCreate(uri, m.Groups[1].Value, out var asset) || !Local(asset)) continue;
                var extension = Path.GetExtension(asset.AbsolutePath).ToLowerInvariant();
                if (extension is ".ttf" or ".otf" or ".woff" or ".woff2") { fonts.Add(asset); continue; }
                var name = Path.GetFileNameWithoutExtension(asset.AbsolutePath);
                if (!Names.Contains(name)) continue;
                try
                {
                    var bytes = await Fetch(asset);
                    if (extension == ".svg") IconRepository.ValidateSvg(Encoding.UTF8.GetString(bytes));
                    var file = "original-" + name + extension;
                    await File.WriteAllBytesAsync(Path.Combine(AppPaths.Icons, file), bytes, ct); mapping[name] = file;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or System.Xml.XmlException or System.Text.RegularExpressions.RegexMatchTimeoutException) { AppPaths.Log(ex.Message); }
            }
        }
        foreach (var font in fonts.Take(4))
        {
            try
            {
                using var bytes = SKData.CreateCopy(await Fetch(font));
                using var face = SKTypeface.FromData(bytes);
                if (face == null) continue;
                using var skFont = new SKFont(face, 64);
                foreach (var (name, glyph) in glyphs)
                {
                    if (!face.ContainsGlyphs(glyph)) continue;
                    using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
                    skFont.MeasureText(glyph, out var bounds);
                    using var surface = SKSurface.Create(new SKImageInfo(96, 96));
                    surface.Canvas.Clear(SKColors.Transparent);
                    surface.Canvas.DrawText(glyph, 48 - bounds.MidX, 48 - bounds.MidY, SKTextAlign.Left, skFont, paint);
                    using var image = surface.Snapshot(); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                    var file = "original-" + name + ".png";
                    await File.WriteAllBytesAsync(Path.Combine(AppPaths.Icons, file), png.ToArray(), ct); mapping[name] = file;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or ArgumentException) { AppPaths.Log(ex.Message); }
        }
        if (mapping.Count > 0) await File.WriteAllTextAsync(Path.Combine(AppPaths.Icons, "original-mapping.json"), JsonSerializer.Serialize(mapping, AppSettings.JsonOptions), ct);
        return mapping.Count;
    }
}
