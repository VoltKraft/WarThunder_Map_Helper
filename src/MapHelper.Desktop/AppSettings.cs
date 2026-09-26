using System.Text.Json;
using System.Text.Json.Serialization;
using MapHelper.Core;

namespace MapHelper.Desktop;

public static class AppPaths
{
    public static string Root => Program.Option("--data-dir") ?? Path.Combine(
        OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        : Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "WarThunderMapHelper");
    public static string Icons => Path.Combine(Root, "icons");
    public static void Ensure()
    {
        Directory.CreateDirectory(Root); Directory.CreateDirectory(Icons);
    }
    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Root);
            var path = Path.Combine(Root, "app.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Move(path, path + ".old", true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class AppSettings
{
    public string ApiAddress { get; set; } = "http://127.0.0.1:8111/";
    public Dictionary<string, string> IconOverrides { get; set; } = [];
    public Dictionary<string, Affiliation> ColorOverrides { get; set; } = [];
    public TargetDisplayOptions TargetDisplay { get; set; } = new();
    public CameraOptions Camera { get; set; } = new();
    public MapDisplayOptions Display { get; set; } = new();
    public static JsonSerializerOptions JsonOptions { get; } = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public static AppSettings Load()
    {
        AppPaths.Ensure();
        var path = Path.Combine(AppPaths.Root, "settings.json");
        if (!File.Exists(path)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new();
            settings.IconOverrides ??= []; settings.ColorOverrides ??= [];
            settings.TargetDisplay ??= new();
            settings.Camera ??= new();
            settings.Display ??= new();
            if (!TryAddress(settings.ApiAddress, out _)) settings.ApiAddress = "http://127.0.0.1:8111/";
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException) { AppPaths.Log("Settings: " + ex.Message); return new(); }
    }
    public void Save()
    {
        var path = Path.Combine(AppPaths.Root, "settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, JsonOptions));
        File.Move(path + ".tmp", path, true);
        File.WriteAllText(Path.Combine(AppPaths.Icons, "mapping.json"), JsonSerializer.Serialize(IconOverrides, JsonOptions));
    }
    public static bool TryAddress(string address, out Uri? uri) => Uri.TryCreate(address, UriKind.Absolute, out uri)
        && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
}
