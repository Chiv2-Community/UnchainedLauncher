using Newtonsoft.Json.Linq;
using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnchainedLauncher.Core.API;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using LanguageExt.Pipes;

namespace UnchainedLauncher.Core.API
{

    public static class TaskExtensions{
        public static Task WhenCanceled(this CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
#pragma warning disable //There are no nulls that can emerge here
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
#pragma warning restore
            return tcs.Task;
        }

    }

    //TODO? Implement IObservableProperty here for GUI bindings
    public class RegisteredServer : IDisposable
    {
        // this thread object is used as the mutex
        private readonly Thread registrationThread;
        private readonly CancellationTokenSource shutDownSource;
        private bool disposed;
        public readonly C2ServerInfo serverInfo;
        public readonly int updateIntervalMillis;
        public readonly Uri backend;
        public readonly IPEndPoint a2sLocation;
        public readonly string localIp;
        //thread-safe accessors
        // see also https://stackoverflow.com/a/541348
        // not used to avoid locking on this
        private ResponseServer? _registeredServer;
        public ResponseServer? registeredServer
        {
            get { lock (registrationThread) { return _registeredServer; } }
            private set { lock (registrationThread) { _registeredServer = value; } }
        }
        private Exception? _lastException;
        public Exception? lastException
        {
            get { lock (registrationThread) { return _lastException; } }
            private set { lock (registrationThread) { _lastException = value; } }
        }
        private bool _isA2SOk = false;
        public bool isA2SOk
        {
            get { lock (registrationThread) { return _isA2SOk; } }
            private set { lock (registrationThread) { _isA2SOk = value; } }
        }

        public RegisteredServer(Uri backend,
            C2ServerInfo serverInfo, string localIp,
            int updateIntervalMillis = 1000)
        {
            this.backend = backend;
            this.serverInfo = serverInfo;
            this.updateIntervalMillis = updateIntervalMillis;
            this.localIp = localIp;
            this.a2sLocation = new(IPAddress.Parse("127.0.0.1"), serverInfo.ports.a2s);
            shutDownSource = new();

            registrationThread = new(
                    () => Run(shutDownSource.Token)
                    )
            {
                IsBackground = true
            };
            registrationThread.Start();
        }

        private async Task maintainRegistration(CancellationToken token)
        {
            A2S_INFO a2sRes = await getServerState(token);
            var res = await ServerBrowser.registerServerAsync(backend, localIp, new(serverInfo, a2sRes));
            double refreshBefore = res.refreshBefore;
            string key = res.key;
            registeredServer = res.server;
            int heartBeatAfterSeconds = (int)(refreshBefore - DateTimeOffset.Now.ToUnixTimeSeconds() - 5);
            Task heartBeatDelay = Task.Delay(1000*heartBeatAfterSeconds);
            Task updateDelay = Task.Delay(updateIntervalMillis);
            while (true)
            {
                var fin = await Task.WhenAny(heartBeatDelay, updateDelay, token.WhenCanceled());
                token.ThrowIfCancellationRequested();
                if(fin == heartBeatDelay)
                {
                    refreshBefore = await ServerBrowser.heartbeatAsync(backend, registeredServer, key);
                    heartBeatAfterSeconds = (int)(refreshBefore - DateTimeOffset.Now.ToUnixTimeSeconds() - 5);
                    heartBeatDelay = Task.Delay(1000 * heartBeatAfterSeconds);
                }
                else if(fin == updateDelay)
                {
                    if(registeredServer.update(await getServerState(token)))
                    {
                        refreshBefore = await ServerBrowser.updateServerAsync(backend, registeredServer, key);
                    }
                    updateDelay = Task.Delay(updateIntervalMillis);
                }
            }
        }
        private async void Run(CancellationToken token)
        {
            while (true)
            {
                try
                {
                    await maintainRegistration(token);
                }
                catch (HttpRequestException e) //if something goes wrong and the registration dies
                {
                    if(e.StatusCode != HttpStatusCode.NotFound)
                    {
                        break;
                    }
                }
                catch (OperationCanceledException) //propagate cancellation
                {
                    break;
                }
                
            }
        }

        private async Task<A2S_INFO> getServerState(CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var res = await A2S.infoAsync(a2sLocation);
                    isA2SOk = true;
                    return res;
                }
                catch (Exception e)
                {
                    isA2SOk = false;
                    lastException = e;
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                // Signal the thread to stop and wait for it to finish
                shutDownSource.Cancel();
                if (registrationThread != null && registrationThread.IsAlive)
                {
                    registrationThread.Join(); // Optional: You might want to add a timeout here
                }

                // Dispose managed resources
                shutDownSource.Dispose();
            }

            // Clean up unmanaged resources (if any) here

            disposed = true;
        }

        ~RegisteredServer()
        {
            Dispose(false);
        }
    }
}
