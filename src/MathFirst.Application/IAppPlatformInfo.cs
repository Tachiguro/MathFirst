namespace MathFirst.Application;

public interface IAppPlatformInfo
{
    string PlatformName { get; }

    string PlatformVersion { get; }

    string PlatformDescription => string.IsNullOrWhiteSpace(PlatformVersion)
        ? (string.IsNullOrWhiteSpace(PlatformName) ? "Unknown" : PlatformName.Trim())
        : (string.IsNullOrWhiteSpace(PlatformName) ? PlatformVersion.Trim() : $"{PlatformName.Trim()} {PlatformVersion.Trim()}");
}
