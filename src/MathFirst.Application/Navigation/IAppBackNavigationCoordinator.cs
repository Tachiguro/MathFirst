namespace MathFirst.Application.Navigation;

/// <summary>
/// Coordinates in-app hardware and system Back button and gesture handling across application surfaces.
/// </summary>
public interface IAppBackNavigationCoordinator
{
    /// <summary>
    /// Registers a back action handler. Handlers are evaluated in reverse order of registration (LIFO).
    /// </summary>
    /// <param name="handler">A function returning <c>true</c> if the back action was handled, or <c>false</c> to pass through.</param>
    /// <returns>A disposable token that unregisters the handler.</returns>
    IDisposable RegisterHandler(Func<bool> handler);

    /// <summary>
    /// Attempts to handle a back navigation event through registered handlers.
    /// </summary>
    /// <returns><c>true</c> if a registered handler handled the event; otherwise, <c>false</c>.</returns>
    bool TryHandleBack();

    /// <summary>
    /// Gets the count of currently registered handlers.
    /// </summary>
    int RegisteredHandlerCount { get; }
}
