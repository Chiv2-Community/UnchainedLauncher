using System.Net;
using UnchainedLauncher.Core.Services.Server.A2S;

namespace UnchainedLauncher.Core.Tests.Integration.API {
    // Should have a chivalry/A2S server running on 127.0.0.1:7071 for these to
    // test against
    public class A2STests {
        private static readonly IPEndPoint Endpoint = IPEndPoint.Parse("127.0.0.1:7071");

        [Fact]
        public async Task InfoAsyncTest() {
            A2S a2SEndpoint = new(Endpoint);
            var info = await a2SEndpoint.InfoAsync();

            Assert.NotNull(info);
            Assert.Equal("Chivalry 2", info.Game);
        }
    }
}