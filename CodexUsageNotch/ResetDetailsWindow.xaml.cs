using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using CodexUsageNotch.Interop;
using CodexUsageNotch.Presentation;

namespace CodexUsageNotch;

public partial class ResetDetailsWindow : Window
{
    private nint _windowHandle;
    internal bool IsOpen { get; private set; }

    internal ResetDetailsWindow(NotchViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Left = -10000;
        Top = -10000;
        Opacity = 0;
    }

    internal void AttachOwner(MainWindow owner)
    {
        Owner = owner;
    }

    internal void ApplyScale(double scale)
    {
        Width = 288 * scale;
    }

    internal NativeRect ScreenBounds
    {
        get
        {
            if (_windowHandle != nint.Zero && NativeMethods.GetWindowRect(_windowHandle, out var bounds))
            {
                return bounds;
            }

            return default;
        }
    }

    internal void ShowAt(int left, int top, uint dpi, NativeRect ownerBounds)
    {
        var opening = !IsOpen;
        IsOpen = true;
        if (!IsVisible)
        {
            BeginAnimation(OpacityProperty, null);
            PopoverOffset.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
            Opacity = 0;
            PopoverOffset.Y = -3;
            Show();
        }

        UpdateLayout();
        var scale = dpi / 96.0;
        var width = (int)Math.Round(Width * scale);
        var height = (int)Math.Ceiling(ActualHeight * scale);
        var clampedLeft = Math.Clamp(
            left,
            ownerBounds.Left + (int)Math.Round(12 * scale),
            Math.Max(ownerBounds.Left, ownerBounds.Right - width - (int)Math.Round(12 * scale)));

        var current = ScreenBounds;
        if (current.Left != clampedLeft || current.Top != top || current.Width != width || current.Height != height)
        {
            NativeMethods.SetWindowPos(
                _windowHandle, nint.Zero, clampedLeft, top, width, height,
                NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder | NativeMethods.SwpNoZOrder);
        }

        if (opening && SystemParameters.ClientAreaAnimation)
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(Opacity, 1, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            PopoverOffset.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new DoubleAnimation(PopoverOffset.Y, 0, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }
        else if (opening)
        {
            BeginAnimation(OpacityProperty, null);
            PopoverOffset.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
            Opacity = 1;
            PopoverOffset.Y = 0;
        }
    }

    internal void HidePopover(bool animate = false)
    {
        IsOpen = false;
        if (animate && IsVisible && SystemParameters.ClientAreaAnimation)
        {
            var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(90))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fade.Completed += (_, _) =>
            {
                if (!IsOpen)
                {
                    HidePopover();
                }
            };
            BeginAnimation(OpacityProperty, fade);
            PopoverOffset.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new DoubleAnimation(PopoverOffset.Y, -2, TimeSpan.FromMilliseconds(90))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                });
            return;
        }

        BeginAnimation(OpacityProperty, null);
        PopoverOffset.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
        Opacity = 0;
        Hide();
    }

    private void OnSourceInitialized(object? sender, EventArgs eventArgs)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        var extendedStyle = NativeMethods.GetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle).ToInt64();
        extendedStyle |= NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle, new nint(extendedStyle));
    }
}
