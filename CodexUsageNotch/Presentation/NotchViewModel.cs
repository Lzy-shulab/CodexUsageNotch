using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CodexUsageNotch.Models;

namespace CodexUsageNotch.Presentation;

public sealed class NotchViewModel : INotifyPropertyChanged
{
    private int _remainingPercent;
    private string _remainingText = "读取中…";
    private string _resetText = "正在读取额度";
    private string _creditText = "完整重置";
    private string _creditBadgeText = "可用 0 次";
    private string _statusHint = "正在连接 Codex";
    private int _creditCount;
    private bool _isCompact;
    private string _compactResetText = "读取中…";
    private UsageWindow? _firstWindow;
    private UsageWindow? _secondWindow;
    private bool _showQuotaDetails;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ResetCreditRowViewModel> CreditRows { get; } = [];
    public ObservableCollection<UsageWindowRowViewModel> QuotaRows { get; } = [];
    public bool HasMultipleWindows => _secondWindow is not null;
    public int OuterRingRemainingPercent => HasMultipleWindows ? _secondWindow?.RemainingPercent ?? 0 : RemainingPercent;
    public int InnerRingRemainingPercent => HasMultipleWindows ? _firstWindow?.RemainingPercent ?? 0 : 0;
    public string PrimaryTooltip => HasMultipleWindows
        ? $"{WindowTooltip(_firstWindow)}\n\n{WindowTooltip(_secondWindow)}" : WindowTooltip(_firstWindow);
    public bool ShowQuotaDetails
    {
        get => _showQuotaDetails;
        internal set
        {
            SetField(ref _showQuotaDetails, value);
            NotifyDisplayText();
        }
    }
    public string DetailsTitle => ShowQuotaDetails ? "额度详情" : "完整重置";
    public string DetailsBadge => ShowQuotaDetails ? "剩余额度" : CreditBadgeText;

    public bool IsCompact
    {
        get => _isCompact;
        internal set
        {
            if (_isCompact == value) return;
            SetField(ref _isCompact, value);
            NotifyDisplayText();
        }
    }

    public double CapsuleDesignWidth => HasMultipleWindows ? IsCompact ? 344 : 448 : IsCompact ? 260 : 392;
    public string DisplayRemainingText => HasMultipleWindows
        ? $"{WindowSummary(_firstWindow!, compact: true)}/{WindowSummary(_secondWindow!, compact: false)}"
        : IsCompact && RemainingText.StartsWith("剩余 ", StringComparison.Ordinal) ? RemainingText[3..] : RemainingText;
    public string DisplayResetText => IsCompact ? _compactResetText : ResetText;
    public string DisplayCreditText => IsCompact
        ? CreditText.Contains('×') ? $"×{CreditCount}" : string.Empty
        : CreditText;

    public int RemainingPercent
    {
        get => _remainingPercent;
        private set => SetField(ref _remainingPercent, value);
    }

    public string RemainingText
    {
        get => _remainingText;
        private set => SetField(ref _remainingText, value);
    }

    public string ResetText
    {
        get => _resetText;
        private set => SetField(ref _resetText, value);
    }

    public string CreditText
    {
        get => _creditText;
        private set => SetField(ref _creditText, value);
    }

    public string CreditBadgeText
    {
        get => _creditBadgeText;
        private set => SetField(ref _creditBadgeText, value);
    }

    public string StatusHint
    {
        get => _statusHint;
        private set => SetField(ref _statusHint, value);
    }

    public int CreditCount
    {
        get => _creditCount;
        private set => SetField(ref _creditCount, value);
    }

