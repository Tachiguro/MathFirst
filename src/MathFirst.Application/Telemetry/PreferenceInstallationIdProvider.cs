using System;

namespace MathFirst.Application.Telemetry;

public sealed class PreferenceInstallationIdProvider : IInstallationIdProvider
{
    private readonly IInstallationIdStore _store;

    public PreferenceInstallationIdProvider(IInstallationIdStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public string GetOrCreateInstallationId()
    {
        var existing = _store.Get();

        if (Guid.TryParseExact(existing, "D", out var parsed) && parsed != Guid.Empty)
        {
            return existing;
        }

        var value = Guid.NewGuid().ToString("D");
        _store.Set(value);
        return value;
    }

    public void ClearInstallationId()
    {
        _store.Clear();
    }
}
