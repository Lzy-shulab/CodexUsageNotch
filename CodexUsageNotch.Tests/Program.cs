using System.Text.Json;
using System.IO;
using System.Runtime.InteropServices;
using CodexUsageNotch.Models;
using CodexUsageNotch.Services;
using CodexUsageNotch.Presentation;

if (args.Contains("--gallery", StringComparer.OrdinalIgnoreCase))
{
    UiChecks.Run(Path.Combine(Environment.CurrentDirectory, "docs", "images"), galleryOnly: true);
    return;
}

if (args.Contains("--ui-check", StringComparer.OrdinalIgnoreCase))
{
    UiChecks.Run(Path.Combine(Environment.CurrentDirectory, "artifacts", "ui-1.1.2"));
    return;
}

if (args.Contains("--integration", StringComparer.OrdinalIgnoreCase))
{
    await using var client = new CodexAppServerClient();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    var response = await client.ReadRateLimitsAsync(excludeResetCreditDetails: false, timeout.Token);
    var actual = UsageResponseParser.Parse(response);
    Console.WriteLine($"剩余：{actual.RemainingPercent?.ToString() ?? "未知"}%");
    Console.WriteLine($"重置：{UsageTextFormatter.FormatReset(actual.ResetsAt)}");
    Console.WriteLine($"完整重置：{actual.ResetCreditCount} 次");
    foreach (var window in actual.Windows ?? [])
    {
        Console.WriteLine($"{window.Label}（{window.DurationMinutes} 分钟）：剩余 {window.RemainingPercent?.ToString() ?? "未知"}%，{UsageTextFormatter.FormatReset(window.ResetsAt)}");
    }
    foreach (var credit in actual.ResetCredits ?? [])
    {
        Console.WriteLine(UsageTextFormatter.FormatExpiration(credit.ExpiresAt));
    }
    return;
}

var checks = new (string Name, Action Run)[]
{
    ("解析长期额度与完整重置明细", ParseSecondaryWindowAndCredits),
    ("优先选择 Codex 额度桶并限制百分比范围", PreferCodexBucketAndClamp),
    ("缺少长期窗口时回退到短期窗口", FallBackToPrimaryWindow),
    ("Plus 同时保留两个额度窗口及各自重置时间", ParsePlusWindows),
    ("Pro primary 周窗口按实际时长识别", ParseProWeeklyPrimary),
    ("双额度与单额度切换清除旧窗口", SwitchWindowPresentation),
    ("格式化本地重置与到期时间", FormatLocalTimes),
    ("开机启动快捷方式指向正式程序且不带演示参数", RegisterStartupShortcut)
};

foreach (var check in checks)
{
    check.Run();
    Console.WriteLine($"PASS  {check.Name}");
}

Console.WriteLine($"全部 {checks.Length} 项自检通过。");
return;

static void RegisterStartupShortcut()
{
    var directory = Path.Combine(Path.GetTempPath(), "CodexUsageNotch-startup-" + Guid.NewGuid().ToString("N"));
    var executablePath = Path.Combine(directory, "folder with spaces", "CodexUsageNotch.exe");
    dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!)!;
    dynamic? shortcut = null;
    try
    {
        StartupRegistration.Register(executablePath, directory);
        shortcut = shell.CreateShortcut(Path.Combine(directory, "Codex Usage Notch.lnk"));
        Equal(executablePath, (string)shortcut.TargetPath, "启动程序路径");
        Equal(string.Empty, (string)shortcut.Arguments, "正式启动不带演示参数");
        Equal(Path.GetDirectoryName(executablePath), (string)shortcut.WorkingDirectory, "启动工作目录");
    }
    finally
    {
        if (shortcut is not null)
        {
            Marshal.FinalReleaseComObject(shortcut);
        }
        Marshal.FinalReleaseComObject(shell);
        File.Delete(Path.Combine(directory, "Codex Usage Notch.lnk"));
        Directory.Delete(directory);
    }
}

static void ParseSecondaryWindowAndCredits()
{
    const string json = """
    {
      "rateLimits": {
        "primary": { "usedPercent": 40, "resetsAt": 1791432000 },
        "secondary": { "usedPercent": 1, "resetsAt": 1792038600 }
      },
      "rateLimitsByLimitId": null,
      "rateLimitResetCredits": {
        "availableCount": 3,
        "credits": [
          { "id": "third", "grantedAt": 1, "expiresAt": 1793480640, "resetType": "codexRateLimits", "status": "available" },
          { "id": "used", "grantedAt": 1, "expiresAt": 1792872000, "resetType": "codexRateLimits", "status": "redeemed" },
          { "id": "first", "grantedAt": 1, "expiresAt": 1792702200, "resetType": "codexRateLimits", "status": "available" },
          { "id": "second", "grantedAt": 1, "expiresAt": 1793301360, "resetType": "codexRateLimits", "status": "available" }
        ]
      }
    }
    """;

    var snapshot = Parse(json);
    Equal(99, snapshot.RemainingPercent, "剩余额度");
    Equal(1792038600L, snapshot.ResetsAt?.ToUnixTimeSeconds(), "长期额度重置时间");
    Equal(3, snapshot.ResetCreditCount, "完整重置数量");
    Equal(3, snapshot.ResetCredits?.Count, "可用明细数量");
    Equal("first", snapshot.ResetCredits?[0].Id, "明细按到期时间排序");
}

