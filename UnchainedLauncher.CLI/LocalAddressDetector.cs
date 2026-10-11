using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace UnchainedLauncher.CLI;

public static class LocalAddressDetector {
    public static IPAddress Detect() {
        try {
            var addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up
                    && adapter.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .SelectMany(adapter => {
                    var properties = adapter.GetIPProperties();
                    var hasGateway = properties.GatewayAddresses.Any(g => !g.Address.Equals(IPAddress.Any)
                        && g.Address.AddressFamily == AddressFamily.InterNetwork);
                    return properties.UnicastAddresses.Select(ip => (ip.Address, HasGateway: hasGateway));
                });
            return Select(addresses);
        }
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException or SocketException) {
            return IPAddress.Loopback;
        }
    }

    internal static IPAddress Select(IEnumerable<(IPAddress Address, bool HasGateway)> candidates) =>
        candidates.Where(candidate => candidate.Address.AddressFamily == AddressFamily.InterNetwork
                && !candidate.Address.Equals(IPAddress.Any) && !IPAddress.IsLoopback(candidate.Address)
                && !(candidate.Address.GetAddressBytes()[0] == 169 && candidate.Address.GetAddressBytes()[1] == 254))
            .OrderByDescending(candidate => candidate.HasGateway)
            .Select(candidate => candidate.Address).FirstOrDefault() ?? IPAddress.Loopback;
}
