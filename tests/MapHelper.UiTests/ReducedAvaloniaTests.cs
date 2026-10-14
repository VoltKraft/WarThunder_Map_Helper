using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Xunit;

namespace MapHelper.UiTests;

public sealed class ReducedAvaloniaTests
{
    [Theory]
    [InlineData("Avalonia.Controls.AutoCompleteBox")]
    [InlineData("Avalonia.Controls.Calendar")]
    [InlineData("Avalonia.Controls.CalendarDatePicker")]
    [InlineData("Avalonia.Controls.LayoutTransformControl")]
    [InlineData("Avalonia.Controls.Primitives.CalendarButton")]
    [InlineData("Avalonia.Controls.Primitives.CalendarDayButton")]
    [InlineData("Avalonia.Controls.Primitives.CalendarItem")]
    public void ShippedControlsExcludeRemovedComponents(string typeName)
    {
        Assert.Null(typeof(Control).Assembly.GetType(typeName));
    }

    [AvaloniaFact]
    public void FluentThemeKeepsApplicationControlsWithoutDateOrTimeTemplates()
    {
        var theme = new FluentTheme();
        Assert.True(theme.TryGetResource(typeof(Button), ThemeVariant.Dark, out _));
        Assert.True(theme.TryGetResource(typeof(TextBox), ThemeVariant.Dark, out _));
        Assert.False(theme.TryGetResource(typeof(DatePicker), ThemeVariant.Dark, out _));
        Assert.False(theme.TryGetResource(typeof(TimePicker), ThemeVariant.Dark, out _));
    }
}
