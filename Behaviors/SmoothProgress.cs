using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;

namespace EsoAddons.Behaviors;

/// <summary>Attach to a ProgressBar via <c>b:SmoothProgress.Target="{Binding ...}"</c>: each new target value
/// is animated (ease-out) into the bar's Value instead of snapping. This makes coarse/jumpy progress reports
/// — e.g. Velopack's update download, which reports roughly 0 → 70 (download) → 100 (delta-apply) — glide
/// smoothly. Bind a "%" label to the bar's own <c>Value</c> so the displayed number climbs through every
/// integer as the bar fills, rather than jumping between the few raw data points.</summary>
public static class SmoothProgress
{
    public static readonly DependencyProperty TargetProperty =
        DependencyProperty.RegisterAttached("Target", typeof(double), typeof(SmoothProgress),
            new PropertyMetadata(0.0, OnTargetChanged));

    public static void SetTarget(DependencyObject o, double value) => o.SetValue(TargetProperty, value);
    public static double GetTarget(DependencyObject o) => (double)o.GetValue(TargetProperty);

    private static void OnTargetChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not ProgressBar bar) return;
        var to = (double)e.NewValue;
        // A reset back to 0 (new download starting) should be instant — only forward motion glides.
        if (to <= 0)
        {
            bar.BeginAnimation(RangeBase.ValueProperty, null);
            bar.Value = 0;
            return;
        }
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        bar.BeginAnimation(RangeBase.ValueProperty, anim);
    }
}
