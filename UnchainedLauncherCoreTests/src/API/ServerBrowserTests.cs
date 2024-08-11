using Microsoft.VisualStudio.TestTools.UnitTesting;
using UnchainedLauncher.Core.API;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;

namespace UnchainedLauncher.Core.API.Tests
{
    // Should have an instance of the backend running on localhost:8080 for this
    // to test against
    [TestClass()]
    public class ServerBrowserTests
    {
        private static readonly C2ServerInfo testServerC2Info = new()
        {
            Name = "Test server",
            Description = "Test description"
        };
        //test a2s response to use where it would be useless/inconvenient to actually make such a query
        private static readonly A2sInfo testA2SInfo =
            new(0, "", "test map", "", "Chivalry 2", 0, 10, 100, 5, ServerType.NONDEDICATED, Environment.WINDOWS, true, false);

        private static readonly ServerInfo testServerInfo = new(testServerC2Info, testA2SInfo);

        private static readonly Uri endpoint = new("http://localhost:8080/api/v1");
        private static readonly String localIP = "127.0.0.1";

        [TestMethod()]
        public async Task RegisterServerTest()
        {
            ServerBrowser backend = new(endpoint);
            RegisterServerResponse res = await backend.RegisterServerAsync(localIP, testServerInfo); ;
            //not exhaustive
            //it's not possible to up-cast and compare using equals
            //because the result will still retain the fields and cause a not-equal
            //also, the server modifies certain fields so they will never be equal
            //Server does "Unverified" stuff so this won't be true
            //Assert.AreEqual(res.server.name, testServerInfo.name); 
            Assert.AreEqual(res.Server.Description, testServerInfo.Description);
            Assert.AreEqual(res.Server.Ports, testServerInfo.Ports);
            Assert.AreEqual(res.Server.CurrentMap, testServerInfo.CurrentMap);
        }

        [TestMethod()]
        public async Task UpdateServerTest()
        {
            ServerBrowser backend = new(endpoint);
            var (_, key, server) = await backend.RegisterServerAsync(localIP, testServerInfo);
            await Task.Delay(1000); //wait a bit before updating
            double refreshBefore2 = await backend.UpdateServerAsync(server);
            long now = DateTimeOffset.Now.ToUnixTimeSeconds();
            Assert.IsTrue(refreshBefore2 > now);
        }

        [TestMethod()]
        public async Task HeartbeatTest()
        {
            ServerBrowser backend = new(endpoint);
            var (_, key, server) = await backend.RegisterServerAsync(localIP, testServerInfo);
            Thread.Sleep(1000); //give it time to sit before sending a heartbeat
            double refreshBefore2 = await backend.HeartbeatAsync(server);
            long now = DateTimeOffset.Now.ToUnixTimeSeconds();
            Assert.IsTrue(refreshBefore2 > now);
        }

        [TestMethod()]
        public async Task DeleteServerTest()
        {
            ServerBrowser backend = new(endpoint);
            var (_, key, server) = await backend.RegisterServerAsync(localIP, testServerInfo);
            await backend.DeleteServerAsync(server);
        }
    }
}