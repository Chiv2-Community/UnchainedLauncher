using UnchainedLauncher.Core.API.A2S;
using UnchainedLauncher.Core.Tests.Unit.API.Mocks;
using Environment = UnchainedLauncher.Core.API.A2S.Environment;

namespace UnchainedLauncher.Core.Tests.Unit.API {
    public class A2SWatcherTests {
        [Fact]
        public async Task TestWatcher() {
            MockA2S mockA2S = new(
                new A2SInfo[]{
                    new(0, "", "test map", "", "Chivalry 2", 0, 10, 100, 5, ServerType.NonDedicated, Environment.Windows, true, false),
                    new(0, "", "test map", "", "Chivalry 2", 0, 5, 100, 5, ServerType.NonDedicated, Environment.Windows, true, false),
                    new(0, "", "some other map", "", "Chivalry 2", 0, 5, 100, 5, ServerType.NonDedicated, Environment.Windows, true, false),
                    new(0, "", "some other map", "", "Chivalry 2", 0, 7, 100, 5, ServerType.NonDedicated, Environment.Windows, true, false),
                }
            );

            var receivedCount = 0;
            A2SWatcher.OnA2SReceived onReceivedAction = a2S => {
                receivedCount++;
                return Task.CompletedTask;
            };

            using (var watcher = new A2SWatcher(mockA2S, onReceivedAction, 500)) {
                // should be enough time for 8 queries
                await Task.Delay(4000);
            }
            ;

            Assert.Equal(mockA2S.NumRequests, receivedCount);
            Assert.Equal(8, receivedCount);
        }
    }
}