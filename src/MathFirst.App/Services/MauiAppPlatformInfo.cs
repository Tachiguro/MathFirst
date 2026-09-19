namespace MathFirst.App.Services;

using MathFirst.Application;
using Microsoft.Maui.Devices;

public sealed class MauiAppPlatformInfo : IAppPlatformInfo
{
    public string PlatformName
    {
        get
        {
            try
            {
                var platform = DeviceInfo.Current.Platform;
                if (platform == DevicePlatform.WinUI)
                {
                    return "Windows";
                }

                if (platform == DevicePlatform.Android)
                {
                    return "Android";
                }

                return platform.ToString();
            }
            catch
            {
                return "Unknown";
            }
        }
    }

    public string PlatformVersion
    {
        get
        {
            try
            {
                return DeviceInfo.Current.VersionString;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
