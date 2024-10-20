using System.Net;

namespace UnchainedLauncher.Core.API.Tests
{
    // Should have a chivalry/A2S server running on 127.0.0.1:7071 for these to
    // test against
    public class A2STests
    {
        private static readonly IPEndPoint endpoint = IPEndPoint.Parse("127.0.0.1:7071");

        [Fact]
        public async Task InfoAsyncTest()
        {
            A2S A2sEndpoint = new(endpoint);
            var info = await A2sEndpoint.InfoAsync();

            Assert.NotNull(info);
            Assert.Equal("Chivalry 2", info.Game);
        }
    }
}