using Avalonia;

namespace MapHelper.Desktop;

internal static class Program
{
    public static string[] Arguments { get; internal set; } = [];
    public static string? Option(string name) => Arguments.FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.Ordinal))?[(name.Length + 1)..];
    [STAThread]
    public static int Main(string[] args)
    {
        Arguments = args;
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); return Environment.ExitCode; }
        catch (Exception ex) { AppPaths.Log(ex.ToString()); return 1; }
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
