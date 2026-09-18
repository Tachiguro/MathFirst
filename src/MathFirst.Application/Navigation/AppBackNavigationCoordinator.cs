namespace MathFirst.Application.Navigation;

/// <summary>
/// Thread-safe coordinator for application-owned back navigation handling.
/// </summary>
public sealed class AppBackNavigationCoordinator : IAppBackNavigationCoordinator
{
    private readonly object _syncRoot = new();
    private readonly List<Func<bool>> _handlers = [];

    public int RegisteredHandlerCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _handlers.Count;
            }
        }
    }

    public IDisposable RegisterHandler(Func<bool> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_syncRoot)
        {
            _handlers.Add(handler);
        }

        return new BackHandlerRegistration(this, handler);
    }

    public bool TryHandleBack()
    {
        Func<bool>[] snapshot;
        lock (_syncRoot)
        {
            if (_handlers.Count == 0)
            {
                return false;
            }

            snapshot = [.. _handlers];
        }

        // Evaluate handlers in reverse order of registration (most recent / topmost first).
        for (var i = snapshot.Length - 1; i >= 0; i--)
        {
            var handler = snapshot[i];
            try
            {
                if (handler())
                {
                    return true;
                }
            }
            catch
            {
                // If a handler throws, fail safely to continue evaluating remaining handlers
            }
        }

        return false;
    }

    private void UnregisterHandler(Func<bool> handler)
    {
        lock (_syncRoot)
        {
            _handlers.Remove(handler);
        }
    }

    private sealed class BackHandlerRegistration : IDisposable
    {
        private AppBackNavigationCoordinator? _coordinator;
        private Func<bool>? _handler;

        public BackHandlerRegistration(AppBackNavigationCoordinator coordinator, Func<bool> handler)
        {
            _coordinator = coordinator;
            _handler = handler;
        }

        public void Dispose()
        {
            var coordinator = Interlocked.Exchange(ref _coordinator, null);
            var handler = Interlocked.Exchange(ref _handler, null);

            if (coordinator is not null && handler is not null)
            {
                coordinator.UnregisterHandler(handler);
            }
        }
    }
}
