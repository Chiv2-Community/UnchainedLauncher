using UnchainedLauncher.Core.API.A2S;

namespace UnchainedLauncher.Core.API.ServerBrowser {
    /// <summary>
    /// Allows the quick and easy creation of persistent server registration objects.
    /// </summary>
    public class PersistentServerRegistrationFactory : IDisposable {
        public readonly C2ServerInfo ServerInfo;
        public readonly int HeartBeatSecondsBeforeTimeout;
        public readonly IServerBrowser Browser;
        public readonly string LocalIp;
        private bool _disposedValue;

        public PersistentServerRegistrationFactory(IServerBrowser browser,
                                                   C2ServerInfo serverInfo,
                                                   int heartBeatSecondsBeforeTimeout,
                                                   string localIp) {
            this.Browser = browser;
            this.ServerInfo = serverInfo;
            this.HeartBeatSecondsBeforeTimeout = heartBeatSecondsBeforeTimeout;
            this.LocalIp = localIp;
        }

        /// <summary>
        /// Make a new PersistentServerRegistration using the factory's parameters
        /// </summary>
        /// <param name="info">The initial A2S information</param>
        /// <param name="onDeath">The callback for if the PersistentServerRegistration dies</param>
        /// <returns></returns>
        public async Task<PersistentServerRegistration> MakeRegistration(A2SInfo info,
                                                                         PersistentServerRegistration.RegistrationDied? onDeath = null) {
            var specificInfo = new ServerInfo(ServerInfo, info);
            var response = await Browser.RegisterServerAsync(LocalIp, specificInfo);
            return new(Browser, response, onDeath, HeartBeatSecondsBeforeTimeout);
        }

        protected virtual void Dispose(bool disposing) {
            if (!_disposedValue) {
                if (disposing) {
                    // TODO: should this dispose the browser?
                    Browser.Dispose();
                }

                _disposedValue = true;
            }
        }

        public void Dispose() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}