using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace MapHelper.Desktop;

public sealed class IconRepository : IDisposable
{
    private readonly AppSettings _settings;
    private readonly Dictionary<string, Bitmap> _cache = [];
    private readonly HashSet<string> _failed = [];
    private Dictionary<string, string> _originals = [];
    public static readonly string[] Classes = ["Player", "Aircraft", "Ground", "Ship", "Objective"];
    public IconRepository(AppSettings settings)
    {
        _settings = settings;
        foreach (var name in Classes)
        {
            var source = Path.Combine(AppContext.BaseDirectory, "Assets", "icons", name + ".svg");
            var target = Path.Combine(AppPaths.Icons, "default_" + name + ".svg");
            if (!File.Exists(target) && File.Exists(source)) File.Copy(source, target);
        }
        var readme = Path.Combine(AppPaths.Icons, "README.txt");
        if (!File.Exists(readme)) File.WriteAllText(readme, "Place SVG/PNG icons here. Point the nose upward; the center represents the position.\nSVG: currentColor is replaced with the unit color.\nmapping.json maps Player, Aircraft, Ground, Ship, Objective, or exact API icon names to a file.\nUse filenames without directories. Apply changes with Icons > Reload.\noriginal-* files are created after a successful import from the local War Thunder web server.\n");
        Reload();
    }
    public void Reload()
    {
        foreach (var image in _cache.Values) image.Dispose(); _cache.Clear(); _failed.Clear();
        var mapping = Path.Combine(AppPaths.Icons, "mapping.json");
        if (File.Exists(mapping))
            try { _settings.IconOverrides = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(mapping)) ?? []; }
            catch (JsonException ex) { AppPaths.Log(ex.Message); }
        var originals = Path.Combine(AppPaths.Icons, "original-mapping.json");
        if (File.Exists(originals))
            try { _originals = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(originals)) ?? []; }
            catch (JsonException ex) { AppPaths.Log(ex.Message); }
    }
    public string[] Files() => Directory.EnumerateFiles(AppPaths.Icons).Where(p => Path.GetExtension(p).ToLowerInvariant() is ".svg" or ".png")
        .Select(Path.GetFileName).OfType<string>().Order().ToArray();
    public Bitmap? Get(string icon, string iconClass, string color)
    {
        var file = _settings.IconOverrides.GetValueOrDefault(icon) ?? _settings.IconOverrides.GetValueOrDefault(iconClass)
            ?? _originals.GetValueOrDefault(icon) ?? "default_" + iconClass + ".svg";
        return Load(file, color) ?? Load("default_" + iconClass + ".svg", color);
    }
    public Bitmap? Load(string file, string color)
    {
        if (Path.GetFileName(file) != file || !File.Exists(Path.Combine(AppPaths.Icons, file))) return null;
        var key = file + ":" + color;
        if (_cache.TryGetValue(key, out var cached)) return cached;
        if (_failed.Contains(file)) return null;
        try
        {
            var path = Path.Combine(AppPaths.Icons, file);
            if (new FileInfo(path).Length > 4_000_000) return null;
            Bitmap bitmap;
            if (Path.GetExtension(file).Equals(".png", StringComparison.OrdinalIgnoreCase)) bitmap = new Bitmap(path);
            else
            {
                var xml = File.ReadAllText(path);
                using var picture = SvgIcon.Load(xml, color);
                var rect = picture.CullRect;
                if (rect.Width <= 0 || rect.Height <= 0) return null;
                using var surface = SKSurface.Create(new SKImageInfo(96, 96));
                surface.Canvas.Clear(SKColors.Transparent);
                var scale = 88 / Math.Max(rect.Width, rect.Height);
                surface.Canvas.Translate(48 - rect.MidX * scale, 48 - rect.MidY * scale);
                surface.Canvas.Scale(scale);
                surface.Canvas.DrawPicture(picture);
                using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var png = data.AsStream(); bitmap = new Bitmap(png);
            }
            _cache[key] = bitmap;
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or XmlException or ArgumentException or InvalidOperationException or NotSupportedException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        { _failed.Add(file); AppPaths.Log("Icon " + file + ": " + ex.Message); return null; }
    }
    public Bitmap? GetAutomatic(string icon, string iconClass, string color) => Load(_originals.GetValueOrDefault(icon) ?? "default_" + iconClass + ".svg", color)
        ?? Load("default_" + iconClass + ".svg", color);
    public static void ValidateSvg(string xml)
    {
        using var picture = SvgIcon.Load(xml, "#FFFFFF");
    }
    public string Import(string source)
    {
        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension is not (".svg" or ".png")) throw new InvalidDataException("Select an SVG or PNG file.");
        if (new FileInfo(source).Length > 4_000_000) throw new InvalidDataException("Icon must not exceed 4 MB.");
        if (extension == ".svg") ValidateSvg(File.ReadAllText(source));
        var name = Path.GetFileName(source);
        if (Path.GetFullPath(source) == Path.GetFullPath(Path.Combine(AppPaths.Icons, name))) return name;
        var i = 1;
        while (File.Exists(Path.Combine(AppPaths.Icons, name))) name = Path.GetFileNameWithoutExtension(source) + "-" + (i++).ToString(CultureInfo.InvariantCulture) + extension;
        File.Copy(source, Path.Combine(AppPaths.Icons, name));
        return name;
    }
    /// <summary>
    /// Copies a selected SVG or PNG into the profile without requiring direct access to its path.
    /// The caller retains ownership of <paramref name="source"/>. Reads at most 4 MB and validates
    /// SVG content before creating a file. Invalid content, cancellation, and I/O errors are reported
    /// to the caller; an incomplete output is removed. Existing files are never overwritten.
    /// </summary>
    public async Task<string> ImportAsync(Stream source, string fileName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("Use an icon filename without a directory.");
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not (".svg" or ".png")) throw new InvalidDataException("Select an SVG or PNG file.");
        using var data = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (data.Length + read > 4_000_000) throw new InvalidDataException("Icon must not exceed 4 MB.");
            data.Write(buffer, 0, read);
        }
        if (extension == ".svg")
        {
            data.Position = 0;
            using var reader = new StreamReader(data, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            ValidateSvg(reader.ReadToEnd());
        }
        cancellationToken.ThrowIfCancellationRequested();
        var name = fileName;
        var suffix = 1;
        while (File.Exists(Path.Combine(AppPaths.Icons, name)))
            name = Path.GetFileNameWithoutExtension(fileName) + "-" + (suffix++).ToString(CultureInfo.InvariantCulture) + extension;
        var target = Path.Combine(AppPaths.Icons, name);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            data.Position = 0;
            await data.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await output.DisposeAsync().ConfigureAwait(false);
            File.Delete(target);
            throw;
        }
        return name;
    }
    public void Dispose() { foreach (var b in _cache.Values) b.Dispose(); _cache.Clear(); _failed.Clear(); }
}
