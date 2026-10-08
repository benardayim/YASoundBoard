using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace YasoundBoard.Server;

public sealed record LanAddress(string Interface, string Address);

public static class NetworkInfo
{
    public static IReadOnlyList<LanAddress> LocalAddresses()
    {
        var found = new List<LanAddress>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                var address = unicast.Address.ToString();
                if (address.StartsWith("169.254.", StringComparison.Ordinal))
                {
                    continue;
                }

                found.Add(new LanAddress(nic.Name, address));
            }
        }

        return found;
    }

    public static bool IsLikelyReachable(string address)
    {
        if (IPAddress.TryParse(address, out var parsed) && IPAddress.IsLoopback(parsed))
        {
            return false;
        }

        if (address.StartsWith("192.168.", StringComparison.Ordinal) ||
            address.StartsWith("10.", StringComparison.Ordinal))
        {
            return true;
        }

        if (!address.StartsWith("172.", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = address.Split('.');
        return parts.Length > 1
            && int.TryParse(parts[1], out var second)
            && second is >= 16 and <= 31;
    }
}
