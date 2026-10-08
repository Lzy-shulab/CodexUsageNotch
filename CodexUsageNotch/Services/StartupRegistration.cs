using System.IO;
using System.Runtime.InteropServices;

namespace CodexUsageNotch.Services;

internal static class StartupRegistration
{
    internal static void EnsureRegistered()
    {
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法获取程序路径。");
        Register(executablePath, Environment.GetFolderPath(Environment.SpecialFolder.Startup));
    }

    internal static void Register(string executablePath, string startupDirectory)
    {
        Directory.CreateDirectory(startupDirectory);
        var shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!;
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic? shortcut = null;
        try
        {
            shortcut = shell.CreateShortcut(Path.Combine(startupDirectory, "Codex Usage Notch.lnk"));
            shortcut.TargetPath = executablePath;
            shortcut.Arguments = string.Empty;
            shortcut.WorkingDirectory = Path.GetDirectoryName(executablePath);
            shortcut.Description = "Codex 额度浮窗：开机启动，打开 Codex 后显示";
            shortcut.Save();
        }
        finally
        {
            if (shortcut is not null)
            {
                Marshal.FinalReleaseComObject(shortcut);
            }
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
