using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MapHelper.Core;
using MapHelper.Desktop;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(MapHelper.UiTests.TestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace MapHelper.UiTests;

public sealed class TestApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        MapHelper.Desktop.Program.Arguments = ["--data-dir=" + Path.Combine(Path.GetTempPath(), "MapHelper.UiTests", Guid.NewGuid().ToString("N"))];
        AppPaths.Ensure();
        return AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}

public sealed class MapInteractionTests
{
    private static MapScene Scene(long session = 1)
    {
        var player = new Contact(1, new("own", "aircraft", "Player", Affiliation.Self, "#ffff00", new(.5, .5)),
            new(5000, 5000), new Vec2(100, 0), new Vec2(1, 0), 1, 0, ContactPhase.Live, null, false);
        return new(new("test", 1, default, new(10000, 10000), default, default), [player], null, null, "Live", true, 1, [], session);
    }
    private static Window Open(Control control)
    {
        var window = new Window { Width = 1000, Height = 700, Content = control };
        window.Show(); Dispatcher.UIThread.RunJobs(); return window;
    }

    [AvaloniaFact]
    public void RightDragMeasuresLivePersistsThroughZoomAndRightClickClears()
    {
        using var icons = new IconRepository(new());
        var map = new MapControl(icons);
        var window = Open(map);
        try
        {
            map.SetScene(Scene(), null); map.AutoFrame = false; map.Fit();
            Dispatcher.UIThread.RunJobs();
            window.MouseDown(new(250, 250), MouseButton.Right);
            window.MouseMove(new(450, 250), RawInputModifiers.RightMouseButton);
            var live = Assert.IsType<MeasuredSegment>(map.Measurement);
            var scale = Math.Min((map.Bounds.Width - 52) / 10000, (map.Bounds.Height - 72) / 10000);
            Assert.Equal(200 / scale, live.Meters, 5);
            window.MouseUp(new(550, 250), MouseButton.Right);
            var finished = Assert.IsType<MeasuredSegment>(map.Measurement);
            Assert.Equal(300 / scale, finished.Meters, 5);
            window.MouseWheel(new(400, 300), new(0, 2));
            Assert.Equal(finished, map.Measurement);
            window.MouseDown(new(100, 100), MouseButton.Right); window.MouseUp(new(100, 100), MouseButton.Right);
            Assert.Null(map.Measurement);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MeasuringDoesNotDisableDefaultAutomaticCameraAndNewSessionClearsLine()
    {
        using var icons = new IconRepository(new()); var map = new MapControl(icons); var window = Open(map);
        try
        {
            map.SetScene(Scene(), null); Dispatcher.UIThread.RunJobs(); Assert.True(map.AutoFrame);
            window.MouseDown(new(250, 250), MouseButton.Right);
            window.MouseMove(new(400, 400), RawInputModifiers.RightMouseButton);
            var start = map.Measurement;
            window.MouseWheel(new(300, 300), new(0, 2), RawInputModifiers.RightMouseButton);
            Assert.Equal(start, map.Measurement); Assert.True(map.AutoFrame);
            window.MouseUp(new(400, 400), MouseButton.Right);
            Assert.NotNull(map.Measurement); Assert.True(map.AutoFrame);
            map.SetScene(Scene(2), null); Assert.Null(map.Measurement); Assert.True(map.AutoFrame);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void HoverFindsUnitEvenWhenAllTargetReadoutsAreDisabled()
    {
        using var icons = new IconRepository(new());
        var map = new MapControl(icons) { TargetDisplay = new(TargetValues.None, MarkedTarget: TargetValues.None) }; var window = Open(map);
        try
        {
            map.SetScene(Scene(), null); map.AutoFrame = false; map.Fit(); Dispatcher.UIThread.RunJobs();
            long? hovered = null; map.HoverContactChanged += (id, _) => hovered = id;
            window.MouseMove(new(map.Bounds.Width / 2, map.Bounds.Height / 2)); Assert.Equal(1L, hovered);
            window.MouseMove(new(10, 10)); Assert.Null(hovered);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void HiddenTankSpawnCannotBeHoveredButStillSetsAutomaticZoom()
    {
        using var icons = new IconRepository(new());
        var map = new MapControl(icons) { Camera = new(false, false) };
        var window = Open(map);
        try
        {
            var source = Scene();
            var own = source.Contacts[0] with
            {
                Position = new(2000, 5000),
                Observation = source.Contacts[0].Observation with { Type = "ground_model", Position = new(.2, .5) }
            };
            var spawn = new Contact(2, new("spawn", "respawn_base_tank", "respawn_base_tank", Affiliation.Ally,
                "#174DFF", new(.8, .5)), new(8000, 5000), null, null, 1, 0, ContactPhase.Live, null, false);
            var scene = source with
            {
                Contacts = [own, spawn],
                Player = new(true, "tankModels/test", null, null, null, null, WeaponTelemetry.Empty, Army: "tank")
            };
            map.SetScene(scene, null); Dispatcher.UIThread.RunJobs();
            long? hovered = null; map.HoverContactChanged += (id, _) => hovered = id;
            window.MouseMove(new(48, map.Bounds.Height / 2)); Assert.Equal(1L, hovered);
            window.MouseMove(new(map.Bounds.Width - 48, map.Bounds.Height / 2)); Assert.Null(hovered);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void OpenSymbolDropdownContainsAnImageForEveryEntry()
    {
        var settings = new AppSettings(); using var icons = new IconRepository(settings);
        var window = new SymbolsWindow(settings, icons, new Dictionary<string, string>());
        try
        {
            window.Show(); window.OpenFirstChoice(); Dispatcher.UIThread.RunJobs();
            var combo = window.GetVisualDescendants().OfType<ComboBox>().First(c => c.IsDropDownOpen);
            var popup = combo.GetVisualDescendants().OfType<Popup>().Single();
            Assert.True(popup.IsOpen);
            var images = popup.Child!.GetVisualDescendants().OfType<Image>().ToArray();
            Assert.Equal(icons.Files().Length + 1, images.Length);
            Assert.All(images, image => Assert.NotNull(image.Source));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DisplayAndCameraPreferencesPersistAndOlderSettingsUseSafeDefaults()
    {
        File.WriteAllText(Path.Combine(AppPaths.Root, "settings.json"), "{\"ApiAddress\":\"http://127.0.0.1:8111/\"}");
        var settings = AppSettings.Load(); Assert.True(settings.Display.ShowAllies);
        Assert.True(settings.Display.SquadVectors); Assert.True(settings.Display.TargetVector);
        Assert.True(settings.Camera.IncludeAi); Assert.True(settings.Camera.IncludeBases);
        settings.Display = new(false, false, false); settings.Camera = new(false, false); settings.Save();
        var loaded = AppSettings.Load(); Assert.Equal(settings.Display, loaded.Display); Assert.Equal(settings.Camera, loaded.Camera);
    }

    [AvaloniaFact]
    public async Task OldProfilesEnableMarkedTargetValuesAndTheDialogSavesThemSeparately()
    {
        File.WriteAllText(Path.Combine(AppPaths.Root, "settings.json"), "{\"TargetDisplay\":{\"CourseTarget\":\"None\",\"Air\":\"None\"}}");
        var settings = AppSettings.Load();
        Assert.Equal(TargetValues.Distance | TargetValues.Time, settings.TargetDisplay.MarkedTarget);
        Assert.Equal(TargetValues.None, settings.TargetDisplay.CourseTarget);
        var owner = Open(new Border()); var dialog = new TargetSettingsWindow(settings);
        try
        {
            var closed = dialog.ShowDialog<bool>(owner); Dispatcher.UIThread.RunJobs();
            var boxes = dialog.GetVisualDescendants().OfType<CheckBox>().ToArray();
            var distance = boxes.Single(c => AutomationProperties.GetName(c) == "Marked target: Distance");
            var time = boxes.Single(c => AutomationProperties.GetName(c) == "Marked target: Time to target");
            Assert.True(distance.IsChecked); Assert.True(time.IsChecked);
            var buttons = dialog.GetVisualDescendants().OfType<Button>().ToArray();
            buttons.Single(b => Equals(b.Content, "All off")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.All(boxes, c => Assert.False(c.IsChecked));
            time.IsChecked = true;
            buttons.Single(b => Equals(b.Content, "Save")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await closed);
            var loaded = AppSettings.Load(); Assert.Equal(TargetValues.Time, loaded.TargetDisplay.MarkedTarget);
            Assert.Equal(TargetValues.None, loaded.TargetDisplay.CourseTarget); Assert.Equal(TargetValues.None, loaded.TargetDisplay.Air);
        }
        finally { dialog.Close(); owner.Close(); }
    }
}
