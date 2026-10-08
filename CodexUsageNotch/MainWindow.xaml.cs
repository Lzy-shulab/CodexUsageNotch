using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CodexUsageNotch.Interop;
using CodexUsageNotch.Models;
using CodexUsageNotch.Presentation;
using CodexUsageNotch.Services;

namespace CodexUsageNotch;

public partial class MainWindow : Window
{
    private const int LeftMouseButton = 0x01;
    private const int EscapeKey = 0x1B;
    private const double TitleBarHeightDip = 36;
    private const double CapsuleHeightDip = 32;

    private readonly AppOptions _options;
    private readonly NotchViewModel _viewModel = new();
    private readonly CodexWindowLocator _windowLocator = new();
    private readonly UsageMonitor _usageMonitor = new();
    private readonly DispatcherTimer _trackingTimer;
    private readonly ResetDetailsWindow _detailsWindow;
    private readonly NativeMethods.WinEventProc _windowEventCallback;
    private readonly List<nint> _windowEventHooks = [];
    private nint _windowHandle;
    private CodexWindowInfo? _target;
    private AppTheme _theme;
    private long _nextThemeCheck;
    private double _responsiveScale = 1;
    private bool _mouseWasDown;
    private bool _escapeWasDown;
    private bool _hasShown;
    private bool _isClosing;
    private FrameworkElement? _detailsAnchor;

