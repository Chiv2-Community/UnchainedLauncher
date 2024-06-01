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
    // You must have a backend instance AND a chivalry 2 server running for these tests to succeed
    [TestClass()]
    public class RegisteredServerTests
    {
        private static readonly IPEndPoint endpoint = IPEndPoint.Parse("127.0.0.1:7071");
        private static readonly Uri backend = new("http://localhost:8080/api/v1");
        private static readonly C2ServerInfo testServerC2Info = new()
        {
            name = "Test server",
            description = "Test description"
        };
        //you might want to watch this test case run using wireshark
        [TestMethod()]
        public async Task RegisterLoopTest()
        {
            using RegisteredServer l = new(backend, testServerC2Info, "127.0.0.1");
            await Task.Delay(70000); //give time for a heartbeat to occur
            var reg = l.registeredServer;
            Assert.IsNotNull(reg);
            Assert.AreNotEqual(reg.uniqueId, "");
        }
    }
}