using System.Reflection;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace MapHelper.Desktop;

internal static class AppIdentity
{
    public const string Name = "War Thunder Map Helper";
    public static string Version { get; } = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion.Split('+')[0] ?? typeof(App).Assembly.GetName().Version?.ToString(3) ?? "Development";

    public static Bitmap LoadLogo()
    {
        using var stream = AssetLoader.Open(new Uri("avares://WarThunderMapHelper/Assets/branding/app-logo-128.png"));
        return new Bitmap(stream);
    }
}
