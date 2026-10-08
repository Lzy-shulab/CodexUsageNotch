using System.IO;

namespace CodexUsageNotch.Services;

internal static class DiagnosticLog
{
    internal static void Write(Exception exception)
    {
        Write(exception.ToString());
    }

    internal static void Write(string message)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CODEX_USAGE_NOTCH_DEBUG"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexUsageNotch");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "debug.log"),
                $"[{DateTimeOffset.Now:O}] {message}\n\n");
        }
        catch
        {
        }
    }
}
