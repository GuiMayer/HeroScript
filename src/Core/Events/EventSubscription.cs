namespace Core.Events;

/// <summary>
/// Representa uma subscription que pode ser cancelada via Dispose.
/// </summary>
internal class EventSubscription : IDisposable
{
    private readonly Action _unsubscribe;
    private bool _disposed;

    public EventSubscription(Action unsubscribe)
    {
        _unsubscribe = unsubscribe ?? throw new ArgumentNullException(nameof(unsubscribe));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _unsubscribe();
            _disposed = true;
        }
    }
}
