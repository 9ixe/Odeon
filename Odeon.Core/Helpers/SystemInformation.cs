using Windows.System.Profile;

namespace Odeon.Core.Helpers;

public static class SystemInformation
{
    public static readonly string DeviceFamily = AnalyticsInfo.VersionInfo.DeviceFamily;

    public static bool IsDesktop => DeviceFamily == "Windows.Desktop";

    public static bool IsXbox => DeviceFamily == "Windows.Xbox";
}
