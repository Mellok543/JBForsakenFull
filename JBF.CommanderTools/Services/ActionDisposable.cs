namespace JBF.CommanderTools.Services;

internal sealed class ActionDisposable : IDisposable
{
    private Action? _dispose;

    public ActionDisposable(Action dispose)
    {
        _dispose = dispose;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
