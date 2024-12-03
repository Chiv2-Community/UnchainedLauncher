using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnchainedLauncher.Core.API;
using Environment = UnchainedLauncher.Core.API.Environment;
using UnchainedLauncher.Core.Tests.Unit.API.Mocks;

namespace UnchainedLauncher.Core.Tests.Unit.API
{
    public class A2SWatcherTests
    {
        [Fact]
        public async Task TestWatcher()
        {
            MockA2S mockA2s = new(
                new A2sInfo[]{
                    new(0, "", "test map", "", "Chivalry 2", 0, 10, 100, 5, ServerType.NONDEDICATED, Environment.WINDOWS, true, false),
                    new(0, "", "test map", "", "Chivalry 2", 0, 5, 100, 5, ServerType.NONDEDICATED, Environment.WINDOWS, true, false),
                    new(0, "", "some other map", "", "Chivalry 2", 0, 5, 100, 5, ServerType.NONDEDICATED, Environment.WINDOWS, true, false),
                    new(0, "", "some other map", "", "Chivalry 2", 0, 7, 100, 5, ServerType.NONDEDICATED, Environment.WINDOWS, true, false),
                }
            );

            int receivedCount = 0;
            A2SWatcher.OnA2SReceived onReceivedAction = a2s => {
                receivedCount++;
                return Task.CompletedTask;
            };

            using (var watcher = new A2SWatcher(mockA2s, onReceivedAction, 500))
            {
                // should be enough time for 8 queries
                await Task.Delay(4000);
            };

            Assert.Equal(mockA2s.NumRequests, receivedCount);
            Assert.Equal(8, receivedCount);
        }
    }
}
