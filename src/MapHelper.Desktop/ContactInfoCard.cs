using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MapHelper.Core;

namespace MapHelper.Desktop;

internal sealed class ContactInfoCard : Border
{
    private readonly TextBlock _title = new() { FontSize = 17, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _summary = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
    private readonly TextBlock _raw = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSlateGray };
    private readonly Expander _more;
    public ContactInfoCard()
    {
        IsVisible = false; Width = 420; Padding = new Thickness(16);
        Background = new SolidColorBrush(Color.Parse("#F7101B29"));
        BorderBrush = new SolidColorBrush(Color.Parse("#58758D")); BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(8);
        _more = new Expander { Header = "All supplied API values", Content = _raw };
        Child = new ScrollViewer { Content = new StackPanel { Spacing = 12, Children = { _title, _summary, _more } } };
    }
    public void Update(ContactDetails details)
    {
        _title.Text = details.Title; _summary.Text = details.Summary; _raw.Text = details.RawValues;
        _more.IsVisible = details.RawValues.Length > 0;
    }
    public void NewContact() => _more.IsExpanded = false;
}
