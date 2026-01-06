using System.Net;
using System.Reflection;
using FluentAssertions;
using Moq;
using Unchained.ServerBrowser.Api;
using Unchained.ServerBrowser.Model;
using UnchainedLauncher.Core.Services.Server;
using UnchainedLauncher.Core.Services.Server.A2S;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Server;

public class ServerRegistrationServiceTests {

    private static void SetPrivateAutoProperty<T>(object instance, string propertyName, T value) {
        var field = instance.GetType().GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"Expected auto-property backing field for {propertyName}");
        field!.SetValue(instance, value);
    }

    private static T GetPrivateField<T>(object instance, string fieldName) {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"Expected private field {fieldName}");
        return (T)field!.GetValue(instance)!;
    }

    private static async Task InvokePrivateAsync(object instance, string methodName, params object[] args) {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull($"Expected private method {methodName}");
        var result = method!.Invoke(instance, args);
        (result is Task).Should().BeTrue($"Expected {methodName} to return Task");
        await (Task)result!;
    }

    private static ServerRegistrationOptions CreateOptions() => new() {
        Name = "TestServer",
        Description = "Test",
        Ports = new Chivalry2PortsOptions { Game = 7777, A2S = 27015, Ping = 27016 }
    };

    private static RegistrationResponse CreateRegistrationResponse(string uniqueId, string key) {
        var ports = new Chivalry2Ports(7777, 27015, 27016);
        var server = new ServerResponse(
            uniqueId,
            ports,
            passwordProtected: false,
            isVerified: false,
            name: "TestServer",
            description: "Test",
            currentMap: "Map1",
            playerCount: 1,
            maxPlayers: 64,
            mods: new List<Mod>(),
            lastHeartbeat: 0
        );

        return new RegistrationResponse(key, refreshBefore: 0, server);
    }

    [Fact]
    public async Task TryRegisterAsync_ShouldSeedLastSentSnapshot_FromRegistrationA2SInfo() {
        var api = new Mock<IDefaultApi>(MockBehavior.Strict);
        var postResponse = new Mock<IApiV1ServersPostApiResponse>(MockBehavior.Strict);
        postResponse.Setup(r => r.Created()).Returns(CreateRegistrationResponse(uniqueId: "uid", key: "key"));
        api.Setup(a => a.ApiV1ServersPostAsync(It.IsAny<ServerRegistrationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(postResponse.Object);

        var svc = new ServerRegistrationService(api.Object);
        SetPrivateAutoProperty(svc, nameof(ServerRegistrationService.Options), CreateOptions());
        SetPrivateAutoProperty(svc, nameof(ServerRegistrationService.LocalIp), IPAddress.Loopback);

        var a2s = new A2SInfo { Players = 1, MaxPlayers = 64, Map = "Map1", Name = "TestServer" };
        await InvokePrivateAsync(svc, "TryRegisterAsync", a2s, CancellationToken.None);

        svc.UniqueId.Should().Be("uid");
        svc.RegistrationKey.Should().Be("key");

        GetPrivateField<int?>(svc, "_lastSentPlayers").Should().Be(1);
        GetPrivateField<int?>(svc, "_lastSentMaxPlayers").Should().Be(64);
        GetPrivateField<string?>(svc, "_lastSentMap").Should().Be("Map1");
    }

    [Fact]
    public async Task UpdateAsync_ShouldNotCallPut_WhenNothingChanged() {
        var api = new Mock<IDefaultApi>(MockBehavior.Strict);

        var svc = new ServerRegistrationService(api.Object) {
            UniqueId = "uid",
            RegistrationKey = "key"
        };

        // Pre-seed last-sent snapshot
        var lastPlayers = 1;
        var lastMax = 64;
        var lastMap = "Map1";
        typeof(ServerRegistrationService).GetField("_lastSentPlayers", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(svc, lastPlayers);
        typeof(ServerRegistrationService).GetField("_lastSentMaxPlayers", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(svc, lastMax);
        typeof(ServerRegistrationService).GetField("_lastSentMap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(svc, lastMap);

        var a2s = new A2SInfo { Players = lastPlayers, MaxPlayers = lastMax, Map = lastMap, Name = "TestServer" };
        await InvokePrivateAsync(svc, "UpdateAsync", a2s, CancellationToken.None);

        api.Verify(a => a.ApiV1ServersUniqueIdPutAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ServerUpdateRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_ShouldCallPut_AndUpdateSnapshot_WhenPlayerCountChanged() {
        var api = new Mock<IDefaultApi>(MockBehavior.Strict);
        var putResponse = new Mock<IApiV1ServersUniqueIdPutApiResponse>(MockBehavior.Strict);
        api.Setup(a => a.ApiV1ServersUniqueIdPutAsync("key", "uid", It.IsAny<ServerUpdateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(putResponse.Object);

        var svc = new ServerRegistrationService(api.Object) {
            UniqueId = "uid",
            RegistrationKey = "key"
        };

        typeof(ServerRegistrationService).GetField("_lastSentPlayers", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(svc, 1);
        typeof(ServerRegistrationService).GetField("_lastSentMaxPlayers", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(svc, 64);
        typeof(ServerRegistrationService).GetField("_lastSentMap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(svc, "Map1");

        var a2s = new A2SInfo { Players = 2, MaxPlayers = 64, Map = "Map1", Name = "TestServer" };
        await InvokePrivateAsync(svc, "UpdateAsync", a2s, CancellationToken.None);

        api.Verify(a => a.ApiV1ServersUniqueIdPutAsync("key", "uid", It.IsAny<ServerUpdateRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        GetPrivateField<int?>(svc, "_lastSentPlayers").Should().Be(2);
    }
}
