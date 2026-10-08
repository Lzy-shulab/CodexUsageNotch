using System.Windows;
using System.Windows.Media;
using CodexUsageNotch.Services;

namespace CodexUsageNotch.Presentation;

internal static class ThemeController
{
    public static void Apply(AppTheme theme)
    {
        var resources = Application.Current.Resources;
        if (theme == AppTheme.Dark)
        {
            resources["PrimaryTextBrush"] = Brush("#FFF5F7F8");
            resources["SecondaryTextBrush"] = Brush("#FFB9BDC1");
            resources["MutedTextBrush"] = Brush("#FF8B9298");
            resources["AccentLabelBrush"] = Brush("#FF66DC84");
            resources["WeeklyAccentBrush"] = Brush("#FF5AA9FF");
            resources["DividerBrush"] = Brush("#24FFFFFF");
            resources["NotchBorderBrush"] = Gradient("#2CFFFFFF", "#10FFFFFF");
            resources["GlassSheenBrush"] = Gradient("#04FFFFFF", "#00FFFFFF");
            resources["GlassReflectionBrush"] = Brush("#00FFFFFF");
            resources["CapsuleShadowOpacity"] = 0.16;
            resources["PopoverShadowOpacity"] = 0.22;
            resources["InnerHighlightBrush"] = Brush("#38FFFFFF");
            resources["HoverBrush"] = Brush("#10FFFFFF");
            resources["ProgressTrackBrush"] = Brush("#38FFFFFF");
            resources["PopoverBorderBrush"] = Gradient("#30FFFFFF", "#15FFFFFF");
            resources["NotchSurfaceBrush"] = GlassGradient("#ED282A2C", "#EE191B1D", "#F0101214");
            resources["PopoverSurfaceBrush"] = GlassGradient("#FC232527", "#FC181A1C", "#FD111315");
        }
        else
        {
            resources["PrimaryTextBrush"] = Brush("#FF17191B");
            resources["SecondaryTextBrush"] = Brush("#FF545A60");
            resources["MutedTextBrush"] = Brush("#FF7C838A");
            resources["AccentLabelBrush"] = Brush("#FF157D37");
            resources["WeeklyAccentBrush"] = Brush("#FF0070D9");
            resources["DividerBrush"] = Brush("#24000000");
            resources["NotchBorderBrush"] = Gradient("#EFFFFFFF", "#42000000");
            resources["GlassSheenBrush"] = Gradient("#60FFFFFF", "#08FFFFFF", diagonal: true);
            resources["GlassReflectionBrush"] = Gradient("#80FFFFFF", "#00FFFFFF");
            resources["CapsuleShadowOpacity"] = 0.28;
            resources["PopoverShadowOpacity"] = 0.3;
            resources["InnerHighlightBrush"] = Brush("#CFFFFFFF");
            resources["HoverBrush"] = Brush("#0C000000");
            resources["ProgressTrackBrush"] = Brush("#26000000");
            resources["PopoverBorderBrush"] = Brush("#2D000000");
            resources["NotchSurfaceBrush"] = GlassGradient("#DFFFFFFF", "#D6F2F5F8", "#DEDBE2E9");
            resources["PopoverSurfaceBrush"] = Gradient("#FCFFFFFF", "#FCF1F4F7", diagonal: true);
        }
    }

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush GlassGradient(string top, string middle, string bottom)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(top), 0));
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(middle), 0.45));
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(bottom), 1));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush Gradient(string first, string second, bool diagonal = false)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = diagonal ? new Point(1, 1) : new Point(0, 1)
        };
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(first), 0));
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(second), 1));
        brush.Freeze();
        return brush;
    }
}
