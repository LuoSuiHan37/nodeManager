namespace NoteManager.Helpers;

/// <summary>
/// 简单防抖：在指定延迟后执行异步操作。
/// </summary>
public sealed class DebounceHelper : IDisposable
{
    private readonly int _delayMs;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public DebounceHelper(int delayMs)
    {
        _delayMs = Math.Clamp(delayMs, 200, 5000);
    }

    public void Debounce(Func<CancellationToken, Task> action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_delayMs, token);
                if (!token.IsCancellationRequested)
                {
                    await action(token);
                }
            }
            catch (OperationCanceledException)
            {
                // ignored
            }
        }, CancellationToken.None);
    }

    public void Cancel()
    {
        _cts?.Cancel();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