    public void ApplySnapshot(UsageSnapshot snapshot)
    {
        var windows = snapshot.Windows ?? [new UsageWindow(snapshot.RemainingPercent, snapshot.ResetsAt, 10080)];
        _firstWindow = windows.FirstOrDefault();
        _secondWindow = windows.Skip(1).FirstOrDefault();
        RemainingPercent = _firstWindow?.RemainingPercent ?? 0;
        RemainingText = _firstWindow?.RemainingPercent is { } remaining ? $"剩余 {remaining}%" : "额度未知";
        var resetWindow = _secondWindow ?? _firstWindow;
        ResetText = UsageTextFormatter.FormatReset(resetWindow?.ResetsAt);
        _compactResetText = resetWindow?.ResetsAt?.ToLocalTime().ToString("MM/dd HH:mm") ?? "时间未知";
        QuotaRows.Clear();
        foreach (var window in windows)
        {
            QuotaRows.Add(new UsageWindowRowViewModel(window.Label,
                window.RemainingPercent is { } percent ? $"{percent}%" : "未知",
                window.RemainingPercent ?? 0, UsageTextFormatter.FormatReset(window.ResetsAt)));
        }
        CreditCount = snapshot.ResetCreditCount;
        CreditText = $"完整重置 ×{snapshot.ResetCreditCount}";
        CreditBadgeText = $"可用 {snapshot.ResetCreditCount} 次";
        StatusHint = "只读额度信息，每 10 秒刷新";
        NotifyDisplayText();

        if (snapshot.ResetCredits is not null)
        {
            ReplaceCreditRows(snapshot);
        }
    }

    public void SetUnavailable(string message)
    {
        _firstWindow = null;
        _secondWindow = null;
        RemainingPercent = 0;
        QuotaRows.Clear();
        RemainingText = "暂不可用";
        ResetText = "等待重新连接";
        _compactResetText = "等待连接";
        CreditText = "完整重置";
        StatusHint = message;
        NotifyDisplayText();
    }

    public void SetLoadingCreditDetails()
    {
        CreditRows.Clear();
        for (var index = 0; index < CreditCount; index++)
        {
            CreditRows.Add(new ResetCreditRowViewModel($"第 {index + 1} 次", "正在读取到期时间…", false));
        }
        if (CreditCount == 0)
        {
            CreditRows.Add(new ResetCreditRowViewModel(string.Empty, "正在读取到期时间…", true));
        }
    }

    public void SetStatus(string message)
    {
        StatusHint = message;
    }

    private void ReplaceCreditRows(UsageSnapshot snapshot)
    {
        CreditRows.Clear();
        var knownCredits = snapshot.ResetCredits ?? [];
        for (var index = 0; index < snapshot.ResetCreditCount; index++)
        {
            var expiration = index < knownCredits.Count
                ? UsageTextFormatter.FormatExpiration(knownCredits[index].ExpiresAt)
                : "到期时间未提供";
            CreditRows.Add(new ResetCreditRowViewModel($"第 {index + 1} 次", expiration, false));
        }

        if (snapshot.ResetCreditCount == 0)
        {
            CreditRows.Add(new ResetCreditRowViewModel(string.Empty, "当前没有可用的完整重置", true));
        }
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void NotifyDisplayText()
    {
        foreach (var property in new[] { nameof(CapsuleDesignWidth), nameof(DisplayRemainingText),
                     nameof(DisplayResetText), nameof(DisplayCreditText), nameof(HasMultipleWindows),
                     nameof(OuterRingRemainingPercent), nameof(InnerRingRemainingPercent),
                     nameof(PrimaryTooltip),
                     nameof(DetailsTitle), nameof(DetailsBadge) })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
    }

    private static string WindowSummary(UsageWindow window, bool compact) =>
        $"{(compact ? window.CompactLabel : window.Label)} {window.RemainingPercent?.ToString() ?? "—"}%";

    private static string WindowTooltip(UsageWindow? window) => window is null ? "正在读取额度" :
        $"{window.Label} · {(window.RemainingPercent is { } percent ? $"剩余 {percent}%" : "额度未知")}\n{UsageTextFormatter.FormatReset(window.ResetsAt)}\n点击查看额度详情";
}

public sealed record ResetCreditRowViewModel(string Label, string Expiration, bool IsMessage);
public sealed record UsageWindowRowViewModel(string Label, string RemainingText, int RemainingPercent, string ResetText);
