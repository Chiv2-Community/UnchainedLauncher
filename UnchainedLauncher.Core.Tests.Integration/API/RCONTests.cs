using System.Net;
using UnchainedLauncher.Core.API;

namespace UnchainedLauncher.Core.Tests.Integration.API {
    // make sure there is a Chivalry 2 server running
    // RCON doesn't support sending anything back right now, so this can't return anything
    // use wireshark and watch the game to make sure it's working
    // Make sure the rcon blueprint is active. It will produce lots of console spam in-game if it is.
    public class RCONTests
    {
        private static readonly IPEndPoint endpoint = IPEndPoint.Parse("127.0.0.1:9001");
        [Fact]
        public async Task SendCommandTest()
        {
            await RCON.SendCommandTo(endpoint, "tbsaddstagetime 10");
        }
    }
}