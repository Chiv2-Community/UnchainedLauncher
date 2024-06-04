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
    // make sure there is a Chivalry 2 server running
    // RCON doesn't support sending anything back right now, so this can't return anything
    // use wireshark and watch the game to make sure it's working
    // Make sure the rcon blueprint is active. It will produce lots of console spam in-game if it is.
    [TestClass()]
    public class RCONTests
    {
        private static readonly IPEndPoint endpoint = IPEndPoint.Parse("127.0.0.1:9001");
        [TestMethod()]
        public async Task sendCommandTest()
        {
            await RCON.SendCommandTo(endpoint, "tbsaddstagetime 10");
        }
    }
}