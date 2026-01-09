// TEMP DISABLED: legacy tests reference removed ServerBrowser API. Replace with ServerRegistrationService tests.
#if FALSE
using UnchainedLauncher.Core.API.ServerBrowser;
using UnchainedLauncher.Core.Services.A2S;
using Environment = UnchainedLauncher.Core.Services.A2S.Environment;

namespace UnchainedLauncher.Core.Tests.Integration.API {

    // Should have an instance of the backend running on localhost:8080 for this
    // to test against
    public class ServerBrowserTests {
        private static readonly C2ServerInfo TestServerC2Info = new() {
            Name = "Test server",
            Description = "Test description"
        };
        //test a2s response to use where it would be useless/inconvenient to actually make such a query
        private static readonly A2SInfo TestA2SInfo =
            new(0, "", "test map", "", "Chivalry 2", 0, 10, 100, 5, ServerType.NonDedicated, Environment.Windows, true, false);

        private static readonly ServerInfo TestServerInfo = new(TestServerC2Info, TestA2SInfo);

        private static readonly Uri Endpoint = new("http://localhost:8080/api/v1");
        private static readonly String LocalIp = "127.0.0.1";

        [Fact]
        public async Task RegisterServerTest() {
            using ServerBrowser backend = new(Endpoint, new HttpClient());
            var res = await backend.RegisterServerAsync(LocalIp, TestServerInfo); ;
            //not exhaustive
            //it's not possible to up-cast and compare using equals
            //because the result will still retain the fields and cause a not-equal
            //also, the server modifies certain fields so they will never be equal
            //Server does "Unverified" stuff so this won't be true
            //Assert.AreEqual(res.server.name, testServerInfo.name); 
            Assert.Equal(TestServerInfo.Description, res.Server.Description);
            Assert.Equal(TestServerInfo.Ports, res.Server.Ports);
            Assert.Equal(TestServerInfo.CurrentMap, res.Server.CurrentMap);
        }

        [Fact]
        public async Task UpdateServerTest() {
            using ServerBrowser backend = new(Endpoint, new HttpClient());
            var (_, key, server) = await backend.RegisterServerAsync(LocalIp, TestServerInfo);
            await Task.Delay(1000); //wait a bit before updating
            var refreshBefore2 = await backend.UpdateServerAsync(server, key);
            var now = DateTimeOffset.Now.ToUnixTimeSeconds();
            Assert.True(refreshBefore2 > now);
        }

        [Fact]
        public async Task HeartbeatTest() {
            using ServerBrowser backend = new(Endpoint, new HttpClient());
            var (_, key, server) = await backend.RegisterServerAsync(LocalIp, TestServerInfo);
            Thread.Sleep(1000); //give it time to sit before sending a heartbeat
            var refreshBefore2 = await backend.HeartbeatAsync(server, key);
            var now = DateTimeOffset.Now.ToUnixTimeSeconds();

            Assert.True(refreshBefore2 > now);
        }

        [Fact]
        public async Task DeleteServerTest() {
            using ServerBrowser backend = new(Endpoint, new HttpClient());
            var (_, key, server) = await backend.RegisterServerAsync(LocalIp, TestServerInfo);
            await backend.DeleteServerAsync(server, key);
        }
    }
}
#endif