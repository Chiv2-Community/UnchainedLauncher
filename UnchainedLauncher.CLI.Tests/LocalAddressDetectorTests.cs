using System.Net;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public class LocalAddressDetectorTests {
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