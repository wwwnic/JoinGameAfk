using System.Windows;
using System.Windows.Controls;

namespace JoinGameAfk.Controls;

public sealed class JustifiedWrapPanel : Panel
{
    public static readonly DependencyProperty JustifyLastRowProperty =
        DependencyProperty.Register(
            nameof(JustifyLastRow),
            typeof(bool),
            typeof(JustifiedWrapPanel),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsArrange));

    public bool JustifyLastRow
    {
        get => (bool)GetValue(JustifyLastRowProperty);
        set => SetValue(JustifyLastRowProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double availableWidth = double.IsInfinity(availableSize.Width)
            ? double.PositiveInfinity
            : Math.Max(0, availableSize.Width);
        var childAvailableSize = new Size(availableWidth, double.PositiveInfinity);
        var currentRowSize = new Size();
        var desiredSize = new Size();

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(childAvailableSize);
            Size childSize = child.DesiredSize;

            if (currentRowSize.Width > 0
                && currentRowSize.Width + childSize.Width > availableWidth)
            {
                desiredSize.Width = Math.Max(desiredSize.Width, currentRowSize.Width);
                desiredSize.Height += currentRowSize.Height;
                currentRowSize = childSize;
            }
            else
            {
                currentRowSize.Width += childSize.Width;
                currentRowSize.Height = Math.Max(currentRowSize.Height, childSize.Height);
            }
        }

        desiredSize.Width = Math.Max(desiredSize.Width, currentRowSize.Width);
        desiredSize.Height += currentRowSize.Height;

        return desiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int rowStartIndex = 0;
        double rowWidth = 0;
        double rowHeight = 0;
        double rowTop = 0;
        double finalWidth = Math.Max(0, finalSize.Width);

        for (int i = 0; i < InternalChildren.Count; i++)
        {
            Size childSize = InternalChildren[i].DesiredSize;

            if (rowWidth > 0 && rowWidth + childSize.Width > finalWidth)
            {
                ArrangeRow(rowStartIndex, i, rowTop, rowWidth, rowHeight, finalWidth, justify: true);
                rowTop += rowHeight;
                rowStartIndex = i;
                rowWidth = childSize.Width;
                rowHeight = childSize.Height;
            }
            else
            {
                rowWidth += childSize.Width;
                rowHeight = Math.Max(rowHeight, childSize.Height);
            }
        }

        ArrangeRow(rowStartIndex, InternalChildren.Count, rowTop, rowWidth, rowHeight, finalWidth, JustifyLastRow);
        return finalSize;
    }

    private void ArrangeRow(
        int startIndex,
        int endIndex,
        double top,
        double rowWidth,
        double rowHeight,
        double finalWidth,
        bool justify)
    {
        int childCount = endIndex - startIndex;
        if (childCount <= 0)
            return;

        double extraGap = justify && childCount > 1
            ? Math.Max(0, finalWidth - rowWidth) / (childCount - 1)
            : 0;
        double left = 0;

        for (int i = startIndex; i < endIndex; i++)
        {
            UIElement child = InternalChildren[i];
            Size childSize = child.DesiredSize;
            child.Arrange(new Rect(left, top, childSize.Width, rowHeight));
            left += childSize.Width + extraGap;
        }
    }
}
