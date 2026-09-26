using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MapHelper.Core;

namespace MapHelper.Desktop;

internal sealed class SettingsWindow : Window
{
    public SettingsWindow(AppSettings settings)
    {
        Title = "Connection · War Thunder Map Helper"; Width = 570; Height = 510; CanResize = false;
        Background = new SolidColorBrush(Color.Parse("#101B29"));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var address = new TextBox { Text = settings.ApiAddress };
        var allies = new TextBox { Text = string.Join(", ", settings.ColorOverrides.Where(p => p.Value == Affiliation.Ally).Select(p => p.Key)), PlaceholderText = "e.g. #185AFF" };
        var enemies = new TextBox { Text = string.Join(", ", settings.ColorOverrides.Where(p => p.Value == Affiliation.Enemy).Select(p => p.Key)), PlaceholderText = "e.g. #FA3200" };
        var squad = new TextBox { Text = string.Join(", ", settings.ColorOverrides.Where(p => p.Value == Affiliation.Squad).Select(p => p.Key)), PlaceholderText = "e.g. #00FF00" };
        var error = new TextBlock { Foreground = Brushes.Salmon, TextWrapping = TextWrapping.Wrap };
        var save = new Button { Content = "Save and connect", HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += (_, _) =>
        {
            if (!AppSettings.TryAddress(address.Text?.Trim() ?? "", out var uri)) { error.Text = "Enter a valid HTTP or HTTPS address without credentials, query parameters, or a fragment."; return; }
            var colors = new Dictionary<string, Affiliation>(StringComparer.OrdinalIgnoreCase);
            foreach (var (text, team) in new[] { (allies.Text, Affiliation.Ally), (enemies.Text, Affiliation.Enemy), (squad.Text, Affiliation.Squad) })
                foreach (var color in (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (color.Length != 7 || color[0] != '#' || !Color.TryParse(color, out _) || colors.ContainsKey(color)) { error.Text = "Enter unique colors in #RRGGBB format."; return; }
                    colors[color] = team;
                }
            settings.ApiAddress = uri!.AbsoluteUri.TrimEnd('/') + "/"; settings.ColorOverrides = colors;
            try { settings.Save(); Close(true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error.Text = ex.Message; }
        };
        Content = new StackPanel
        {
            Margin = new Thickness(28),
            Spacing = 12,
            Children =
        {
            new TextBlock { Text = "Connect to the game", FontSize = 23, FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = "The local browser map must be reachable during a mission.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSlateGray }, address,
            new TextBlock { Text = "Additional ally colors (comma-separated)" }, allies,
            new TextBlock { Text = "Additional enemy colors (comma-separated)" }, enemies,
            new TextBlock { Text = "Additional squad colors (comma-separated)" }, squad, error, save
        }
        };
    }
}

internal sealed class SymbolsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly IconRepository _icons;
    private readonly StackPanel _rows = new() { Spacing = 14 };
    private readonly Dictionary<string, ComboBox> _choices = [];
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSlateGray };
    private readonly HashSet<string> _keys;
    private readonly IReadOnlyDictionary<string, string> _classes;
    public SymbolsWindow(AppSettings settings, IconRepository icons, IReadOnlyDictionary<string, string> observedIcons)
    {
        _settings = settings; _icons = icons;
        _classes = observedIcons;
        _keys = new(IconRepository.Classes.Concat(settings.IconOverrides.Keys).Concat(observedIcons.Keys.Where(n => n != "none" && n != "unknown")), StringComparer.Ordinal);
        Title = "Icons · War Thunder Map Helper"; Width = 640; Height = 650;
        Background = new SolidColorBrush(Color.Parse("#101B29"));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var import = new Button { Content = "Add SVG / PNG" };
        import.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import unit icon",
                AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("Icons") { Patterns = ["*.svg", "*.png"] }]
            });
            try
            {
                foreach (var file in files)
                {
                    await using var stream = await file.OpenReadAsync();
                    await _icons.ImportAsync(stream, file.Name);
                }
                BuildRows(); _message.Text = "Files added. Select the desired mapping and save.";
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Xml.XmlException or ArgumentException or NotSupportedException or System.Text.RegularExpressions.RegexMatchTimeoutException) { _message.Text = ex.Message; }
        };
        var folder = new Button { Content = "Open icon folder" };
        folder.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(AppPaths.Icons) { UseShellExecute = true }); } catch (Exception ex) { _message.Text = ex.Message; } };
        var reload = new Button { Content = "Reload" };
        reload.Click += (_, _) => { _icons.Reload(); BuildRows(); _message.Text = "Files and mapping.json reloaded."; };
        var save = new Button { Content = "Save mappings", HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += (_, _) =>
        {
            foreach (var (key, choice) in _choices)
                if (choice.SelectedItem is string file && file != "Automatic") _settings.IconOverrides[key] = file;
                else _settings.IconOverrides.Remove(key);
            try { _settings.Save(); _icons.Reload(); Close(true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _message.Text = ex.Message; }
        };
        var top = new StackPanel
        {
            Spacing = 12,
            Children =
        {
            new TextBlock { Text = "Your map. Your icons.", FontSize = 23, FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = "Automatic prefers available original icons. Custom icons should point upward; the map rotates them to the direction of movement.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSlateGray },
            new WrapPanel { Orientation = Orientation.Horizontal, Children = { import, folder, reload } }
        }
        };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(24), RowSpacing = 18 };
        grid.Children.Add(top);
        var scroll = new ScrollViewer { Content = _rows }; Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
        Grid.SetRow(_message, 2); grid.Children.Add(_message); Grid.SetRow(save, 3); grid.Children.Add(save);
        Content = grid; BuildRows();
    }
    private void BuildRows()
    {
        var old = _choices.ToDictionary(p => p.Key, p => p.Value.SelectedItem as string);
        _choices.Clear(); _rows.Children.Clear();
        foreach (var key in _keys.OrderBy(k => Array.IndexOf(IconRepository.Classes, k) is var i && i >= 0 ? i.ToString() : "9" + k))
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("160,*"), ColumnSpacing = 12 };
            var display = key switch { "Player" => "Own player", "Aircraft" => "Aircraft", "Ground" => "Ground units", "Ship" => "Ships", "Objective" => "Objectives / objects", _ => key };
            var label = new TextBlock { Text = display, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            var choices = new[] { "Automatic" }.Concat(_icons.Files()).ToArray();
            var selected = old.GetValueOrDefault(key) ?? _settings.IconOverrides.GetValueOrDefault(key) ?? "Automatic";
            var box = new ComboBox { ItemsSource = choices, SelectedItem = choices.Contains(selected) ? selected : "Automatic", HorizontalAlignment = HorizontalAlignment.Stretch };
            box.ItemTemplate = new FuncDataTemplate<string>((file, _) => new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children =
                {
                    new Image { Width = 30, Height = 30, Source = file == "Automatic"
                        ? _icons.GetAutomatic(key, IconRepository.Classes.Contains(key) ? key : _classes.GetValueOrDefault(key, "Objective"), "#6BD4CB")
                        : _icons.Load(file, "#6BD4CB") },
                    new TextBlock { Text = file, VerticalAlignment = VerticalAlignment.Center }
                }
            });
            _choices[key] = box;
            row.Children.Add(label); Grid.SetColumn(box, 1); row.Children.Add(box); _rows.Children.Add(row);
        }
    }
    internal void OpenFirstChoice() { if (_choices.Values.FirstOrDefault() is { } choice) choice.IsDropDownOpen = true; }
}
