using Microsoft.VisualStudio.TestTools.UnitTesting;
using UnchainedLauncher.Core.API;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;

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
        public async Task RegisterLoopTest()
        {
            // TODO: give this a custom series of responses and make sure the registered server behaves
            // accordingly
            MockA2S mockA2s = new();
            using RegisteredServer l = new(backend, testServerC2Info, "127.0.0.1", a2sEndpoint: mockA2s);
            await Task.Delay(70000); //give time for a heartbeat to occur
            var reg = l.RemoteInfo;
            Assert.IsNotNull(reg);
            Assert.AreNotEqual(reg.UniqueId, "");
            Assert.AreNotEqual(mockA2s.NumRequests, 0);
        }
    }
}