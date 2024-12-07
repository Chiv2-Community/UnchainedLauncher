using UnchainedLauncher.Core.API.A2S;
using UnchainedLauncher.Core.API.ServerBrowser;
using Environment = UnchainedLauncher.Core.API.A2S.Environment;

namespace UnchainedLauncher.Core.Tests.Integration.API {

    // Should have an instance of the backend running on localhost:8080 for this
    // to test against
    public class ServerBrowserTests {
        private static readonly C2ServerInfo testServerC2Info = new() {
            Name = "Test server",
            Description = "Test description"
        };
        //test a2s response to use where it would be useless/inconvenient to actually make such a query
        private static readonly A2sInfo testA2SInfo =
            new(0, "", "test map", "", "Chivalry 2", 0, 10, 100, 5, ServerType.NONDEDICATED, Environment.WINDOWS, true, false);

        private static readonly ServerInfo testServerInfo = new(testServerC2Info, testA2SInfo);

        private static readonly Uri endpoint = new("http://localhost:8080/api/v1");
        private static readonly String localIP = "127.0.0.1";

        [Fact]
        public async Task RegisterServerTest() {
            using ServerBrowser backend = new(endpoint, new HttpClient());
            RegisterServerResponse res = await backend.RegisterServerAsync(localIP, testServerInfo); ;
            //not exhaustive
            //it's not possible to up-cast and compare using equals
            //because the result will still retain the fields and cause a not-equal
            //also, the server modifies certain fields so they will never be equal
            //Server does "Unverified" stuff so this won't be true
            //Assert.AreEqual(res.server.name, testServerInfo.name); 
            Assert.Equal(testServerInfo.Description, res.Server.Description);
            Assert.Equal(testServerInfo.Ports, res.Server.Ports);
            Assert.Equal(testServerInfo.CurrentMap, res.Server.CurrentMap);
        }

        [Fact]
        public async Task UpdateServerTest() {
            using ServerBrowser backend = new(endpoint, new HttpClient());
            var (_, key, server) = await backend.RegisterServerAsync(localIP, testServerInfo);
            await Task.Delay(1000); //wait a bit before updating
            double refreshBefore2 = await backend.UpdateServerAsync(server, key);
            long now = DateTimeOffset.Now.ToUnixTimeSeconds();
            Assert.True(refreshBefore2 > now);
        }

        [Fact]
        public async Task HeartbeatTest() {
            using ServerBrowser backend = new(endpoint, new HttpClient());
            var (_, key, server) = await backend.RegisterServerAsync(localIP, testServerInfo);
            Thread.Sleep(1000); //give it time to sit before sending a heartbeat
            double refreshBefore2 = await backend.HeartbeatAsync(server, key);
            long now = DateTimeOffset.Now.ToUnixTimeSeconds();

            Assert.True(refreshBefore2 > now);
        }

        [Fact]
        public async Task DeleteServerTest() {
            using ServerBrowser backend = new(endpoint, new HttpClient());
            var (_, key, server) = await backend.RegisterServerAsync(localIP, testServerInfo);
            await backend.DeleteServerAsync(server, key);
        }
    }
}