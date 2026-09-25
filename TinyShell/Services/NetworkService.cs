using System.Net.NetworkInformation;

namespace TinyShell.Services;

public static class NetworkService
{
    public static bool IsOnline => NetworkInterface.GetIsNetworkAvailable();

    public static bool HasWifi => NetworkInterface.GetAllNetworkInterfaces().Any(n =>
        n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
        n.OperationalStatus == OperationalStatus.Up);
}
