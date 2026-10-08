using System.Windows;
using CodexUsageNotch.Presentation;
using CodexUsageNotch.Services;

namespace CodexUsageNotch;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;

    internal AppOptions Options { get; private set; } = new();

    private void OnStartup(object sender, StartupEventArgs eventArgs)
    {
        Options = AppOptions.Parse(eventArgs.Args);
        var mutexName = Options.Preview ? @"Local\CodexUsageNotch.Preview" : @"Local\CodexUsageNotch";
        _singleInstanceMutex = new Mutex(true, mutexName, out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        ThemeController.Apply(Options.ThemeOverride ?? AppTheme.Dark);

        var window = new MainWindow(Options)
        {
            Left = -10000,
            Top = -10000,
            Opacity = 0
        };
        MainWindow = window;
        window.Show();
    }

    internal void ExitApplication()
    {
        MainWindow?.Close();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}

internal sealed record AppOptions(
    AppTheme? ThemeOverride = null,
    bool Preview = false,
    bool Demo = false,
    bool OpenPopover = false,
    bool DemoPlus = false)
{
    internal static AppOptions Parse(IEnumerable<string> arguments)
    {
        AppTheme? theme = null;
        var preview = false;
        var demo = false;
        var popover = false;
        var plus = false;

        foreach (var argument in arguments)
        {
            if (string.Equals(argument, "--theme=dark", StringComparison.OrdinalIgnoreCase))
            {
                theme = AppTheme.Dark;
            }
            else if (string.Equals(argument, "--theme=light", StringComparison.OrdinalIgnoreCase))
            {
                theme = AppTheme.Light;
            }
            else if (string.Equals(argument, "--preview", StringComparison.OrdinalIgnoreCase))
            {
                preview = true;
            }
            else if (string.Equals(argument, "--demo", StringComparison.OrdinalIgnoreCase))
            {
                demo = true;
            }
            else if (string.Equals(argument, "--popover", StringComparison.OrdinalIgnoreCase))
            {
                popover = true;
            }
            else if (string.Equals(argument, "--demo=plus", StringComparison.OrdinalIgnoreCase))
            {
                demo = true;
                plus = true;
            }
        }

        return new AppOptions(theme, preview, demo, popover, plus);
    }
}
