using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Qivoryx;

/// <summary>Animate wheel navigation on document pages; controls retain their own scrolling.</summary>
public sealed class SmoothScrollViewer : ScrollViewer
{
    static readonly DependencyProperty WheelOffsetProperty = DependencyProperty.Register("WheelOffset", typeof(double), typeof(SmoothScrollViewer), new PropertyMetadata(0.0, (d, e) => ((SmoothScrollViewer)d).ScrollToVerticalOffset((double)e.NewValue)));
    double target;
    bool animating;
    void StopWheel()
    {
        var current = VerticalOffset;SetValue(WheelOffsetProperty, current);BeginAnimation(WheelOffsetProperty, null);animating = false;target = current;
    }
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e) { StopWheel();base.OnPreviewMouseDown(e); }
    protected override void OnPreviewKeyDown(KeyEventArgs e) { StopWheel();base.OnPreviewKeyDown(e); }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (!SystemParameters.ClientAreaAnimation || ScrollableHeight <= 0 || Keyboard.Modifiers != ModifierKeys.None) { base.OnMouseWheel(e);return; }
        var start = VerticalOffset;
        if (!animating) target = start;
        target = Math.Clamp(target - e.Delta * .85, 0, ScrollableHeight);
        var animation = new DoubleAnimation(start, target, TimeSpan.FromMilliseconds(170)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        animation.Completed += (_, _) => animating = false;
        animating = true;BeginAnimation(WheelOffsetProperty, animation, HandoffBehavior.SnapshotAndReplace);e.Handled = true;
    }
    public void ResetPosition()
    {
        BeginAnimation(WheelOffsetProperty, null);animating = false;target = 0;SetValue(WheelOffsetProperty, 0.0);ScrollToTop();
    }
}
