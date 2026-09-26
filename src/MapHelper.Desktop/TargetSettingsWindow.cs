using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MapHelper.Core;

namespace MapHelper.Desktop;

internal sealed class TargetSettingsWindow : Window
{
    public TargetSettingsWindow(AppSettings settings)
    {
        Title = "Target labels · War Thunder Map Helper";
        Width = 650; Height = 700; CanResize = false;
        Background = new SolidColorBrush(Color.Parse("#101B29"));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var options = settings.TargetDisplay;
        var rows = new[]
        {
            ("Marked target", options.MarkedTarget),
            ("Course-line target", options.CourseTarget),
            ("Other air units", options.Air),
            ("Other ground units", options.Ground),
            ("Other bases / airfields", options.Bases),
            ("Other naval units", options.Sea)
        };
        var choices = new List<(CheckBox Distance, CheckBox Time)>();
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,120,120"),
            RowDefinitions = new RowDefinitions("36,48,48,48,48,48,48"),
            ColumnSpacing = 10
        };
        var distanceHeading = new TextBlock { Text = "Distance", HorizontalAlignment = HorizontalAlignment.Center };
        var timeHeading = new TextBlock { Text = "Time to target", HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetColumn(distanceHeading, 1); grid.Children.Add(distanceHeading);
        Grid.SetColumn(timeHeading, 2); grid.Children.Add(timeHeading);
        for (var index = 0; index < rows.Length; index++)
        {
            var (label, values) = rows[index];
            var title = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            var distance = new CheckBox { IsChecked = values.HasFlag(TargetValues.Distance), HorizontalAlignment = HorizontalAlignment.Center };
            var time = new CheckBox { IsChecked = values.HasFlag(TargetValues.Time), HorizontalAlignment = HorizontalAlignment.Center };
            AutomationProperties.SetName(distance, label + ": Distance");
            AutomationProperties.SetName(time, label + ": Time to target");
            Grid.SetRow(title, index + 1); grid.Children.Add(title);
            Grid.SetRow(distance, index + 1); Grid.SetColumn(distance, 1); grid.Children.Add(distance);
            Grid.SetRow(time, index + 1); Grid.SetColumn(time, 2); grid.Children.Add(time);
            choices.Add((distance, time));
        }
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Salmon };
        var allOff = new Button { Content = "All off" };
        allOff.Click += (_, _) => { foreach (var choice in choices) { choice.Distance.IsChecked = false; choice.Time.IsChecked = false; } };
        var cancel = new Button { Content = "Cancel" }; cancel.Click += (_, _) => Close(false);
        var save = new Button { Content = "Save" };
        save.Click += (_, _) =>
        {
            TargetValues Read(int index) => (choices[index].Distance.IsChecked == true ? TargetValues.Distance : TargetValues.None)
                | (choices[index].Time.IsChecked == true ? TargetValues.Time : TargetValues.None);
            var previous = settings.TargetDisplay;
            settings.TargetDisplay = new(CourseTarget: Read(1), Air: Read(2), Ground: Read(3), Bases: Read(4), Sea: Read(5), MarkedTarget: Read(0));
            try { settings.Save(); Close(true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { settings.TargetDisplay = previous; error.Text = ex.Message; }
        };
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        buttons.Children.Add(allOff);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { cancel, save } };
        Grid.SetColumn(right, 1); buttons.Children.Add(right);
        Content = new StackPanel
        {
            Margin = new Thickness(28),
            Spacing = 16,
            Children =
        {
            new TextBlock { Text = "Distance and time to target", FontSize = 23, FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = "The API-marked target shows distance and time by default, even away from your course line. Its settings take priority over the course target and unit categories.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSlateGray },
            grid,
            new TextBlock { Text = "Time = direct map distance ÷ your ground speed, to the current target position. When stationary or without a measurement, the label shows “Time —”. Estimated contacts use ≈.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSlateGray },
            error, buttons
        }
        };
    }
}
