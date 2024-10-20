using UnchainedLauncher.Core.API.Mocks;

namespace UnchainedLauncher.Core.API.Tests
{
    public class RegisteredServerTests
    {
        private static readonly C2ServerInfo testServerC2Info = new()
        {
            Name = "Test server",
            Description = "Test description"
        };

        [Fact]
        public async Task UnitTest() {
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
            using (RegisteredServer l = new(mockSB, mockA2s, testServerC2Info, "127.0.0.1", updateInterval)) {
                await Task.Delay(testDuration); //give time for heartbeats to happen
                var reg = l.RemoteInfo;
                Assert.NotNull(reg);
                Assert.Equal("Server_ID_0", reg!.UniqueId);
            }

            // server is shut down and cleaned up by this point
            Assert.NotEqual(0, mockA2s.NumRequests);
            Assert.Equal(1, mockSB.NumServerRegisters);
            Assert.True(mockSB.NumServerUpdates <= mockA2s.NumRequests, "More updates than A2S calls");
            Assert.Equal((decimal)mockA2s.NumRequests, (decimal)testDuration / updateInterval);

            // it's possible and acceptable for the server to query a2s, but not
            // send an update because it was told to shut down.
            Assert.Equal((decimal)mockA2s.NumRequests - 1, (decimal)mockSB.NumServerUpdates);
            Assert.Equal(1, mockSB.NumServerDeletes);
        }
    }
}