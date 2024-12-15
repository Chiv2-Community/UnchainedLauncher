using log4net;
using PropertyChanged;
using System.Net;
using UnchainedLauncher.Core.API.A2S;

namespace UnchainedLauncher.Core.API.ServerBrowser {
    /// <summary>
    /// Maintains a registration with a backend by regularly performing heartbeats
    /// <para>- Runs a delegate whenever the registration dies</para>
    /// <para>- Allows updates to be pushed through to the remote, but handling exceptions
    ///   from that is the responsibility of the caller</para>
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class PersistentServerRegistration : IDisposable {
        public readonly IServerBrowser browser;
        public readonly int HeartBeatSecondsBeforeTimeout;
        public delegate Task RegistrationDied(Exception reason);
        private readonly RegistrationDied OnRegistrationDied;
        public bool IsDead { get; private set; }
        private static readonly ILog logger = LogManager.GetLogger(nameof(PersistentServerRegistration));
        private readonly PeriodicRunner Runner;
        public Exception? LastException => Runner.LastException;
        private bool disposedValue;
        public RegisterServerResponse Registration { get; private set; }
        public PersistentServerRegistration(IServerBrowser browser,
                                            RegisterServerResponse registration,
                                            RegistrationDied? OnDeath = null,
                                            int heartBeatSecondsBeforeTimeout = 5) {
            this.browser = browser;
            IsDead = false;
            OnRegistrationDied = OnDeath ?? DefaultOnRegistrationDied;
            Registration = registration;
            HeartBeatSecondsBeforeTimeout = heartBeatSecondsBeforeTimeout;
            Runner = new PeriodicRunner(TryHeartBeat, OnException, CalcSleepDuration(registration.RefreshBefore));
        }

        private async Task<bool> OnException(Exception ex) {
            IsDead = true;
            // notify anyone who cares that this registration is dead
            await OnRegistrationDied(ex);
            return false;
        }

        private static Task DefaultOnRegistrationDied(Exception reason) {
            return Task.CompletedTask;
        }

        // TODO: I am pretty sure the double introduces some precision errors, so timespans
        // less than 1 or 2 go negative. This causes extremely fast heartbeats.
        private TimeSpan CalcSleepDuration(double refreshBefore) {
            return TimeSpan.FromSeconds(refreshBefore - DateTimeOffset.Now.ToUnixTimeSeconds() - HeartBeatSecondsBeforeTimeout);
        }

        public async Task UpdateRegistrationA2s(A2sInfo info) {
            var shouldPush = Registration.Server.Update(info);
            if (shouldPush) {
                await UpdateRegistration(Registration.Server);
            }
        }

        public async Task UpdateRegistration(UniqueServerInfo info) {
            if (disposedValue) {
                logger.Error($"Attemped to update disposed server '{Registration.Server.Name}'");
                throw new ObjectDisposedException("Attempted to update a disposed server");
            }

            try {
                await browser.UpdateServerAsync(info, Registration.Key);
            }
            catch (Exception ex) {
                logger.Error($"Server '${Registration.Server.Name}' failed to update: {ex.Message}");
                throw;
            }
        }

        protected async Task<TimeSpan> TryHeartBeat() {
            try {
                var refreshBefore = await browser.HeartbeatAsync(Registration.Server, Registration.Key);
                // update RefreshBefore for visibility
                Registration = Registration with { RefreshBefore = refreshBefore };
                return CalcSleepDuration(refreshBefore);
            }
            catch (HttpRequestException e) {
                if (e.StatusCode == HttpStatusCode.NotFound) {
                    logger.Error($"Server '{Registration.Server.Name}' got HTTP 404. Probably a missed heartbeat.", e);
                    throw;
                }
                else {
                    logger.Error($"Server '{Registration.Server.Name}' got status code {e.StatusCode}.", e);
                    // re-throw exception
                    throw;
                }
            }
            catch (TimeoutException e) {
                logger.Error($"Server '{Registration.Server.Name}' timed out.", e);
                throw;
            }
        }

        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                try {
                    IsDead = true;
                    Runner.Dispose();
                    browser.DeleteServerAsync(Registration.Server, Registration.Key).Wait();
                }
                catch (Exception e) {
                    logger.Error($"Failed to de-register server '{Registration.Server.Name}' from backend: {e.Message}");
                }

                disposedValue = true;
            }
        }

        public void Dispose() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}