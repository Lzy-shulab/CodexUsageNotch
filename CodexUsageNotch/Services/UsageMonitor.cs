using CodexUsageNotch.Models;

namespace CodexUsageNotch.Services;

internal sealed class UsageMonitor : IAsyncDisposable
{
    private readonly CodexAppServerClient _client = new();
    private readonly SemaphoreSlim _detailLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _pollingTask;
    private UsageSnapshot? _latestSnapshot;

    public event Action<UsageSnapshot>? SnapshotUpdated;
    public event Action<string>? ConnectionUnavailable;

    public UsageSnapshot? LatestSnapshot => _latestSnapshot;

    public void Start()
    {
        _pollingTask ??= PollAsync(_lifetime.Token);
    }

    public async Task RefreshDetailsAsync(CancellationToken cancellationToken = default)
    {
        await _detailLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var response = await _client.ReadRateLimitsAsync(
                excludeResetCreditDetails: false,
                cancellationToken).ConfigureAwait(false);
            Publish(UsageResponseParser.Parse(response));
        }
        finally
        {
            _detailLock.Release();
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        var includeDetails = true;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var response = await _client.ReadRateLimitsAsync(
                    excludeResetCreditDetails: !includeDetails,
                    cancellationToken).ConfigureAwait(false);
                var snapshot = UsageResponseParser.Parse(response);
                var countChanged = _latestSnapshot is not null &&
                                   snapshot.ResetCreditCount != _latestSnapshot.ResetCreditCount;
                snapshot = PreserveKnownDetails(snapshot);
                Publish(snapshot);

                if (!includeDetails && countChanged)
                {
                    await RefreshDetailsAsync(cancellationToken).ConfigureAwait(false);
                }

                includeDetails = false;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                DiagnosticLog.Write(exception);
                ConnectionUnavailable?.Invoke("额度服务暂时不可用，正在自动重连");
                includeDetails = true;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private UsageSnapshot PreserveKnownDetails(UsageSnapshot snapshot)
    {
        if (snapshot.ResetCredits is not null ||
            _latestSnapshot?.ResetCredits is null ||
            snapshot.ResetCreditCount != _latestSnapshot.ResetCreditCount)
        {
            return snapshot;
        }

        return snapshot with { ResetCredits = _latestSnapshot.ResetCredits };
    }

    private void Publish(UsageSnapshot snapshot)
    {
        _latestSnapshot = snapshot;
        SnapshotUpdated?.Invoke(snapshot);
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (_pollingTask is not null)
        {
            try { await _pollingTask.ConfigureAwait(false); } catch { }
        }

        await _client.DisposeAsync().ConfigureAwait(false);
        _detailLock.Dispose();
        _lifetime.Dispose();
    }
}
