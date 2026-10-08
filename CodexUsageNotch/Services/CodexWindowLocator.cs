using System.Diagnostics;
using System.Runtime.InteropServices;
using CodexUsageNotch.Interop;
using Microsoft.Win32;

namespace CodexUsageNotch.Services;

internal enum AppTheme
{
    Dark,
    Light
}

internal sealed record CodexWindowInfo(nint Handle, NativeRect Bounds, uint Dpi, bool IsForeground);

internal sealed class CodexWindowLocator
{
    private const uint InvalidPixel = 0xFFFFFFFF;
    private nint _knownWindow;
    private nint _lastForeground;
    private bool _foregroundIsCodex;
    private long _nextDiscovery;

    public CodexWindowInfo? FindWindow()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground != _lastForeground)
        {
            _lastForeground = foreground;
            _foregroundIsCodex = foreground != nint.Zero && IsCodexWindow(foreground);
        }

        if (_foregroundIsCodex && ReadWindow(foreground, foreground) is { } activeWindow)
        {
            _knownWindow = foreground;
            return activeWindow;
        }

        if (ReadWindow(_knownWindow, foreground) is { } knownWindow)
        {
            return knownWindow;
        }

        if (Environment.TickCount64 < _nextDiscovery)
        {
            return null;
        }
        _nextDiscovery = Environment.TickCount64 + 1000;
        var candidates = new List<CodexWindowInfo>();

        NativeMethods.EnumWindows((window, _) =>
        {
            if (ReadWindow(window, foreground) is not { } candidate || !IsCodexWindow(window))
            {
                return true;
            }

            candidates.Add(candidate);
            return true;
        }, nint.Zero);

        var selected = candidates
            .OrderByDescending(candidate => candidate.IsForeground)
            .ThenByDescending(candidate => (long)candidate.Bounds.Width * candidate.Bounds.Height)
            .FirstOrDefault();
        _knownWindow = selected?.Handle ?? nint.Zero;
        return selected;
    }

    private static CodexWindowInfo? ReadWindow(nint window, nint foreground)
    {
        if (window == nint.Zero || !NativeMethods.IsWindowVisible(window) ||
            NativeMethods.IsIconic(window) || IsCloaked(window) ||
            !TryGetBounds(window, out var bounds) || bounds.Width < 520 || bounds.Height < 320)
        {
            return null;
        }

        var dpi = NativeMethods.GetDpiForWindow(window);
        return new CodexWindowInfo(window, bounds, dpi == 0 ? 96U : dpi, window == foreground);
    }

    public AppTheme DetectTheme(CodexWindowInfo target, AppTheme previousTheme)
    {
        var luminance = SampleClientLuminance(target.Bounds);
        if (luminance is null)
        {
            return ReadWindowsTheme();
        }

        const double darkToLightThreshold = 0.62;
        const double lightToDarkThreshold = 0.42;
        return previousTheme switch
        {
            AppTheme.Dark when luminance >= darkToLightThreshold => AppTheme.Light,
            AppTheme.Light when luminance <= lightToDarkThreshold => AppTheme.Dark,
            _ => previousTheme
        };
    }

    private static bool IsCodexWindow(nint window)
    {
        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (processId == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            if (!string.Equals(process.ProcessName, "ChatGPT", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var executablePath = process.MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(executablePath) &&
                executablePath.Contains("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var buffer = new char[256];
            var length = NativeMethods.GetWindowText(window, buffer, buffer.Length);
            return length > 0 && string.Equals(new string(buffer, 0, length), "ChatGPT", StringComparison.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static bool TryGetBounds(nint window, out NativeRect bounds)
    {
        var result = NativeMethods.DwmGetWindowAttribute(
            window,
            NativeMethods.DwmwaExtendedFrameBounds,
            out bounds,
            Marshal.SizeOf<NativeRect>());

        return result == 0 || NativeMethods.GetWindowRect(window, out bounds);
    }

    private static bool IsCloaked(nint window)
    {
        int cloaked;
        var result = NativeMethods.DwmGetWindowAttribute(
            window,
            NativeMethods.DwmwaCloaked,
            out cloaked,
            sizeof(int));
        return result == 0 && cloaked != 0;
    }

    private static double? SampleClientLuminance(NativeRect bounds)
    {
        var deviceContext = NativeMethods.GetDC(nint.Zero);
        if (deviceContext == nint.Zero)
        {
            return null;
        }

        try
        {
            var samples = new List<double>(6);
            var centerX = bounds.Left + (bounds.Width / 2);
            var sampleXs = new[] { centerX - 120, centerX, centerX + 120 };
            var sampleYs = new[] { bounds.Top + 76, bounds.Top + 108 };

            foreach (var x in sampleXs)
            {
                foreach (var y in sampleYs)
                {
                    var color = NativeMethods.GetPixel(deviceContext, x, y);
                    if (color == InvalidPixel)
                    {
                        continue;
                    }

                    var red = color & 0xFF;
                    var green = (color >> 8) & 0xFF;
                    var blue = (color >> 16) & 0xFF;
                    samples.Add(((0.2126 * red) + (0.7152 * green) + (0.0722 * blue)) / 255.0);
                }
            }

            return samples.Count == 0 ? null : samples.Average();
        }
        finally
        {
            NativeMethods.ReleaseDC(nint.Zero, deviceContext);
        }
    }

    private static AppTheme ReadWindowsTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0
                ? AppTheme.Light
                : AppTheme.Dark;
        }
        catch
        {
            return AppTheme.Dark;
        }
    }
}
