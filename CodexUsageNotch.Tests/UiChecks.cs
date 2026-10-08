using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Reflection;
using CodexUsageNotch;
using CodexUsageNotch.Interop;
using CodexUsageNotch.Models;
using CodexUsageNotch.Presentation;
using CodexUsageNotch.Services;

internal static class UiChecks
{
    internal static void Run(string outputDirectory, bool galleryOnly = false)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources["InterfaceFont"] = new FontFamily("Segoe UI Variable Text, Microsoft YaHei UI");
            app.Resources["AccentBrush"] = new SolidColorBrush(Color.FromRgb(53, 199, 89));
            app.Resources["AccentSoftBrush"] = new SolidColorBrush(Color.FromArgb(38, 53, 199, 89));
            ThemeController.Apply(AppTheme.Dark);
            var notch = new MainWindow(new AppOptions(Preview: true, Demo: true));
            app.MainWindow = notch;
            notch.Show();
            app.Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    if (galleryOnly)
                    {
                        await ExportGalleryAsync(notch, outputDirectory);
                    }
                    else
                    {
                        await CheckAsync(notch, outputDirectory);
                    }
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    notch.Close();
                    app.Shutdown();
                }
            });
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("界面检查失败。", failure);
        }
    }

    private static async Task CheckAsync(MainWindow notch, string outputDirectory)
    {
        await Task.Delay(80);
        ((DispatcherTimer)typeof(MainWindow).GetField("_trackingTimer",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notch)!).Stop();
        var previewTarget = new CodexWindowLocator().FindWindow();
        var previewWidth = previewTarget is null ? SystemParameters.WorkArea.Width :
            previewTarget.Bounds.Width / (previewTarget.Dpi / 96.0);
        var previewScale = Math.Round(Math.Clamp(previewWidth / 1280, 0.86, 1.12), 2);
        Require(Math.Abs(notch.Height - 32 * previewScale) < 1,
            "演示模式沿用 Codex 窗口的自适应高度");
        var details = Application.Current.Windows.OfType<ResetDetailsWindow>().Single();
        ClickResetTime(notch);
        Require(!details.IsOpen && !notch.ResetInfoSegment.Focusable &&
                notch.ResetInfoSegment.Cursor == Cursors.Arrow,
            "Pro 重置时间是纯展示，点击不打开弹层");
        ClickReset(notch);
        Require(details.IsOpen, "点击入口后弹层打开");
        await Task.Delay(200);
        Require(details.IsVisible && Math.Abs(details.Opacity - 1) < 0.01, "弹层进入动画完成");
        ClickResetTime(notch);
        Require(details.IsOpen && !((NotchViewModel)notch.DataContext).ShowQuotaDetails,
            "Pro 点击重置时间不切换已有完整重置弹层");
        Console.WriteLine("PASS  Pro 重置时间点击无操作，不打开或切换额度详情");

        ClickReset(notch);
        await Task.Delay(25);
        var interruptedOpacity = details.Opacity;
        ClickReset(notch);
        Require(details.IsOpen && Math.Abs(details.Opacity - interruptedOpacity) < 0.08,
            "关闭过程中再次点击从当前透明度继续");
        await Task.Delay(200);
        Require(details.IsVisible && details.IsOpen && details.Opacity > 0.99,
            "连续点击后保持正确打开状态");
        ClickReset(notch);
        await Task.Delay(140);
        Require(!details.IsVisible && !details.IsOpen, "关闭动画结束后隐藏弹层");
        Console.WriteLine("PASS  点击开合、动画中再次点击及关闭状态");

        ClickReset(notch);
        await Task.Delay(180);
        var viewModel = (NotchViewModel)notch.DataContext;
        var loadedHeight = details.ActualHeight;
        viewModel.SetLoadingCreditDetails();
        await Task.Delay(50);
        Require(viewModel.CreditRows.Count == 3 && Math.Abs(details.ActualHeight - loadedHeight) < 1,
            "读取明细时预留已有重置次数的行高");
        viewModel.ApplySnapshot(DemoSnapshot());
        await Task.Delay(50);
        Require(Math.Abs(details.ActualHeight - loadedHeight) < 1, "明细加载后高度稳定");
        Console.WriteLine("PASS  明细读取前后高度稳定");

        Directory.CreateDirectory(outputDirectory);
        var proHeights = new Dictionary<double, double>();
        var proTextHeights = new Dictionary<double, double>();
        foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
        {
            ThemeController.Apply(theme);
            var previousWidth = 0.0;
            foreach (var clientWidth in new[] { 900.0, 1280.0, 1800.0 })
            {
                notch.ApplyResponsiveLayout(clientWidth);
                await Task.Delay(80);
                typeof(MainWindow).GetMethod("RepositionDetails", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(notch, null);
                Require(notch.ActualWidth > previousWidth, "窗口放大后胶囊随之放大");
                previousWidth = notch.ActualWidth;
                Require(Math.Abs((details.Width / 288) - (notch.Width / viewModel.CapsuleDesignWidth)) < 0.01,
                    "弹层与胶囊采用同一比例");
                var ring = notch.RemainingRing.TransformToAncestor(notch)
                    .TransformBounds(new Rect(notch.RemainingRing.RenderSize));
                var label = notch.RemainingLabel.TransformToAncestor(notch)
                    .TransformBounds(new Rect(notch.RemainingLabel.RenderSize));
                proHeights[clientWidth] = notch.ActualHeight;
                proTextHeights[clientWidth] = label.Height;
                var surface = notch.CapsuleSurface.TransformToAncestor(notch)
                    .TransformBounds(new Rect(notch.CapsuleSurface.RenderSize));
                var credit = notch.CreditLabel.TransformToAncestor(notch)
                    .TransformBounds(new Rect(notch.CreditLabel.RenderSize));
                Require(ring.Left >= surface.Left && credit.Right <= surface.Right,
                    "各段内容均位于胶囊内部");
                Require(Math.Abs((ring.Top + ring.Height / 2) - (label.Top + label.Height / 2)) < 1,
                    "圆环和文字垂直居中");
                CheckText(notch);
                CheckText(details);
                CaptureScene(notch, details, Path.Combine(outputDirectory,
                    $"pro-{theme.ToString().ToLowerInvariant()}-{clientWidth:0}.png"));
                Console.WriteLine($"PASS  {theme} {clientWidth:0} DIP：胶囊 {notch.Width:F1} × {notch.Height:F1}，弹层高 {details.ActualHeight:F1}");
            }
        }

        viewModel.ApplySnapshot(MainWindow.CreatePlusDemoSnapshot());
        ClickResetTime(notch);
        Require(details.IsOpen && !viewModel.ShowQuotaDetails,
            "点击重置时间不会把已有完整重置弹层切换为额度详情");
        details.HidePopover();
        ClickResetTime(notch);
        Require(!details.IsOpen && !notch.ResetInfoSegment.Focusable &&
                notch.ResetInfoSegment.Cursor != Cursors.Hand,
            "重置时间是纯展示，点击不打开弹层");
        Console.WriteLine("PASS  重置时间没有点击、手形光标或按钮焦点行为");
        Click(notch.PrimaryUsageSegment);
        await Task.Delay(180);
        Require(details.IsOpen && viewModel.ShowQuotaDetails && viewModel.QuotaRows.Count == 2,
            "Plus 额度入口打开两个独立窗口详情");
        foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
        {
            ThemeController.Apply(theme);
            foreach (var clientWidth in new[] { 900.0, 1280.0, 1800.0 })
            {
                notch.ApplyResponsiveLayout(clientWidth);
                await Task.Delay(80);
                typeof(MainWindow).GetMethod("RepositionDetails", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(notch, null);
                var surface = notch.CapsuleSurface.TransformToAncestor(notch)
                    .TransformBounds(new Rect(notch.CapsuleSurface.RenderSize));
                var first = notch.RemainingRing.TransformToAncestor(notch)
                    .TransformBounds(new Rect(notch.RemainingRing.RenderSize));
                var last = notch.CreditLabel.TransformToAncestor(notch)
                    .TransformBounds(new Rect(notch.CreditLabel.RenderSize));
                var usageText = notch.RemainingLabel.TransformToAncestor(notch)
                    .TransformBounds(new Rect(notch.RemainingLabel.RenderSize));
                var resetText = notch.ResetLabel.TransformToAncestor(notch)
                    .TransformBounds(new Rect(notch.ResetLabel.RenderSize));
                Require(Math.Abs(notch.ActualHeight - proHeights[clientWidth]) < 0.5 &&
                        Math.Abs(usageText.Height - proTextHeights[clientWidth]) < 0.5,
                    "相同 Codex 窗口宽度下 Plus 与 Pro 的胶囊和字号等高");
                Require(Math.Abs(usageText.Height - resetText.Height) < 0.5 &&
                        Math.Abs(usageText.Height - last.Height) < 0.5 &&
                        Math.Abs(usageText.Top - resetText.Top) < 0.5 &&
                        Math.Abs(usageText.Top - last.Top) < 0.5,
                    "剩余额度、重置时间和次数的文字等高且居中对齐");
                Require(first.Left >= surface.Left && last.Right <= surface.Right,
                    "Plus 双额度内容位于胶囊内部");
                Require(notch.SecondaryRing.IsVisible && notch.ResetIcon.IsVisible,
                    "Plus 双环放在额度段内，保留重置时钟");
                var inner = notch.SecondaryRing.TransformToAncestor(notch)
                    .TransformBounds(new Rect(notch.SecondaryRing.RenderSize));
                Require(Math.Abs(inner.Left - first.Left) < 1 && Math.Abs(inner.Top - first.Top) < 1,
                    "大小双环共用原圆环位置");
                Require(notch.SecondaryProgressArc.Data.Bounds.Width < notch.ProgressArc.Data.Bounds.Width,
                    "5 小时圆环小于周额度圆环");
                Require(((SolidColorBrush)notch.ProgressArc.Stroke).Color ==
                        ((SolidColorBrush)Application.Current.Resources["WeeklyAccentBrush"]).Color &&
                        ((SolidColorBrush)notch.SecondaryProgressArc.Stroke).Color ==
                        ((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color,
                    "外蓝内绿");
                CheckText(notch);
                CheckText(details);
                CaptureScene(notch, details, Path.Combine(outputDirectory,
                    $"plus-{theme.ToString().ToLowerInvariant()}-{clientWidth:0}.png"));
                CaptureCapsule(notch, Path.Combine(outputDirectory,
                    $"capsule-{theme.ToString().ToLowerInvariant()}-{clientWidth:0}.png"));
                Console.WriteLine($"PASS  Plus {theme} {clientWidth:0} DIP：与 Pro 等高，三段文字对齐，双环与内容完整显示");
            }
        }
        ClickReset(notch);
        Require(details.IsOpen && !viewModel.ShowQuotaDetails && viewModel.CreditCount == 3,
            "附件中的完整重置弹层保留三条到期明细");
        foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
        {
            ThemeController.Apply(theme);
            notch.ApplyResponsiveLayout(1280);
            await Task.Delay(80);
            typeof(MainWindow).GetMethod("RepositionDetails", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(notch, null);
            CheckText(details);
            CaptureScene(notch, details, Path.Combine(outputDirectory,
                $"plus-resets-{theme.ToString().ToLowerInvariant()}.png"));
        }
        viewModel.ApplySnapshot(MainWindow.CreatePlusDemoSnapshot() with { ResetCreditCount = 0, ResetCredits = [] });
        await Task.Delay(80);
        Require(details.IsOpen && !viewModel.ShowQuotaDetails && viewModel.CreditCount == 0,
            "Plus 零次重置入口显示空态");
        CheckText(details);
        CaptureScene(notch, details, Path.Combine(outputDirectory, "plus-zero-credits.png"));
        Click(notch.PrimaryUsageSegment);
        await Task.Delay(80);
        Require(details.IsOpen && viewModel.ShowQuotaDetails, "从完整重置切换回额度详情");
        viewModel.ApplySnapshot(DemoSnapshot());
        await Task.Delay(80);
        Require(!notch.SecondaryRing.IsVisible && notch.ResetIcon.IsVisible && viewModel.QuotaRows.Count == 1,
            "切回 Pro 后恢复原有单窗口布局");

        details.HidePopover();
        Require(!details.IsVisible, "切走时立即隐藏，不等待关闭动画");
        Console.WriteLine("界面检查全部通过。");
    }

    private static void ClickReset(MainWindow notch) => Click(notch.ResetSegment);

    private static void ClickResetTime(MainWindow notch) => notch.ResetInfoSegment.RaiseEvent(
        new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        { RoutedEvent = UIElement.MouseLeftButtonUpEvent });

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void CheckText(DependencyObject root)
    {
        if (root is TextBlock text && text.IsVisible && text.Text.Length > 0)
        {
            var measured = new FormattedText(text.Text, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
                text.FontSize, Brushes.White, VisualTreeHelper.GetDpi(text).PixelsPerDip);
            Require(text.ActualWidth + 1.5 >= measured.WidthIncludingTrailingWhitespace,
                $"文字完整显示：{text.Text}");
            if (((SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]).Color.R > 128 &&
                text.Foreground is SolidColorBrush foreground)
            {
                Require(Math.Max(foreground.Color.R, Math.Max(foreground.Color.G, foreground.Color.B)) > 128,
                    $"深色模式文字颜色可读：{text.Text}");
            }
        }
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            CheckText(VisualTreeHelper.GetChild(root, index));
        }
    }

    private static async Task ExportGalleryAsync(MainWindow notch, string outputDirectory)
    {
        await Task.Delay(100);
        ((DispatcherTimer)typeof(MainWindow).GetField("_trackingTimer",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notch)!).Stop();
        var details = Application.Current.Windows.OfType<ResetDetailsWindow>().Single();
        var viewModel = (NotchViewModel)notch.DataContext;
        Directory.CreateDirectory(outputDirectory);
        foreach (var plus in new[] { false, true })
        {
            viewModel.ApplySnapshot(plus ? MainWindow.CreatePlusDemoSnapshot() :
                DemoSnapshot() with { RemainingPercent = 91 });
            notch.ApplyResponsiveLayout(1280);
            if (!details.IsOpen)
            {
                ClickReset(notch);
            }
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
            {
                ThemeController.Apply(theme);
                await Task.Delay(200);
                typeof(MainWindow).GetMethod("RepositionDetails", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(notch, null);
                var name = $"{(plus ? "plus" : "pro")}-{theme.ToString().ToLowerInvariant()}.png";
                CaptureGallery(notch, details, plus, theme, Path.Combine(outputDirectory, name));
                Console.WriteLine($"已导出 {name}");
            }
        }
    }

    private static void CaptureGallery(MainWindow notch, ResetDetailsWindow details,
        bool plus, AppTheme theme, string path)
    {
        const double width = 760;
        const double height = 460;
        const double zoom = 1.2;
        const double outputScale = 2;
        NativeMethods.GetWindowRect(new WindowInteropHelper(notch).Handle, out var notchBounds);
        var dpiScale = VisualTreeHelper.GetDpi(notch).DpiScaleX;
        var offsetX = (details.ScreenBounds.Left - notchBounds.Left) / dpiScale;
        var offsetY = (details.ScreenBounds.Top - notchBounds.Top) / dpiScale;
        var sceneLeft = Math.Min(0, offsetX);
        var sceneWidth = Math.Max(notch.ActualWidth, offsetX + details.ActualWidth) - sceneLeft;
        var originX = (width - sceneWidth * zoom) / 2 - sceneLeft * zoom;
        const double originY = 175;
        var dark = theme == AppTheme.Dark;
        var background = new SolidColorBrush(dark ? Color.FromRgb(17, 18, 20) : Color.FromRgb(239, 242, 246));
        var foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"];
        var secondary = (Brush)Application.Current.Resources["SecondaryTextBrush"];
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(background, null, new Rect(0, 0, width, height));
            DrawText(drawing, "CODEX USAGE NOTCH", 12, FontWeights.Medium, secondary, 40, 30);
            DrawText(drawing, $"{(plus ? "Plus" : "Pro")} · {(dark ? "深色模式" : "浅色模式")}",
                30, FontWeights.SemiBold, foreground, 40, 56);
            DrawText(drawing, plus ? "外蓝环：本周额度    内绿环：5 小时额度" : "周额度、重置时间和完整重置次数，一眼可见。",
                14, FontWeights.Normal, secondary, 40, 105);
            drawing.DrawRectangle(ContentBrush((Visual)notch.Content, notch.ActualWidth, notch.ActualHeight), null,
                new Rect(originX, originY, notch.ActualWidth * zoom, notch.ActualHeight * zoom));
            drawing.DrawRectangle(ContentBrush((Visual)details.Content, details.ActualWidth, details.ActualHeight), null,
                new Rect(originX + offsetX * zoom, originY + offsetY * zoom,
                    details.ActualWidth * zoom, details.ActualHeight * zoom));
            DrawText(drawing, "Windows 额度浮窗 · 界面实际渲染，数值为演示数据", 11,
                FontWeights.Normal, secondary, 40, 425);
        }
        var bitmap = new RenderTargetBitmap((int)(width * outputScale), (int)(height * outputScale),
            96 * outputScale, 96 * outputScale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static VisualBrush ContentBrush(Visual visual, double width, double height) => new(visual)
    {
        ViewboxUnits = BrushMappingMode.Absolute,
        Viewbox = new Rect(0, 0, width, height),
        Stretch = Stretch.Fill
    };

    private static void DrawText(DrawingContext drawing, string text, double size, FontWeight weight,
        Brush brush, double x, double y)
    {
        var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
            new Typeface((FontFamily)Application.Current.Resources["InterfaceFont"], FontStyles.Normal,
                weight, FontStretches.Normal), size, brush, 2);
        drawing.DrawText(formatted, new Point(x, y));
    }

    private static void CaptureScene(MainWindow notch, ResetDetailsWindow details, string path)
    {
        NativeMethods.GetWindowRect(new WindowInteropHelper(notch).Handle, out var notchBounds);
        var scale = VisualTreeHelper.GetDpi(notch).DpiScaleX;
        var bounds = details.ScreenBounds;
        var offsetX = (bounds.Left - notchBounds.Left) / scale;
        var offsetY = (bounds.Top - notchBounds.Top) / scale;
        var originX = 8 - Math.Min(0, offsetX);
        var sceneWidth = Math.Max(notch.ActualWidth, offsetX + details.ActualWidth) + originX + 8;
        var sceneHeight = offsetY + details.ActualHeight + 12;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var dark = ((SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]).Color.R > 128;
            drawing.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(17, 18, 20) :
                Color.FromRgb(239, 242, 246)), null, new Rect(0, 0, sceneWidth, sceneHeight));
            drawing.DrawRectangle(new VisualBrush((Visual)notch.Content), null,
                new Rect(originX, 4, notch.ActualWidth, notch.ActualHeight));
            drawing.DrawRectangle(new VisualBrush((Visual)details.Content), null,
                new Rect(offsetX + originX, offsetY + 4, details.ActualWidth, details.ActualHeight));
        }
        var image = new RenderTargetBitmap((int)Math.Ceiling(sceneWidth * scale),
            (int)Math.Ceiling(sceneHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        image.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void CaptureCapsule(MainWindow notch, string path)
    {
        var scale = VisualTreeHelper.GetDpi(notch).DpiScaleX;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var dark = ((SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]).Color.R > 128;
            drawing.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(17, 18, 20) :
                Color.FromRgb(239, 242, 246)), null, new Rect(0, 0, notch.ActualWidth + 16, notch.ActualHeight + 16));
            drawing.DrawRectangle(new VisualBrush((Visual)notch.Content), null,
                new Rect(8, 8, notch.ActualWidth, notch.ActualHeight));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling((notch.ActualWidth + 16) * scale),
            (int)Math.Ceiling((notch.ActualHeight + 16) * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static UsageSnapshot DemoSnapshot() => new(99,
        new DateTimeOffset(2026, 10, 15, 12, 30, 0, TimeSpan.FromHours(8)), 3,
        Enumerable.Range(1, 3).Select(index => new ResetCredit($"demo-{index}",
            new DateTimeOffset(2026, 10, 23, 4, 50, 0, TimeSpan.FromHours(8)).AddDays((index - 1) * 7))).ToArray());

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
