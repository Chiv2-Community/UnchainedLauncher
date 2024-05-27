using Microsoft.VisualStudio.TestTools.UnitTesting;
using UnchainedLauncher.Core.API.ServerBrowser;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;

namespace UnchainedLauncher.Core.API.ServerBrowser.Tests
{
    // Should have an instance of the backend running on localhost:8080 for this
    // to test against
    [TestClass()]
    public class ServerBrowserTests
    {
        private static readonly ServerInfo testServerInfo = new()
        {
            currentMap = "test map",
            description = "Description",
            maxPlayers = 100,
            mods = Array.Empty<Mod>(),
            name = "Name",
            passwordProtected = false,
            playerCount = 100,
            ports = new Ports(8888, 3071, 2048)
        };

        private static readonly Uri endpoint = new("http://localhost:8080/api/v1");
        private static readonly String localIP = "127.0.0.1";

        [TestMethod()]
        public void registerServerTest()
        {

            var resTask = ServerBrowser.registerServerAsync(endpoint, localIP, testServerInfo);
            resTask.Wait();
            RegisterServerResponse res = resTask.Result;
            //not exhaustive
            //it's not possible to up-cast and compare using equals
            //because the result will still retain the fields and cause a not-equal
            //also, the server modifies certain fields so they will never be equal
            //Server does "Unverified" stuff so this won't be true
            //Assert.AreEqual(res.server.name, testServerInfo.name); 
            Assert.AreEqual(res.server.description, testServerInfo.description);
            Assert.AreEqual(res.server.ports, testServerInfo.ports);
            Assert.AreEqual(res.server.currentMap, testServerInfo.currentMap);
        }

        [TestMethod()]
        public void updateServerTest()
        {
            var resTask = ServerBrowser.registerServerAsync(endpoint, localIP, testServerInfo);
            resTask.Wait();

            var (refreshBefore, key, server) = resTask.Result;
            var updateTask = ServerBrowser.updateServerAsync(endpoint, server, key);
            updateTask.Wait();
            UpdateServerResponse uResponse = updateTask.Result;

            Assert.AreEqual(uResponse.server.description, testServerInfo.description);
            Assert.AreEqual(uResponse.server.ports, testServerInfo.ports);
            Assert.AreEqual(uResponse.server.currentMap, testServerInfo.currentMap);
        }

        [TestMethod()]
        public void heartbeatTest()
        {
            var resTask = ServerBrowser.registerServerAsync(endpoint, localIP, testServerInfo);
            resTask.Wait();

            var (refreshBefore, key, server) = resTask.Result;
            Thread.Sleep(500); //give it time to sit before sending a heartbeat
            var updateTask = ServerBrowser.heartbeatAsync(endpoint, server, key);
            updateTask.Wait();
            UpdateServerResponse uResponse = updateTask.Result;

            Assert.AreEqual(uResponse.server.description, testServerInfo.description);
            Assert.AreEqual(uResponse.server.ports, testServerInfo.ports);
            Assert.AreEqual(uResponse.server.currentMap, testServerInfo.currentMap);
        }

        [TestMethod()]
        public void deleteServerTest()
        {
            var resTask = ServerBrowser.registerServerAsync(endpoint, localIP, testServerInfo);
            resTask.Wait();

            var (refreshBefore, key, server) = resTask.Result;
            var updateTask = ServerBrowser.deleteServerAsync(endpoint, server, key);
            updateTask.Wait();
            HttpResponseMessage dResponse = updateTask.Result;

            dResponse.EnsureSuccessStatusCode(); //this is an assert
        }
    }
}