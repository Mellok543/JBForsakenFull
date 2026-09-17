namespace JBF.LR.Services;

internal sealed class ActionDisposable : IDisposable
{
    private readonly Action _onDispose;
    private bool _disposed;

    public ActionDisposable(Action onDispose)
    {
        _onDispose = onDispose;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _onDispose();
    }
}
