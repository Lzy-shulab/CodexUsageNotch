using System.Text.Json;

namespace CodexUsageNotch.Models;

public sealed record ResetCredit(string Id, DateTimeOffset? ExpiresAt);

public sealed record UsageWindow(int? RemainingPercent, DateTimeOffset? ResetsAt, int? DurationMinutes)
{
    public string Label => DurationMinutes switch
    {
        10080 => "本周",
        300 => "5 小时",
        >= 1440 when DurationMinutes % 1440 == 0 => $"{DurationMinutes / 1440} 天",
        >= 60 when DurationMinutes % 60 == 0 => $"{DurationMinutes / 60} 小时",
        > 0 => $"{DurationMinutes} 分钟",
        _ => "额度"
    };

    public string CompactLabel => DurationMinutes switch
    {
        10080 => "周",
        >= 1440 when DurationMinutes % 1440 == 0 => $"{DurationMinutes / 1440}d",
        >= 60 when DurationMinutes % 60 == 0 => $"{DurationMinutes / 60}h",
        > 0 => $"{DurationMinutes}m",
        _ => "额度"
    };
}

public sealed record UsageSnapshot(
    int? RemainingPercent,
    DateTimeOffset? ResetsAt,
    int ResetCreditCount,
    IReadOnlyList<ResetCredit>? ResetCredits,
    IReadOnlyList<UsageWindow>? Windows = null);

public static class UsageResponseParser
{
    public static UsageSnapshot Parse(JsonElement payload)
    {
        var root = payload;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("result", out var result))
        {
            root = result;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("额度响应不是 JSON 对象。");
        }

        var limits = SelectRateLimits(root);
        var windows = ParseWindows(limits);
        var displayWindow = windows.LastOrDefault();

        var availableCount = 0;
        IReadOnlyList<ResetCredit>? credits = null;
        if (root.TryGetProperty("rateLimitResetCredits", out var creditsSummary) &&
            creditsSummary.ValueKind == JsonValueKind.Object)
        {
            if (creditsSummary.TryGetProperty("availableCount", out var countValue) &&
                countValue.TryGetInt32(out var count))
            {
                availableCount = Math.Max(0, count);
            }

            if (creditsSummary.TryGetProperty("credits", out var creditRows) &&
                creditRows.ValueKind == JsonValueKind.Array)
            {
                credits = ParseCredits(creditRows);
            }
        }

        return new UsageSnapshot(displayWindow?.RemainingPercent, displayWindow?.ResetsAt,
            availableCount, credits, windows);
    }

    private static JsonElement SelectRateLimits(JsonElement root)
    {
        if (root.TryGetProperty("rateLimitsByLimitId", out var byId) && byId.ValueKind == JsonValueKind.Object)
        {
            if (byId.TryGetProperty("codex", out var codex) && codex.ValueKind == JsonValueKind.Object)
            {
                return codex;
            }

            foreach (var property in byId.EnumerateObject())
            {
                if (property.Name.Contains("codex", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.Object)
                {
                    return property.Value;
                }
            }

            foreach (var property in byId.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    return property.Value;
                }
            }
        }

        return root.TryGetProperty("rateLimits", out var legacy) && legacy.ValueKind == JsonValueKind.Object
            ? legacy
            : default;
    }

    private static IReadOnlyList<UsageWindow> ParseWindows(JsonElement limits)
    {
        var windows = new List<UsageWindow>();
        if (limits.ValueKind != JsonValueKind.Object)
        {
            return windows;
        }

        foreach (var name in new[] { "primary", "secondary" })
        {
            if (!limits.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            int? remaining = window.TryGetProperty("usedPercent", out var used) &&
                             used.ValueKind == JsonValueKind.Number && used.TryGetDouble(out var percent)
                ? (int)Math.Round(Math.Clamp(100 - percent, 0, 100), MidpointRounding.AwayFromZero)
                : null;
            DateTimeOffset? resetsAt = window.TryGetProperty("resetsAt", out var reset) &&
                                      reset.ValueKind == JsonValueKind.Number && reset.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
            int? duration = window.TryGetProperty("windowDurationMins", out var minutes) &&
                            minutes.ValueKind == JsonValueKind.Number && minutes.TryGetInt32(out var value)
                ? value : null;
            windows.Add(new UsageWindow(remaining, resetsAt, duration));
        }

        return windows.OrderBy(window => window.DurationMinutes ?? int.MaxValue).ToArray();
    }

    private static IReadOnlyList<ResetCredit> ParseCredits(JsonElement creditRows)
    {
        var credits = new List<ResetCredit>();
        foreach (var row in creditRows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (row.TryGetProperty("status", out var status) &&
                status.ValueKind == JsonValueKind.String &&
                !string.Equals(status.GetString(), "available", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var id = row.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.String
                ? idValue.GetString() ?? string.Empty
                : string.Empty;

            DateTimeOffset? expiresAt = null;
            if (row.TryGetProperty("expiresAt", out var expiresValue) &&
                expiresValue.ValueKind == JsonValueKind.Number &&
                expiresValue.TryGetInt64(out var expiresSeconds))
            {
                expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresSeconds);
            }

            credits.Add(new ResetCredit(id, expiresAt));
        }

        return credits
            .OrderBy(credit => credit.ExpiresAt ?? DateTimeOffset.MaxValue)
            .ToArray();
    }
}

public static class UsageTextFormatter
{
    public static string FormatReset(DateTimeOffset? timestamp) => timestamp is null
        ? "重置时间未知"
        : $"{timestamp.Value.ToLocalTime():M月d日 HH:mm} 重置";

    public static string FormatExpiration(DateTimeOffset? timestamp) => timestamp is null
        ? "到期时间未提供"
        : $"{timestamp.Value.ToLocalTime():yyyy-MM-dd HH:mm} 到期";
}