    internal MainWindow(AppOptions options)
    {
        _options = options;
        _theme = options.ThemeOverride ?? AppTheme.Dark;
        _windowEventCallback = OnNativeWindowEvent;
        InitializeComponent();
        Topmost = true;
        DataContext = _viewModel;
        _detailsWindow = new ResetDetailsWindow(_viewModel);
        _trackingTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(32)
        };
        _trackingTimer.Tick += OnTrackingTick;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _usageMonitor.SnapshotUpdated += OnSnapshotUpdated;
        _usageMonitor.ConnectionUnavailable += OnConnectionUnavailable;
    }

    private void OnSourceInitialized(object? sender, EventArgs eventArgs)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        var extendedStyle = NativeMethods.GetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle).ToInt64();
        extendedStyle |= NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle, new nint(extendedStyle));
        if (!_options.Preview)
        {
            AddWindowEventHook(NativeMethods.EventSystemForeground, NativeMethods.EventSystemForeground);
            AddWindowEventHook(NativeMethods.EventSystemMinimizeStart, NativeMethods.EventSystemMinimizeEnd);
            AddWindowEventHook(NativeMethods.EventObjectDestroy, NativeMethods.EventObjectLocationChange);
        }
    }

    private void AddWindowEventHook(uint firstEvent, uint lastEvent)
    {
        var hook = NativeMethods.SetWinEventHook(firstEvent, lastEvent, nint.Zero,
            _windowEventCallback, 0, 0,
            NativeMethods.WinEventOutOfContext | NativeMethods.WinEventSkipOwnProcess);
        if (hook != nint.Zero)
        {
            _windowEventHooks.Add(hook);
        }
    }

    private void OnNativeWindowEvent(nint hook, uint eventType, nint window,
        int objectId, int childId, uint eventThread, uint eventTime)
    {
        if (_isClosing || window == nint.Zero || objectId != 0 || childId != 0)
        {
            return;
        }

        if (eventType == NativeMethods.EventSystemForeground)
        {
            TrackWindow();
        }
        else if (window == _target?.Handle)
        {
            if (eventType is NativeMethods.EventObjectHide or NativeMethods.EventObjectDestroy or
                NativeMethods.EventSystemMinimizeStart)
            {
                HideCompanion();
            }
            else if (eventType is NativeMethods.EventObjectShow or NativeMethods.EventObjectLocationChange or
                     NativeMethods.EventSystemMinimizeEnd)
            {
                TrackWindow();
            }
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        _detailsWindow.AttachOwner(this);
        UpdateProgressArc(_viewModel.OuterRingRemainingPercent);

        if (_options.Demo)
        {
            _viewModel.ApplySnapshot(_options.DemoPlus ? CreatePlusDemoSnapshot() : CreateDemoSnapshot());
        }
        else
        {
            _usageMonitor.Start();
        }

        _trackingTimer.Start();
        TrackWindow();

        if (_options.OpenPopover)
        {
            Dispatcher.BeginInvoke(async () => await ShowDetailsAsync(), DispatcherPriority.Background);
        }
    }

    private void OnTrackingTick(object? sender, EventArgs eventArgs)
    {
        TrackWindow();
        TrackDismissGesture();
    }

    private void TrackWindow()
    {
        if (_options.Preview)
        {
            var previewTarget = _windowLocator.FindWindow();
            var clientWidthDip = previewTarget is null
                ? SystemParameters.WorkArea.Width
                : previewTarget.Bounds.Width / (previewTarget.Dpi / 96.0);
            ApplyResponsiveLayout(clientWidthDip);
            Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - Width) / 2);
            if (!_hasShown)
            {
                Top = 96;
                Reveal();
            }

            RepositionDetails();
            return;
        }

        var target = _windowLocator.FindWindow();
        _target = target;
        if (target is null || (!target.IsForeground && !IsCompanionForeground()))
        {
            HideCompanion();
            return;
        }

        var scale = target.Dpi / 96.0;
        ApplyResponsiveLayout(target.Bounds.Width / scale);
        var width = (int)Math.Round(Width * scale);
        var height = (int)Math.Round(Height * scale);
        var left = target.Bounds.Left + ((target.Bounds.Width - width) / 2);
        var top = target.Bounds.Top + (int)Math.Round((TitleBarHeightDip - Height) * scale / 2);
        NativeMethods.GetWindowRect(_windowHandle, out var currentBounds);
        if (currentBounds.Left != left || currentBounds.Top != top ||
            currentBounds.Width != width || currentBounds.Height != height)
        {
            NativeMethods.SetWindowPos(
                _windowHandle, nint.Zero, left, top, width, height,
                NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder | NativeMethods.SwpNoZOrder);
        }

        if (!IsVisible || !_hasShown)
        {
            Reveal();
        }

        if (_options.ThemeOverride is null && target.IsForeground && Environment.TickCount64 >= _nextThemeCheck)
        {
            _nextThemeCheck = Environment.TickCount64 + 500;
            var detectedTheme = _windowLocator.DetectTheme(target, _theme);
            if (detectedTheme != _theme)
            {
                _theme = detectedTheme;
                ThemeController.Apply(_theme);
            }
        }

        RepositionDetails();
    }

    private void Reveal()
    {
        Opacity = 1;
        if (!IsVisible)
        {
            Show();
        }

        _hasShown = true;
    }

    private void HideCompanion()
    {
        _detailsWindow.HidePopover();
        if (IsVisible)
        {
            Hide();
        }
        _hasShown = false;
    }

    internal void ApplyResponsiveLayout(double clientWidthDip)
    {
        var scale = Math.Round(Math.Clamp(clientWidthDip / 1280, 0.86, 1.12), 2);
        var compact = clientWidthDip < 1000;
        if (scale == _responsiveScale && compact == _viewModel.IsCompact)
        {
            return;
        }

        _responsiveScale = scale;
        _viewModel.IsCompact = compact;
        Width = _viewModel.CapsuleDesignWidth * scale;
        Height = CapsuleHeightDip * scale;
        _detailsWindow.ApplyScale(scale);
        UpdateLayout();
        Dispatcher.BeginInvoke(() =>
        {
            ((UIElement)RemainingLabel.Parent).InvalidateMeasure();
            ((UIElement)ResetLabel.Parent).InvalidateMeasure();
            ((UIElement)CreditLabel.Parent).InvalidateMeasure();
            UpdateLayout();
        }, DispatcherPriority.DataBind);
    }

    private async void OnResetSegmentClick(object sender, RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        if (_detailsWindow.IsOpen && !_viewModel.ShowQuotaDetails)
        {
            _detailsWindow.HidePopover(animate: true);
            return;
        }

        _viewModel.ShowQuotaDetails = false;
        _detailsAnchor = ResetSegment;
        await ShowDetailsAsync();
    }

    private void OnQuotaSegmentClick(object sender, RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        if (_detailsWindow.IsOpen && _viewModel.ShowQuotaDetails)
        {
            _detailsWindow.HidePopover(animate: true);
            return;
        }

        _viewModel.ShowQuotaDetails = true;
        _detailsAnchor = (FrameworkElement)sender;
        ShowAndPositionDetails();
    }

    private async Task ShowDetailsAsync()
    {
        if (_usageMonitor.LatestSnapshot?.ResetCredits is null && !_options.Demo)
        {
            _viewModel.SetLoadingCreditDetails();
        }

        ShowAndPositionDetails();

        if (_options.Demo)
        {
            return;
        }

        try
        {
            await _usageMonitor.RefreshDetailsAsync();
        }
        catch
        {
            _viewModel.SetStatus("重置到期时间暂时不可用");
        }
    }

    private void ShowAndPositionDetails()
    {
        var anchor = _detailsAnchor ?? ResetSegment;
        var detailsOffsetDip = anchor.TranslatePoint(
            new Point(anchor.ActualWidth / 2, 0), this).X - (_detailsWindow.Width / 2);
        var detailsTopDip = CapsuleSurface.TranslatePoint(
            new Point(0, CapsuleSurface.ActualHeight), this).Y + (3 * _responsiveScale);

        if (_options.Preview)
        {
            var dpi = _windowHandle == nint.Zero ? 96U : NativeMethods.GetDpiForWindow(_windowHandle);
            NativeMethods.GetWindowRect(_windowHandle, out var notchBounds);
            var scale = dpi / 96.0;
            _detailsWindow.ShowAt(
                notchBounds.Left + (int)Math.Round(detailsOffsetDip * scale),
                notchBounds.Top + (int)Math.Round(detailsTopDip * scale),
                dpi,
                new NativeRect(0, 0, (int)(SystemParameters.PrimaryScreenWidth * scale), (int)(SystemParameters.PrimaryScreenHeight * scale)));
            return;
        }

        if (_target is not null)
        {
            NativeMethods.GetWindowRect(_windowHandle, out var notchBounds);
            var scale = _target.Dpi / 96.0;
            _detailsWindow.ShowAt(
                notchBounds.Left + (int)Math.Round(detailsOffsetDip * scale),
                notchBounds.Top + (int)Math.Round(detailsTopDip * scale),
                _target.Dpi,
                _target.Bounds);
        }
    }

    private void RepositionDetails()
    {
        if (_detailsWindow.IsOpen)
        {
            ShowAndPositionDetails();
        }
    }

    private void TrackDismissGesture()
    {
        var mouseIsDown = (NativeMethods.GetAsyncKeyState(LeftMouseButton) & 0x8000) != 0;
        if (mouseIsDown && !_mouseWasDown && _detailsWindow.IsOpen && NativeMethods.GetCursorPos(out var point))
        {
            NativeMethods.GetWindowRect(_windowHandle, out var notchBounds);
            if (!notchBounds.Contains(point) && !_detailsWindow.ScreenBounds.Contains(point))
            {
                _detailsWindow.HidePopover(animate: true);
            }
        }
        _mouseWasDown = mouseIsDown;

        var escapeIsDown = (NativeMethods.GetAsyncKeyState(EscapeKey) & 0x8000) != 0;
        if (escapeIsDown && !_escapeWasDown && _detailsWindow.IsOpen)
        {
            _detailsWindow.HidePopover(animate: true);
        }
        _escapeWasDown = escapeIsDown;
    }

    private bool IsCompanionForeground()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == nint.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
        return processId == Environment.ProcessId;
    }

    private void OnSnapshotUpdated(UsageSnapshot snapshot)
    {
        Dispatcher.BeginInvoke(() => _viewModel.ApplySnapshot(snapshot));
    }

    private void OnConnectionUnavailable(string message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_usageMonitor.LatestSnapshot is null)
            {
                _viewModel.SetUnavailable(message);
            }
            else
            {
                _viewModel.SetStatus(message);
            }
        });
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(NotchViewModel.OuterRingRemainingPercent))
        {
            UpdateProgressArc(_viewModel.OuterRingRemainingPercent);
        }
        else if (eventArgs.PropertyName == nameof(NotchViewModel.InnerRingRemainingPercent))
        {
            SecondaryProgressArc.Data = CreateProgressGeometry(_viewModel.InnerRingRemainingPercent, radius: 4);
        }
        else if (eventArgs.PropertyName == nameof(NotchViewModel.CapsuleDesignWidth))
        {
            Width = _viewModel.CapsuleDesignWidth * _responsiveScale;
        }
    }

    private void UpdateProgressArc(int percent) => ProgressArc.Data = CreateProgressGeometry(percent);

    private static Geometry CreateProgressGeometry(int percent, double radius = 7)
    {
        const double center = 8;
        var clamped = Math.Clamp(percent, 0, 100);
        if (clamped <= 0)
        {
            return Geometry.Empty;
        }

        if (clamped >= 100)
        {
            return new EllipseGeometry(new Point(center, center), radius, radius);
        }

        var angle = clamped * 3.6;
        var start = new Point(center, center - radius);
        var radians = (angle - 90) * Math.PI / 180;
        var end = new Point(center + (radius * Math.Cos(radians)), center + (radius * Math.Sin(radians)));
        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(
            end,
            new Size(radius, radius),
            0,
            angle > 180,
            SweepDirection.Clockwise,
            true));
        return new PathGeometry([figure]);
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_options.Demo)
        {
            return;
        }

        try
        {
            await _usageMonitor.RefreshDetailsAsync();
        }
        catch
        {
            _viewModel.SetStatus("立即刷新失败，后台会继续重试");
        }
    }

    private void OnExitClick(object sender, RoutedEventArgs eventArgs)
    {
        ((App)Application.Current).ExitApplication();
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        if (_isClosing)
        {
            return;
        }
        _isClosing = true;
        _trackingTimer.Stop();
        foreach (var hook in _windowEventHooks)
        {
            NativeMethods.UnhookWinEvent(hook);
        }
        _windowEventHooks.Clear();
        _detailsWindow.Close();
        if (!_options.Demo)
        {
            _usageMonitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static UsageSnapshot CreateDemoSnapshot()
    {
        var localOffset = TimeSpan.FromHours(8);
        return new UsageSnapshot(
            99,
            new DateTimeOffset(2026, 10, 15, 12, 30, 0, localOffset),
            3,
            [
                new ResetCredit("demo-1", new DateTimeOffset(2026, 10, 23, 4, 50, 0, localOffset)),
                new ResetCredit("demo-2", new DateTimeOffset(2026, 10, 30, 3, 16, 0, localOffset)),
                new ResetCredit("demo-3", new DateTimeOffset(2026, 11, 7, 7, 44, 0, localOffset))
            ]);
    }

    internal static UsageSnapshot CreatePlusDemoSnapshot()
    {
        var offset = TimeSpan.FromHours(8);
        var weekly = new UsageWindow(80, new DateTimeOffset(2026, 10, 15, 12, 30, 0, offset), 10080);
        return new UsageSnapshot(weekly.RemainingPercent, weekly.ResetsAt, 3, CreateDemoSnapshot().ResetCredits,
            [new UsageWindow(99, new DateTimeOffset(2026, 10, 8, 18, 30, 0, offset), 300), weekly]);
    }
}
