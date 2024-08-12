using Microsoft.VisualStudio.TestTools.UnitTesting;
using UnchainedLauncher.Core.API;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using Microsoft.VisualBasic;

namespace UnchainedLauncher.Core.API.Tests
{
    // You must have a backend instance running for these tests to succeed
    [TestClass()]
    public class RegisteredServerTests
    {
        private static readonly Uri backend = new("http://localhost:8080/api/v1");
        private static readonly C2ServerInfo testServerC2Info = new()
        {
            Name = "Test server",
            Description = "Test description"
        };
        //you might want to watch this test case run using wireshark
        [TestMethod()]
        public async Task UnitTest()
        {
            // fake series of A2S responses to invoke some update calls
            MockA2S mockA2s = new(
                new A2sInfo[]{
                    new(0, "", "test map", "", "Chivalry 2", 0, 10, 100, 5, ServerType.NONDEDICATED, Environment.WINDOWS, true, false),
                    new(0, "", "test map", "", "Chivalry 2", 0, 5, 100, 5, ServerType.NONDEDICATED, Environment.WINDOWS, true, false),
                    new(0, "", "some other map", "", "Chivalry 2", 0, 5, 100, 5, ServerType.NONDEDICATED, Environment.WINDOWS, true, false),
                    new(0, "", "some other map", "", "Chivalry 2", 0, 7, 100, 5, ServerType.NONDEDICATED, Environment.WINDOWS, true, false),
                }
            );
            MockServerBrowser mockSB = new(4);
            int updateInterval = 500;
            int testDuration = 10000;
            using (RegisteredServer l = new(backend, testServerC2Info, "127.0.0.1", updateInterval, mockSB, mockA2s)){
                await Task.Delay(testDuration); //give time for heartbeats to happen
                var reg = l.RemoteInfo;
                Assert.IsNotNull(reg);
                Assert.AreNotEqual(reg.UniqueId, "");
            }

            // server is shut down and cleaned up by this point
            Assert.AreNotEqual(mockA2s.NumRequests, 0, "A2S was not queried");
            Assert.AreEqual(mockSB.NumServerRegisters, 1, "Server was registered more or less times than expected");
            Assert.IsTrue(mockSB.NumServerUpdates <= mockA2s.NumRequests, "More updates than A2S calls");
            Assert.AreEqual((decimal)testDuration/updateInterval, (decimal)mockA2s.NumRequests, (decimal)2.0, "not enough A2S queries");
            // it's possible and acceptable for the server to query a2s, but not
            // send an update because it was told to shut down.
            Assert.AreEqual((decimal)mockSB.NumServerUpdates, (decimal)mockA2s.NumRequests-1, (decimal)1.5, "Server missed an update");
            Assert.AreEqual(mockSB.NumServerDeletes, 1, "Server was not cleanly deleted from backend");
        }

        // requires a full setup; a real local backend server, and a real instance of chiv running
        [TestMethod()]
        public async Task IntegrationTest()
        {
            RegisteredServer l = new(backend, testServerC2Info, "127.0.0.1");
            await Task.Delay(70000); //give time for heartbeats to happen
            var reg = l.RemoteInfo;
            Assert.IsNotNull(reg);
            Assert.AreNotEqual(reg.UniqueId, "");
        }
    }
}