using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace CodexUsageNotch.Services;

internal sealed class CodexAppServerClient : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Process? _process;
    private Task? _stdoutReader;
    private Task? _stderrReader;
    private long _requestId;
    private bool _initialized;

    public async Task<JsonElement> ReadRateLimitsAsync(bool excludeResetCreditDetails, CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SendRequestAsync(
                "account/rateLimits/read",
                new { excludeResetCreditDetails },
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await ResetProcessAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_initialized && _process is { HasExited: false })
        {
            return;
        }

        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized && _process is { HasExited: false })
            {
                return;
            }

            await StopProcessCoreAsync().ConfigureAwait(false);
            var executable = FindCodexExecutable();
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            startInfo.ArgumentList.Add("app-server");
            startInfo.ArgumentList.Add("--stdio");

            _process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 Codex 额度服务。");
            _stdoutReader = ReadStandardOutputAsync(_process, _lifetime.Token);
            _stderrReader = DrainStandardErrorAsync(_process, _lifetime.Token);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            await SendRequestAsync(
                "initialize",
                new
                {
                    clientInfo = new
                    {
                        name = "codex_usage_notch",
                        title = "Codex Usage Notch",
                        version = typeof(CodexAppServerClient).Assembly.GetName().Version?.ToString(3) ?? "1.1.1"
                    },
                    capabilities = new { experimentalApi = true }
                },
                timeout.Token).ConfigureAwait(false);
            await SendNotificationAsync("initialized", new { }, timeout.Token).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async Task<JsonElement> SendRequestAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        var process = _process;
        if (process is null || process.HasExited)
        {
            throw new InvalidOperationException("Codex 额度服务尚未运行。");
        }

        var id = Interlocked.Increment(ref _requestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException("无法登记额度读取请求。");
        }

        try
        {
            await WriteMessageAsync(new { id, method, @params = parameters }, process, cancellationToken)
                .ConfigureAwait(false);
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task SendNotificationAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        var process = _process;
        if (process is null || process.HasExited)
        {
            throw new InvalidOperationException("Codex 额度服务尚未运行。");
        }

        await WriteMessageAsync(new { method, @params = parameters }, process, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task WriteMessageAsync(object message, Process process, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadStandardOutputAsync(Process process, CancellationToken cancellationToken)
    {
        Exception? terminalError = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                JsonElement message;
                try
                {
                    using var document = JsonDocument.Parse(line);
                    message = document.RootElement.Clone();
                }
                catch (JsonException)
                {
                    continue;
                }

                if (!message.TryGetProperty("id", out var idValue) || !idValue.TryGetInt64(out var id))
                {
                    continue;
                }

                if (!_pending.TryRemove(id, out var completion))
                {
                    continue;
                }

                if (message.TryGetProperty("error", out var error))
                {
                    completion.TrySetException(new InvalidOperationException(ReadErrorMessage(error)));
                }
                else if (message.TryGetProperty("result", out var result))
                {
                    completion.TrySetResult(result.Clone());
                }
                else
                {
                    completion.TrySetException(new FormatException("Codex 额度服务返回了不完整的响应。"));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            terminalError = exception;
        }
        finally
        {
            var error = terminalError ?? new InvalidOperationException("Codex 额度服务已停止。");
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(error);
            }
        }
    }

    private static async Task DrainStandardErrorAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                // Tracing is drained so the child process cannot block on a full error stream.
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ResetProcessAsync()
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopProcessCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async Task StopProcessCoreAsync()
    {
        _initialized = false;
        var process = _process;
        _process = null;
        if (process is null)
        {
            return;
        }

        try
        {
            process.StandardInput.Close();
            if (!process.HasExited)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    private static string FindCodexExecutable()
    {
        var overridePath = Environment.GetEnvironmentVariable("CODEX_USAGE_NOTCH_CODEX_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return overridePath;
        }

        var binRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenAI",
            "Codex",
            "bin");

        if (Directory.Exists(binRoot))
        {
            var executable = Directory
                .EnumerateFiles(binRoot, "codex.exe", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
            if (executable is not null)
            {
                return executable.FullName;
            }
        }

        return "codex.exe";
    }

    private static string ReadErrorMessage(JsonElement error)
    {
        if (error.ValueKind == JsonValueKind.Object &&
            error.TryGetProperty("message", out var message) &&
            message.ValueKind == JsonValueKind.String)
        {
            return message.GetString() ?? "Codex 额度服务返回错误。";
        }

        return "Codex 额度服务返回错误。";
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopProcessCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
            _lifecycleLock.Dispose();
            _writeLock.Dispose();
            _lifetime.Dispose();
        }

        if (_stdoutReader is not null)
        {
            try { await _stdoutReader.ConfigureAwait(false); } catch { }
        }

        if (_stderrReader is not null)
        {
            try { await _stderrReader.ConfigureAwait(false); } catch { }
        }
    }
}
