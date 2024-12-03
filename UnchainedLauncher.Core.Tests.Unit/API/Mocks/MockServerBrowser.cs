using UnchainedLauncher.Core.API.ServerBrowser;

namespace UnchainedLauncher.Core.Tests.Unit.API.Mocks
{
    public class MockServerBrowser : IServerBrowser
    {
        protected int refreshBeforeSeconds;
        public int NumServerRegisters { get; private set; }
        public int NumServerUpdates { get; private set; }
        public int NumServerHeartbeats { get; private set; }
        public int NumServerDeletes { get; private set; }

        public string Host { get => "LocalTest"; }

        // TODO: allow throwing occasional 404 errors from
        // appropriate functions for more testing
        public MockServerBrowser(int refreshBeforeSeconds)
        {
            this.refreshBeforeSeconds = refreshBeforeSeconds;
        }

        private (long, long) GetTimes()
        {
            long now = DateTimeOffset.Now.ToUnixTimeSeconds();
            long rfBefore = now + refreshBeforeSeconds;
            return (now, rfBefore);
        }

        public Task<RegisterServerResponse> RegisterServerAsync(string localIp, ServerInfo info, CancellationToken? ct = null)
        {
            var (now, refreshBefore) = GetTimes();
            NumServerRegisters++;
            return Task.FromResult(
                new RegisterServerResponse(
                    refreshBefore,
                    "Some Arbitrary key",
                    new ResponseServer(
                        new UniqueServerInfo(
                            info.Name,
                            now
                        ),
                        localIp,
                        "127.0.0.1"
                    )
                )
            );
        }

        public Task<double> UpdateServerAsync(UniqueServerInfo info, string key, CancellationToken? ct = null)
        {
            NumServerUpdates++;
            var (_, refreshBefore) = GetTimes();
            return Task.FromResult((double)refreshBefore);
        }

        public Task<double> HeartbeatAsync(UniqueServerInfo info, string key, CancellationToken? ct = null)
        {
            NumServerHeartbeats++;
            var (_, refreshBefore) = GetTimes();
            return Task.FromResult((double)refreshBefore);
        }

        public Task DeleteServerAsync(UniqueServerInfo info, string key, CancellationToken? ct = null)
        {
            NumServerDeletes++;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
