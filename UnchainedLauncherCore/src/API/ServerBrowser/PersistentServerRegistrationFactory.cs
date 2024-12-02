﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnchainedLauncher.Core.API;

namespace UnchainedLauncher.Core.API
{
    /// <summary>
    /// Allows the quick and easy creation of persistent server registration objects.
    /// </summary>
    public class PersistentServerRegistrationFactory : IDisposable
    {
        public readonly C2ServerInfo ServerInfo;
        public readonly int HeartBeatSecondsBeforeTimeout;
        public readonly IServerBrowser Browser;
        public readonly string LocalIp;
        private bool disposedValue;

        public PersistentServerRegistrationFactory(IServerBrowser Browser,
                                                   C2ServerInfo ServerInfo,
                                                   int HeartBeatSecondsBeforeTimeout,
                                                   string LocalIp) {
            this.Browser = Browser;
            this.ServerInfo = ServerInfo;
            this.HeartBeatSecondsBeforeTimeout = HeartBeatSecondsBeforeTimeout;
            this.LocalIp = LocalIp;
        }

        /// <summary>
        /// Make a new PersistentServerRegistration using the factory's parameters
        /// </summary>
        /// <param name="info">The initial A2S information</param>
        /// <param name="OnDeath">The callback for if the PersistentServerRegistration dies</param>
        /// <returns></returns>
        public async Task<PersistentServerRegistration> MakeRegistration(A2sInfo info,
                                                                         PersistentServerRegistration.RegistrationDied? OnDeath = null)
        {
            ServerInfo specificInfo = new ServerInfo(this.ServerInfo, info);
            var response = await this.Browser.RegisterServerAsync(this.LocalIp, specificInfo);
            return new(this.Browser, response, OnDeath, this.HeartBeatSecondsBeforeTimeout);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: should this dispose the browser?
                    this.Browser.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
