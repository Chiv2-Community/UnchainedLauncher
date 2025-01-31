using UnchainedLauncher.Core.API;
using UnchainedLauncher.Core.API.A2S;
using UnchainedLauncher.Core.API.ServerBrowser;
using UnchainedLauncher.Core.Tests.Unit.API.Mocks;

namespace UnchainedLauncher.Core.Tests.Unit.API {
    using Environment = Core.API.A2S.Environment;

    public class RunningC2ServerTests {
        public static readonly C2ServerInfo TestServerC2Info = new() {
            Name = "Test server",
            Description = "Test description"
        };

        [Fact]
        public async Task BackendMaintenanceTest() {
            // fake series of A2S responses to invoke some update calls
            MockA2S mockA2S = new(
                new A2SInfo[]{
                    new(0, "", "test map", "", "Chivalry 2", 0, 10, 100, 5, ServerType.NonDedicated, Environment.Windows, true, false),
                    new(0, "", "test map", "", "Chivalry 2", 0, 5, 100, 5, ServerType.NonDedicated, Environment.Windows, true, false),
                    new(0, "", "some other map", "", "Chivalry 2", 0, 5, 100, 5, ServerType.NonDedicated, Environment.Windows, true, false),
                    new(0, "", "some other map", "", "Chivalry 2", 0, 7, 100, 5, ServerType.NonDedicated, Environment.Windows, true, false),
                }
            );

            var heartbeatSeconds = 8;
            var heartbeatSecondsBeforeTimeout = 2;
            var testDurationSeconds = 30;
            var a2SUpdateInterval = 500;

            // we just want to make sure things are in the right ballpark here.
            // Small timing variations that we can't control and don't actually
            // matter can throw exact counts off a little
            var minA2SProbes = testDurationSeconds * 1000 / a2SUpdateInterval - 1;
            var maxA2SProbes = minA2SProbes + 10;
            // say only 1 out of every 5 probes causes an update to be pushed
            var (minUpdatesSent, maxUpdatesSent) = (minA2SProbes / 5, maxA2SProbes + 10);
            var minHeartbeats = testDurationSeconds / (heartbeatSeconds - heartbeatSecondsBeforeTimeout) - 1;
            var maxHeartbeats = minHeartbeats + 10;

            MockServerBrowser mockSb = new(heartbeatSeconds);


            using (var server = new A2SBoundRegistration(mockSb,
                                                    mockA2S,
                                                    TestServerC2Info,
                                                    "127.0.0.1",
                                                    heartbeatSecondsBeforeTimeout,
                                                    a2SUpdateInterval)) {
                await Task.Delay(testDurationSeconds * 1000 + 500);
                Assert.NotNull(server.Registration);
            }

            Assert.Equal(1, mockSb.NumServerRegisters);
            Assert.Equal(1, mockSb.NumServerDeletes);
            Assert.True(mockSb.NumServerHeartbeats >= minHeartbeats && mockSb.NumServerHeartbeats <= maxHeartbeats);
            Assert.True(mockA2S.NumRequests >= minA2SProbes && mockA2S.NumRequests <= maxA2SProbes);
            Assert.True(mockSb.NumServerUpdates >= minUpdatesSent && mockSb.NumServerUpdates <= maxUpdatesSent);

            // make sure it stops probing A2s
            var savedProbesCount = mockA2S.NumRequests;
            await Task.Delay(2000);
            Assert.Equal(savedProbesCount, mockA2S.NumRequests);
        }
    }
}