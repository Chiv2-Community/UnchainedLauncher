using NuGet.Frameworks;
using UnchainedLauncher.Core.API.A2S;
using UnchainedLauncher.Core.API.ServerBrowser;
using UnchainedLauncher.Core.Tests.Unit.API.Mocks;

namespace UnchainedLauncher.Core.Tests.Unit.API {
    using Environment = Core.API.A2S.Environment;

    public class PersistentServerRegistrationTests {
        public static readonly C2ServerInfo testServerC2Info = new() {
            Name = "Test server",
            Description = "Test description"
        };

        [Fact]
        public async Task PersistentServerRegistrationTest() {
            // fake series of A2S responses to invoke some update calls
            // if applied in order, these infos should cause updates ONLY at indexes 2, 3 and 4
            // additionally, because our mock is lacking, index 0 will also cause an update.
            // (the response sent back from the back has mostly absent server info)
            // NOTE: IF YOU CHANGE THIS, YOU MUST FIGURE OUT THE ABOVE COMMENT AND CHANGE CHECKS ACCORDINGLY
            var mockA2s = new A2sInfo[] {
                    new(0, "", "test map", "", "Chivalry 2", 0, 10, 100, 5, ServerType.NONDEDICATED, Environment.WINDOWS, true, false),
                    new(0, "", "test map", "", "Chivalry 2", 0, 10, 100, 5, ServerType.NONDEDICATED, Environment.WINDOWS, true, false),
                    new(0, "", "test map", "", "Chivalry 2", 0, 5, 100, 5, ServerType.NONDEDICATED, Environment.WINDOWS, true, false),
                    new(0, "", "some other map", "", "Chivalry 2", 0, 5, 100, 5, ServerType.NONDEDICATED, Environment.WINDOWS, true, false),
                    new(0, "", "some other map", "", "Chivalry 2", 0, 7, 100, 5, ServerType.NONDEDICATED, Environment.WINDOWS, true, false),
                };

            int heartbeatSeconds = 10;
            int heartbeatBeforeSeconds = 5;
            int testDuration = 24;
            int expectedHeartbeatCount = testDuration / (heartbeatSeconds - heartbeatBeforeSeconds);

            MockServerBrowser mockSB = new(heartbeatSeconds);

            ServerInfo serverInfo = new(testServerC2Info, mockA2s[0]);
            var registration = await mockSB.RegisterServerAsync("127.0.0.1", serverInfo);
            bool hasDied = false;
            Task OnDeath(Exception ex) {
                hasDied = true;
                return Task.CompletedTask;
            }

            using (var server = new PersistentServerRegistration(mockSB, registration, OnDeath)) {
                await Task.Delay(testDuration * 1000);
                // make sure it's doing heartbeats properly
                Assert.Equal(expectedHeartbeatCount, mockSB.NumServerHeartbeats);
                foreach (var info in mockA2s) {
                    await server.UpdateRegistrationA2s(info);
                }
                // see comment above mockA2s to see where this 4 comes from
                Assert.Equal(4, mockSB.NumServerUpdates);
            }

            // server is shut down and cleaned up by this point
            // make sure it's cleaning up after itself
            Assert.Equal(1, mockSB.NumServerDeletes);
            Assert.Equal(1, mockSB.NumServerRegisters);
            Assert.False(hasDied);
        }
    }
}