using System.Net;
using System.Net.Sockets;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public class LocalAddressDetectorTests {
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    public async Task QueryUsesIpv4LoopbackForLocalAndWildcardAddresses(string address) {
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)listener.Client.LocalEndPoint!).Port;
        var options = Assert.IsType<ServerOptions>(CliLaunchOptions.Parse([
            "server", "--chivalry2-path", ".", "--platform", "Steam", "--local-ip", address, "--query-port", port.ToString()
        ]));
        var query = ServerLauncher.CreateQuery(options).InfoAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var request = await listener.ReceiveAsync(timeout.Token);
        Assert.Equal(AddressFamily.InterNetwork, request.RemoteEndPoint.AddressFamily);
        byte[] response = [255, 255, 255, 255, 73, 17,
            .. "Test\0Map\0Folder\0Game\0"u8.ToArray(), 0, 0, 1, 32, 0, (byte)'D', (byte)'W', 0, 1];
        await listener.SendAsync(response, request.RemoteEndPoint, timeout.Token);
        Assert.Equal("Test", (await query).Name);
    }

    [Fact]
    public void PrefersUsableDefaultGatewayAddressOverOtherInterfaces() {
        var candidates = new[] {
            (IPAddress.Parse("172.17.0.1"), false),
            (IPAddress.Parse("192.168.1.50"), true),
            (IPAddress.Loopback, true),
            (IPAddress.IPv6Loopback, true)
        };
        Assert.Equal(IPAddress.Parse("192.168.1.50"), LocalAddressDetector.Select(candidates));
    }

    [Fact]
    public void FallsBackToAnActiveIpv4OrLoopbackWithoutGateway() {
        Assert.Equal(IPAddress.Parse("10.0.0.2"), LocalAddressDetector.Select([(IPAddress.Parse("10.0.0.2"), false)]));
        Assert.Equal(IPAddress.Loopback, LocalAddressDetector.Select([
            (IPAddress.Any, true), (IPAddress.Parse("169.254.1.2"), true), (IPAddress.IPv6Any, true)
        ]));
        Assert.Equal(IPAddress.Loopback, LocalAddressDetector.Select([]));
    }

    [Fact]
    public void ExplicitOverrideIsNotReplacedAndClientDoesNotGetServerLocalIp() {
        var server = CliLaunchOptions.Parse(["server", "--chivalry2-path", ".", "--platform", "Steam", "--local-ip=192.168.50.10"]);
        Assert.Equal("192.168.50.10", Assert.IsType<ServerOptions>(server).Settings.LocalIp);
        Assert.Contains("--local-ip 192.168.50.10", Assert.IsType<ServerOptions>(server).ToServerLaunchOptions().ToCLIArgs().Select(arg => arg.Rendered));
        var client = CliLaunchOptions.Parse(["client", "--chivalry2-path", ".", "--platform", "Steam"]);
        Assert.IsType<ClientOptions>(client);
        Assert.True(client.ToLaunchOptions().ServerLaunchOptions.IsNone);
    }
}