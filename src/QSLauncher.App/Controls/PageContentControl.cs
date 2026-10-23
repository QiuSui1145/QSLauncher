using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace QSLauncher.App.Controls;

/// <summary>
/// Gives every page switch a short fade-and-rise transition without touching
/// each individual view. The animation is intentionally subtle so it stays
/// usable on low-end machines.
/// </summary>
public sealed class PageContentControl : ContentControl
{
    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        if (newContent is not FrameworkElement page)
        {
            return;
        }

        page.Opacity = 0;
        var translate = new TranslateTransform(0, 12);
        page.RenderTransform = translate;

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        page.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190))
            {
                EasingFunction = easing
            });
        translate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(230))
            {
                EasingFunction = easing
            });
    }
}
