﻿using log4net;
using PropertyChanged;
using UnchainedLauncher.Core.API.A2S;
using UnchainedLauncher.Core.API.ServerBrowser;

namespace UnchainedLauncher.Core.API {
    /// <summary>
    /// Forwards polled A2S information to a backend registration. If the registration dies,
    /// then a new one will be created automatically and as needed.
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class A2SBoundRegistration : IDisposable {
        // handles creating new registrations
        private readonly PersistentServerRegistrationFactory _registrationFactory;
        public C2ServerInfo ServerInfo => _registrationFactory.ServerInfo;
        private static readonly ILog Logger = LogManager.GetLogger(nameof(A2SBoundRegistration));
        public PersistentServerRegistration? Registration { get; private set; }
        public A2SWatcher A2SWatcher { get; private set; }
        private bool _disposedValue;

        public A2SBoundRegistration(IServerBrowser browser,
                                    IA2S a2SEndpoint,
                                    C2ServerInfo serverInfo,
                                    string localIp,
                                    int heartbeatSecondsBeforeTimeout = 5,
                                    int updateIntervalMillis = 1000) {
            this._registrationFactory = new(browser, serverInfo, heartbeatSecondsBeforeTimeout, localIp);
            // this should be constructed last, because it will start polling immediately
            // and could otherwise trigger stuff before this object is fully constructed!
            this.A2SWatcher = new(a2SEndpoint, this.OnA2SPoll, updateIntervalMillis);
        }

        /// <summary>
        /// Drop the current registration. The registration may be re-created automatically.
        /// </summary>
        public void DropRegistration() {
            this.Registration?.Dispose();
        }

        /// <summary>
        /// Attempt to register with the backend using the given A2sInfo.
        /// If a previous registration exists, this will drop it before making a new one.
        /// </summary>
        /// <param name="info">The A2s info to send as the initial server state</param>
        /// <returns></returns>
        public async Task TryRegister(A2SInfo info) {
            this.DropRegistration();
            Logger.Info($"Server '{this.ServerInfo.Name}' is attempting to register with the backend");
            try {
                this.Registration = await this._registrationFactory.MakeRegistration(info, this.OnRegistrationDeath);
            }
            catch (Exception reason) {
                Logger.Error($"Server '{this.ServerInfo.Name}' failed to register with backend: {reason.Message}");
            }
        }

        /// <summary>
        /// Delegate to execute whenever new A2S information is available from the server
        /// </summary>
        /// <param name="info">The new A2S information</param>
        /// <returns></returns>
        private async Task OnA2SPoll(A2SInfo info) {
            Logger.Debug($"Server '{this.ServerInfo.Name}' just got A2s info.");
            // if the registration is null or dead, make a new registration with this info
            if (this.Registration is null || this.Registration.IsDead) {
                await this.TryRegister(info);
                return;
            }

            try {
                await this.Registration.UpdateRegistrationA2S(info);
            }
            catch (Exception ex) {
                // if something went wrong, try re-making the registration
                Logger.Error($"Server '{this.ServerInfo.Name}' failed to update registration and will drop it: {ex.Message}");
                this.DropRegistration();
            }
        }

        private Task OnRegistrationDeath(Exception _) {
            // drop it and let the next A2S poll try to re-create it
            this.DropRegistration();
            return Task.CompletedTask;
        }

        protected virtual void Dispose(bool disposing) {
            if (!_disposedValue) {
                if (disposing) {
                    // A2SWatcher must dispose before Registration.
                    // Otherwise, it might make a new Registration
                    // before it is Disposed
                    this.A2SWatcher.Dispose();
                    this.Registration?.Dispose();
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