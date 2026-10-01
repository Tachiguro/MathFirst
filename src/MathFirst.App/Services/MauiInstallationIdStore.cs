namespace MathFirst.App.Services;

using MathFirst.Application.Telemetry;
using Microsoft.Maui.Storage;

public sealed class MauiInstallationIdStore : IInstallationIdStore
{
    private const string InstallationIdKey = "mathfirst.telemetry.installation_id";

    public string? Get() => Preferences.Default.Get(InstallationIdKey, (string?)null);

    public void Set(string value) => Preferences.Default.Set(InstallationIdKey, value);

    public void Clear() => Preferences.Default.Remove(InstallationIdKey);
}
