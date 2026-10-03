using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MapHelper.Core;
using MapHelper.Telemetry;

namespace MapHelper.Desktop;

public sealed class MainWindow : Window
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly IconRepository _icons;
    private readonly Bitmap _logo = AppIdentity.LoadLogo();
    private readonly MapControl _map;
    private MapEngine _engine = new();
    private readonly DispatcherTimer _renderTimer;
    private readonly TextBlock _status = Text("Waiting for War Thunder", 12, "#8C9DB1");
    private readonly TextBlock _modeLabel = Text("LOCAL CONNECTION", 10, "#78D8C7");
    private readonly TextBlock _contacts = Text("0 contacts", 12, "#8C9DB1");
    private readonly ToggleButton _follow = new() { Content = "Follow player" };
    private readonly ToggleButton _autoFrame = new() { Content = "All units", IsChecked = true };
    private readonly Button _rangeView = new() { Content = "Range circle", IsEnabled = false };
    private readonly ComboBox _mode = new() { ItemsSource = new[] { "Live", "Demo" }, SelectedIndex = 0, Width = 105 };
    private readonly SemaphoreSlim _restartGate = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _readerTask;
    private Bitmap? _mapImage;
    private int _imageVersion = -1;
    private long _sourceVersion;
    private bool _pendingRangeView;
    private bool _opened;
    private bool _closing;
    private bool _closeReady;
    private double _lastUi;
    private readonly Dictionary<string, string> _observedIcons = [];
    private readonly ContactInfoCard _infoCard = new();
    private readonly Canvas _infoOverlay = new();
    private readonly DispatcherTimer _hideInfo = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private long? _hoverTrack;
    private Point _hoverAnchor;
    private bool _overInfoCard;
    private MapScene? _lastScene;

    public MainWindow()
    {
        Title = $"{AppIdentity.Name} {AppIdentity.Version}"; Icon = new WindowIcon(_logo);
        Width = 1320; Height = 860; MinWidth = 1000; MinHeight = 650;
        Background = new SolidColorBrush(Color.Parse("#0B1320"));
        FontFamily = new FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter");
        _icons = new(_settings); _map = new(_icons) { ClipToBounds = true, TargetDisplay = _settings.TargetDisplay, Camera = _settings.Camera, Display = _settings.Display, AutoFrame = true };
        Content = BuildUi();
        _hideInfo.Tick += (_, _) => { _hideInfo.Stop(); if (!_overInfoCard) { _infoCard.IsVisible = false; _hoverTrack = null; } };
        _infoCard.PointerEntered += (_, _) => { _overInfoCard = true; _hideInfo.Stop(); };
        _infoCard.PointerExited += (_, _) => { _overInfoCard = false; _hideInfo.Start(); };
        _map.HoverContactChanged += (id, point) =>
        {
            if (id == null) { if (!_overInfoCard && !_hideInfo.IsEnabled) _hideInfo.Start(); return; }
            _hideInfo.Stop();
            if (_hoverTrack != id) _infoCard.NewContact();
            _hoverTrack = id; _hoverAnchor = point;
            UpdateHoverCard();
        };
        _map.NewSession += () =>
        {
            _autoFrame.IsChecked = true; _follow.IsChecked = false;
            _hideInfo.Stop(); _hoverTrack = null; _infoCard.IsVisible = false; _overInfoCard = false;
        };
        _map.ManualNavigation += () => { _follow.IsChecked = false; _autoFrame.IsChecked = false; };
        _follow.IsCheckedChanged += (_, _) =>
        {
            _map.Follow = _follow.IsChecked == true;
            if (_map.Follow) _autoFrame.IsChecked = false;
        };
        _autoFrame.IsCheckedChanged += (_, _) =>
        {
            _map.AutoFrame = _autoFrame.IsChecked == true;
            if (_map.AutoFrame) _follow.IsChecked = false;
        };
        ToolTip.SetTip(_autoFrame, "Default mode: zoom and view follow contacts according to the camera filters.");
        _rangeView.Click += (_, _) => ShowRange();
        _mode.SelectionChanged += async (_, _) => { if (_opened) await RestartAsync(); };
        _renderTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Refresh());
        Opened += async (_, _) =>
        {
            if (Program.Arguments.Contains("--demo")) _mode.SelectedIndex = 1;
            _pendingRangeView = Program.Arguments.Contains("--range-view");
            _opened = true; _renderTimer.Start(); await RestartAsync();
            if (Program.Option("--screenshot") is { } screenshot)
            {
                var seconds = double.TryParse(Program.Option("--capture-delay"), System.Globalization.CultureInfo.InvariantCulture, out var delay) ? delay : 7;
                await Task.Delay(TimeSpan.FromSeconds(seconds));
                Window captureWindow = this;
                try
                {
                    if (Program.Arguments.Contains("--capture-measurement") && _lastScene?.Map is { } previewMap)
                        _map.PreviewMeasurement(previewMap.ToWorld(new(.25, .65)), previewMap.ToWorld(new(.68, .28)));
                    if (Program.Option("--capture-contact") is { } contactId && _lastScene?.Contacts.FirstOrDefault(c => c.Observation.SourceId == contactId) is { } contact)
                    {
                        _hoverTrack = contact.TrackId; _hoverAnchor = new(40, 40); _overInfoCard = true;
                        UpdateHoverCard(); await Task.Delay(200);
                    }
                    if (Program.Arguments.Contains("--capture-target-settings"))
                    {
                        captureWindow = new TargetSettingsWindow(_settings);
                        captureWindow.Show(this);
                        await Task.Delay(300);
                    }
                    if (Program.Arguments.Contains("--capture-symbols"))
                    {
                        var symbolsWindow = new SymbolsWindow(_settings, _icons, _observedIcons);
                        captureWindow = symbolsWindow; captureWindow.Show(this);
                        await Task.Delay(300);
                    }
                    Control captureControl = captureWindow;
                    if (Program.Arguments.Contains("--capture-symbol-dropdown") && captureWindow is SymbolsWindow symbols)
                    {
                        symbols.OpenFirstChoice(); await Task.Delay(300);
                        captureControl = symbols.GetVisualDescendants().OfType<ComboBox>().First(c => c.IsDropDownOpen)
                            .GetVisualDescendants().OfType<Popup>().Single().Child!;
                    }
                    if (Program.Arguments.Contains("--capture-map-options"))
                    {
                        var button = this.GetVisualDescendants().OfType<Button>().First(b => b.Name == "MapOptions");
                        button.Flyout!.ShowAt(button); await Task.Delay(300);
                        captureControl = (Control)((Flyout)button.Flyout).Content!;
                    }
                    var absolute = Path.GetFullPath(screenshot); Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
                    using var bitmap = new RenderTargetBitmap(new PixelSize((int)captureControl.Bounds.Width, (int)captureControl.Bounds.Height), new Vector(96, 96));
                    bitmap.Render(captureControl); bitmap.Save(absolute, PngBitmapEncoderOptions.Default);
                }
                catch (Exception ex) { AppPaths.Log(ex.ToString()); Environment.ExitCode = 2; }
                finally { if (captureWindow != this) captureWindow.Close(); }
                if (Program.Arguments.Contains("--smoke-test")) Close();
            }
            else if (Program.Arguments.Contains("--smoke-test")) { await Task.Delay(3000); Close(); }
        };
        Closing += async (_, e) =>
        {
            if (_closeReady) return;
            e.Cancel = true;
            if (_closing) return;
            _closing = true; _opened = false; _sourceVersion++; _renderTimer.Stop(); _hideInfo.Stop();
            await _restartGate.WaitAsync();
            try { await StopAsync(); _mapImage?.Dispose(); _icons.Dispose(); _logo.Dispose(); _closeReady = true; }
            finally { _restartGate.Release(); }
            Close();
        };
    }
    private static TextBlock Text(string text, double size, string color = "#E5EDF5") => new()
    { Text = text, FontSize = size, Foreground = new SolidColorBrush(Color.Parse(color)), TextWrapping = TextWrapping.Wrap };
    private Control BuildUi()
    {
        var root = new Grid { Margin = new Thickness(16, 12, 16, 12), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 12 };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 20 };
        var title = Text("WAR THUNDER  /  MAP HELPER", 15, "#78D8C7");
        var version = Text("Version " + AppIdentity.Version, 10, "#8C9DB1");
        version.Name = "ApplicationVersion";
        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { new Image { Source = _logo, Width = 36, Height = 36 }, new StackPanel { Spacing = 2, Children = { title, version } } }
        };
        header.Children.Add(brand);
        var settings = new Button { Content = "Connection" };
        settings.Click += async (_, _) => { if (await new SettingsWindow(_settings).ShowDialog<bool>(this)) await RestartAsync(); };
        var symbols = new Button { Content = "Icons" };
        symbols.Click += async (_, _) => { await new SymbolsWindow(_settings, _icons, _observedIcons).ShowDialog<bool>(this); _map.InvalidateVisual(); };
        var targets = new Button { Content = "Target labels" };
        targets.Click += async (_, _) =>
        {
            if (await new TargetSettingsWindow(_settings).ShowDialog<bool>(this))
            { _map.TargetDisplay = _settings.TargetDisplay; _map.InvalidateVisual(); }
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { _mode, targets, symbols, settings } };
        Grid.SetColumn(actions, 1); header.Children.Add(actions); root.Children.Add(header);
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        toolbar.Children.Add(new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { _modeLabel, _status } });
        var fit = new Button { Content = "Full map" }; fit.Click += (_, _) => { _follow.IsChecked = false; _autoFrame.IsChecked = false; _map.Fit(); };
        var includeAi = new CheckBox { Content = "Include AI units", IsChecked = _settings.Camera.IncludeAi };
        var includeBases = new CheckBox { Content = "Include bases / mission objects", IsChecked = _settings.Camera.IncludeBases };
        var showAllies = new CheckBox { Content = "Show allies (excluding squad)", IsChecked = _settings.Display.ShowAllies };
        var squadVectors = new CheckBox { Content = "Squad member course lines", IsChecked = _settings.Display.SquadVectors };
        var targetVector = new CheckBox { Content = "Marked / nearest target course line", IsChecked = _settings.Display.TargetVector };
        var compass = new CheckBox { Name = "ShowCompass", Content = "Show compass", IsChecked = _settings.Display.ShowCompass };
        var compassCount = new ComboBox
        {
            Name = "CompassLineCount",
            ItemsSource = Enumerable.Range(1, 18).Select(n => n * 4).ToArray(),
            SelectedItem = Compass.NormalizeLineCount(_settings.Display.CompassLineCount),
            IsEnabled = _settings.Display.ShowCompass,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Avalonia.Automation.AutomationProperties.SetName(compassCount, "Compass lines");
        var compassDegrees = new CheckBox
        {
            Name = "ShowCompassDegrees",
            Content = "Show degrees on additional lines",
            IsChecked = _settings.Display.ShowCompassDegrees,
            IsEnabled = _settings.Display.ShowCompass && _settings.Display.CompassLineCount > 4
        };
        ToolTip.SetTip(targetVector, "Direction of the uniquely API-marked enemy, otherwise the enemy nearest your course line. An API marker does not always identify your selected target.");
        var filterMessage = Text("These filters affect only automatic framing. Contacts with unknown AI status remain included.", 12, "#8C9DB1");
        var filterError = Text("", 12, "#FF7E82");
        void SaveCamera()
        {
            _settings.Camera = new(includeAi.IsChecked == true, includeBases.IsChecked == true);
            _map.Camera = _settings.Camera;
            var lineCount = compassCount.SelectedItem is int count ? count : 4;
            compassCount.IsEnabled = compass.IsChecked == true;
            compassDegrees.IsEnabled = compass.IsChecked == true && lineCount > 4;
            _settings.Display = new(showAllies.IsChecked == true, squadVectors.IsChecked == true, targetVector.IsChecked == true,
                compass.IsChecked == true, lineCount, compassDegrees.IsChecked == true);
            _map.Display = _settings.Display;
            _map.InvalidateVisual();
            try { _settings.Save(); filterError.Text = ""; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { filterError.Text = "Saved for this session only: " + ex.Message; }
        }
        includeAi.IsCheckedChanged += (_, _) => SaveCamera(); includeBases.IsCheckedChanged += (_, _) => SaveCamera();
        showAllies.IsCheckedChanged += (_, _) => SaveCamera(); squadVectors.IsCheckedChanged += (_, _) => SaveCamera(); targetVector.IsCheckedChanged += (_, _) => SaveCamera();
        compass.IsCheckedChanged += (_, _) => SaveCamera(); compassCount.SelectionChanged += (_, _) => SaveCamera();
        compassDegrees.IsCheckedChanged += (_, _) => SaveCamera();
        var filters = new Button
        {
            Name = "MapOptions",
            Content = "Map options",
            Flyout = new Flyout
            {
                Content = new ScrollViewer
                {
                    MaxHeight = 560,
                    Content = new StackPanel
                    {
                        Width = 340,
                        Spacing = 12,
                        Children =
            {
                Text("Display", 15), showAllies, squadVectors, targetVector,
                Text("Compass", 15), compass, Text("Compass lines (4–72, in steps of 4)", 12), compassCount, compassDegrees,
                Text("Automatic framing", 15), includeAi, includeBases, filterMessage,
                Text("With both filters off, the last observed enemy position remains in view.", 12, "#8C9DB1"), filterError
            }
                    }
                }
            }
        };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _autoFrame, filters, _follow, fit, _rangeView } };
        Grid.SetColumn(tools, 1); toolbar.Children.Add(tools); Grid.SetRow(toolbar, 1); root.Children.Add(toolbar);
        _infoOverlay.Children.Add(_infoCard);
        var mapLayers = new Grid { Children = { _map, _infoOverlay } };
        var body = new Border { Child = mapLayers, CornerRadius = new CornerRadius(12), ClipToBounds = true, BorderBrush = new SolidColorBrush(Color.Parse("#2A384A")), BorderThickness = new Thickness(1) };
        Grid.SetRow(body, 2); root.Children.Add(body);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        footer.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 18,
            Children =
        { Text("▲  Own player", 11, "#F8D781"), Text("▲  Squad", 11, "#74E79C"), Text("▲  Ally", 11, "#68C4F5"), Text("▲  Enemy", 11, "#FF7E82"), Text("AI  Computer", 11, "#B8C4D1"), Text("··  Estimated", 11, "#8997AC"), Text("×  Destroyed", 11, "#C8D3E0") }
        });
        Grid.SetColumn(_contacts, 1); footer.Children.Add(_contacts); Grid.SetRow(footer, 3); root.Children.Add(footer);
        return root;
    }
    private void ShowRange()
    {
        if (!_map.FitRange()) return;
        _follow.IsChecked = false; _autoFrame.IsChecked = false;
    }
    private async Task StopAsync()
    {
        if (_cts != null) await _cts.CancelAsync();
        if (_readerTask != null) try { await _readerTask; } catch (OperationCanceledException) { }
        _cts?.Dispose(); _cts = null; _readerTask = null;
    }
    private async Task RestartAsync()
    {
        await _restartGate.WaitAsync();
        try
        {
            if (_closing) return;
            _sourceVersion++;
            await StopAsync();
            _engine = new(); _mapImage?.Dispose(); _mapImage = null; _imageVersion = -1; _observedIcons.Clear();
            var version = _sourceVersion;
            _cts = new(); var ct = _cts.Token;
            var demo = _mode.SelectedIndex == 1;
            _modeLabel.Text = demo ? "DEMO · SAMPLE DATA" : "LOCAL CONNECTION";
            ToolTip.SetTip(_modeLabel, demo ? "Synthetic sample data" : _settings.ApiAddress);
            var parser = new TelemetryParser(); foreach (var pair in _settings.ColorOverrides) parser.ColorOverrides[pair.Key] = pair.Value;
            var address = new Uri(_settings.ApiAddress);
            ITelemetrySource source = demo ? new DemoTelemetrySource(() => _clock.Elapsed.TotalSeconds) : new HttpTelemetrySource(address, () => _clock.Elapsed.TotalSeconds, parser);
            _readerTask = Task.Run(async () =>
            {
                await using var ownedSource = source;
                var iconImportAttempted = false;
                Task? iconImport = null;
                try
                {
                    await foreach (var packet in source.ReadAsync(ct))
                    {
                        Dispatcher.UIThread.Post(() => { if (version == _sourceVersion) _engine.Accept(packet); });
                        if (!demo && packet.Map != null && !iconImportAttempted)
                        {
                            iconImportAttempted = true;
                            iconImport = Task.Run(async () =>
                            {
                                try
                                {
                                    var count = await OriginalIconImporter.ImportAsync(address, ct);
                                    if (count > 0) Dispatcher.UIThread.Post(() => { if (version == _sourceVersion) _icons.Reload(); });
                                }
                                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or OperationCanceledException or System.Xml.XmlException) { AppPaths.Log("Original icons: " + ex.Message); }
                            }, ct);
                        }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    AppPaths.Log(ex.ToString());
                    Dispatcher.UIThread.Post(() => { if (version == _sourceVersion) ToolTip.SetTip(_status, "Data source: " + ex.Message); });
                }
                finally { if (iconImport != null) try { await iconImport; } catch (OperationCanceledException) { } }
            });
        }
        finally { _restartGate.Release(); }
    }
    private void Refresh()
    {
        var now = _clock.Elapsed.TotalSeconds;
        var scene = MapDisplay.ApplyVisibility(_engine.Scene(now), _settings.Display);
        _lastScene = scene;
        if (_imageVersion != _engine.ImageVersion)
        {
            _imageVersion = _engine.ImageVersion; _mapImage?.Dispose(); _mapImage = null;
            if (_engine.MapImage is { } bytes)
                try { using var stream = new MemoryStream(bytes); _mapImage = new Bitmap(stream); }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { AppPaths.Log("Map image: " + ex.Message); }
        }
        _map.SetScene(scene, _mapImage);
        if (_pendingRangeView && scene.Range is { Meters: > 1 }) { ShowRange(); _pendingRangeView = false; }
        if (now - _lastUi < .25) return; _lastUi = now;
        UpdateHoverCard();
        _status.Text = _mode.SelectedIndex == 1 && scene.Live ? "Demo active" : scene.Status;
        _rangeView.IsEnabled = scene.Live && scene.Range is { Meters: > 1 };
        ToolTip.SetTip(_rangeView, scene.Range is { } range
            ? FormattableString.Invariant($"Show the full circle: approximately {range.Meters / 1000:0.0} km. One-way distance at unchanged consumption and speed.")
            : "The range circle becomes available when movement and fuel consumption can be measured.");
        _contacts.Text = $"{scene.Contacts.Count(c => c.Observation.IsMobile)} units · North up";
        foreach (var contact in scene.Contacts) _observedIcons[contact.Observation.Icon] = contact.Observation.IconClass;
    }
    private void UpdateHoverCard()
    {
        if (_lastScene?.Contacts.FirstOrDefault(c => c.TrackId == _hoverTrack) is not { } contact)
        { _infoCard.IsVisible = false; return; }
        _infoCard.Update(ContactInformation.Build(_lastScene, contact));
        _infoCard.Width = Math.Min(420, Math.Max(250, _map.Bounds.Width - 32));
        _infoCard.MaxHeight = Math.Max(100, Math.Min(560, _map.Bounds.Height - 32));
        var x = _hoverAnchor.X + 24;
        if (x + _infoCard.Width > _map.Bounds.Width - 8) x = _hoverAnchor.X - _infoCard.Width - 24;
        Canvas.SetLeft(_infoCard, Math.Clamp(x, 8, Math.Max(8, _map.Bounds.Width - _infoCard.Width - 8)));
        Canvas.SetTop(_infoCard, Math.Clamp(_hoverAnchor.Y - 20, 8, Math.Max(8, _map.Bounds.Height - _infoCard.MaxHeight - 8)));
        _infoCard.IsVisible = true;
    }
}
