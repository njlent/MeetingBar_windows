using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace MeetingBar.SettingsApp;

/// <summary>
/// Places a settings card's description beside its control, and moves the control below the
/// description when the card is too narrow to keep a readable text column.
/// </summary>
public sealed class SettingsCardRow : Panel
{
    private bool _stacked;

    public double ColumnSpacing { get; set; } = 24;
    public double RowSpacing { get; set; } = 10;
    public double MinimumTextWidth { get; set; } = 220;

    protected override Size MeasureOverride(Size availableSize)
    {
        var text = Children.Count > 0 ? Children[0] : null;
        var control = Children.Count > 1 ? Children[1] : null;
        if (text == null) return new Size();
        if (control == null)
        {
            text.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            return text.DesiredSize;
        }

        control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var controlWidth = control.DesiredSize.Width;
        var bounded = !double.IsInfinity(availableSize.Width);
        _stacked = bounded && availableSize.Width - controlWidth - ColumnSpacing < MinimumTextWidth;

        if (_stacked)
        {
            text.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            control.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            return new Size(availableSize.Width, text.DesiredSize.Height + RowSpacing + control.DesiredSize.Height);
        }

        var textWidth = bounded ? availableSize.Width - controlWidth - ColumnSpacing : double.PositiveInfinity;
        text.Measure(new Size(textWidth, double.PositiveInfinity));
        var width = bounded ? availableSize.Width : text.DesiredSize.Width + ColumnSpacing + controlWidth;
        return new Size(width, Math.Max(text.DesiredSize.Height, control.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var text = Children.Count > 0 ? Children[0] : null;
        var control = Children.Count > 1 ? Children[1] : null;
        if (text == null) return finalSize;
        if (control == null)
        {
            text.Arrange(new Rect(0, 0, finalSize.Width, text.DesiredSize.Height));
            return finalSize;
        }

        if (_stacked)
        {
            text.Arrange(new Rect(0, 0, finalSize.Width, text.DesiredSize.Height));
            // Text inputs use the full card width once stacked; other controls keep their natural size.
            var controlWidth = control is TextBox or PasswordBox ? finalSize.Width : Math.Min(control.DesiredSize.Width, finalSize.Width);
            control.Arrange(new Rect(0, text.DesiredSize.Height + RowSpacing, controlWidth, control.DesiredSize.Height));
            return finalSize;
        }

        var width = control.DesiredSize.Width;
        var textWidth = Math.Max(0, finalSize.Width - width - ColumnSpacing);
        text.Arrange(new Rect(0, (finalSize.Height - text.DesiredSize.Height) / 2, textWidth, text.DesiredSize.Height));
        control.Arrange(new Rect(finalSize.Width - width, (finalSize.Height - control.DesiredSize.Height) / 2, width, control.DesiredSize.Height));
        return finalSize;
    }
}

/// <summary>Flows buttons and other small controls onto additional lines when they run out of width.</summary>
public sealed class WrapPanel : Panel
{
    public double HorizontalSpacing { get; set; } = 8;
    public double VerticalSpacing { get; set; } = 8;

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return Flow(availableSize.Width, arrange: false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Flow(finalSize.Width, arrange: true);
        return finalSize;
    }

    private Size Flow(double maximumWidth, bool arrange)
    {
        double x = 0, y = 0, lineHeight = 0, width = 0;
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > maximumWidth)
            {
                y += lineHeight + VerticalSpacing;
                x = 0;
                lineHeight = 0;
            }
            if (arrange) child.Arrange(new Rect(x, y, Math.Min(size.Width, maximumWidth), size.Height));
            x += size.Width + HorizontalSpacing;
            width = Math.Max(width, x - HorizontalSpacing);
            lineHeight = Math.Max(lineHeight, size.Height);
        }
        return new Size(width, y + lineHeight);
    }
}