static void PreferCodexBucketAndClamp()
{
    const string json = """
    {
      "rateLimits": { "secondary": { "usedPercent": 20 } },
      "rateLimitsByLimitId": {
        "other": { "secondary": { "usedPercent": 5 } },
        "codex": { "secondary": { "usedPercent": 108, "resetsAt": 1792038600 } }
      },
      "rateLimitResetCredits": { "availableCount": -2, "credits": null }
    }
    """;

    var snapshot = Parse(json);
    Equal(0, snapshot.RemainingPercent, "负剩余额度限制为零");
    Equal(0, snapshot.ResetCreditCount, "负重置次数限制为零");
    Equal(null, snapshot.ResetCredits, "后台轮询不含明细时保留 null");
}

static void FallBackToPrimaryWindow()
{
    const string json = """
    {
      "rateLimits": { "primary": { "usedPercent": 26, "resetsAt": 1791432000 }, "secondary": null },
      "rateLimitResetCredits": null
    }
    """;

    var snapshot = Parse(json);
    Equal(74, snapshot.RemainingPercent, "短期额度回退");
    Equal(1791432000L, snapshot.ResetsAt?.ToUnixTimeSeconds(), "短期额度重置时间");
}

static void FormatLocalTimes()
{
    var timestamp = new DateTimeOffset(2026, 10, 15, 4, 30, 0, TimeSpan.Zero);
    var resetText = UsageTextFormatter.FormatReset(timestamp);
    var expirationText = UsageTextFormatter.FormatExpiration(timestamp);
    True(resetText.EndsWith(" 重置", StringComparison.Ordinal), "重置文本后缀");
    True(expirationText.EndsWith(" 到期", StringComparison.Ordinal), "到期文本后缀");
    True(!resetText.Contains("北京时间", StringComparison.Ordinal), "不显示时区标签");
}

static void ParsePlusWindows()
{
    var snapshot = Parse("""
    { "rateLimitsByLimitId": { "codex": {
        "primary": { "usedPercent": 28, "windowDurationMins": 300, "resetsAt": 1791455400 },
        "secondary": { "usedPercent": 14, "windowDurationMins": 10080, "resetsAt": 1792038600 }
    } }, "rateLimitResetCredits": { "availableCount": 0, "credits": [] } }
    """);
    Equal(2, snapshot.Windows?.Count, "双窗口数量");
    Equal("5 小时", snapshot.Windows?[0].Label, "短期窗口标签");
    Equal(72, snapshot.Windows?[0].RemainingPercent, "短期剩余百分比");
    Equal(1791455400L, snapshot.Windows?[0].ResetsAt?.ToUnixTimeSeconds(), "短期独立重置时间");
    Equal("本周", snapshot.Windows?[1].Label, "周窗口标签");
    Equal(86, snapshot.Windows?[1].RemainingPercent, "周剩余百分比");
    Equal(1792038600L, snapshot.Windows?[1].ResetsAt?.ToUnixTimeSeconds(), "周独立重置时间");
}

static void ParseProWeeklyPrimary()
{
    var snapshot = Parse("""
    { "rateLimits": { "primary": { "usedPercent": 8, "windowDurationMins": 10080,
        "resetsAt": 1792038613 }, "secondary": null } }
    """);
    Equal(1, snapshot.Windows?.Count, "仅一个窗口");
    Equal("本周", snapshot.Windows?[0].Label, "primary 可以是周窗口");
    Equal(92, snapshot.Windows?[0].RemainingPercent, "真实 Pro 结构");
}

static void SwitchWindowPresentation()
{
    var viewModel = new NotchViewModel();
    viewModel.ApplySnapshot(CodexUsageNotch.MainWindow.CreatePlusDemoSnapshot());
    True(viewModel.HasMultipleWindows, "双窗口可见");
    Equal("5h 99%/本周 80%", viewModel.DisplayRemainingText, "合并两项剩余额度");
    Equal("10月15日 12:30 重置", viewModel.DisplayResetText, "保留周额度重置时间段");
    Equal(80, viewModel.OuterRingRemainingPercent, "外环对应周额度");
    Equal(99, viewModel.InnerRingRemainingPercent, "内环对应 5 小时额度");
    Equal("完整重置 ×3", viewModel.DisplayCreditText, "保留完整重置段");
    True(viewModel.PrimaryTooltip.Contains("18:30", StringComparison.Ordinal), "悬停显示独立重置时间");
    viewModel.IsCompact = true;
    Equal("5h 99%/本周 80%", viewModel.DisplayRemainingText, "窄窗口保留两项文案");
    Equal("10/15 12:30", viewModel.DisplayResetText, "重置时间沿用原有紧凑格式");
    viewModel.ApplySnapshot(new UsageSnapshot(92, null, 3, [], [new UsageWindow(92, null, 10080)]));
    True(!viewModel.HasMultipleWindows, "恢复单窗口");
    Equal(1, viewModel.QuotaRows.Count, "移除旧详情行");
    Equal("92%", viewModel.DisplayRemainingText, "恢复原有单窗口文案");
}

static UsageSnapshot Parse(string json)
{
    using var document = JsonDocument.Parse(json);
    return UsageResponseParser.Parse(document.RootElement);
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{label}：期望 {expected}，实际 {actual}。");
    }
}

static void True(bool condition, string label)
{
    if (!condition)
    {
        throw new InvalidOperationException($"{label} 未通过。");
    }
}
