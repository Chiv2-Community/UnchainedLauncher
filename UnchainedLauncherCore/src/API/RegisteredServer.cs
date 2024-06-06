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
using PropertyChanged;

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
    [AddINotifyPropertyChangedInterface]
    public class RegisteredServer : IDisposable
    {
        // this thread object is used as the mutex
        private readonly Thread RegistrationThread;
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
        private ResponseServer? _RemoteInfo;
        public ResponseServer? RemoteInfo
        {
            get { lock (RegistrationThread) { return _RemoteInfo; } }
            private set { lock (RegistrationThread) { _RemoteInfo = value; } }
        }
        private Exception? _LastException;
        public Exception? LastException
        {
            get { lock (RegistrationThread) { return _LastException; } }
            private set { lock (RegistrationThread) { _LastException = value; } }
        }
        private bool _IsA2SOk = false;
        public bool IsA2SOk
        {
            get { lock (RegistrationThread) { return _IsA2SOk; } }
            private set { lock (RegistrationThread) { _IsA2SOk = value; } }
        }

        public RegisteredServer(Uri backend,
            C2ServerInfo serverInfo, string localIp,
            int updateIntervalMillis = 1000)
        {
            this.backend = backend;
            this.serverInfo = serverInfo;
            this.updateIntervalMillis = updateIntervalMillis;
            this.localIp = localIp;
            this.a2sLocation = new(IPAddress.Parse("127.0.0.1"), serverInfo.Ports.A2s);
            shutDownSource = new();

            RegistrationThread = new(
                    () => Run(shutDownSource.Token)
                    )
            {
                IsBackground = true
            };
            RegistrationThread.Start();
        }

        private async Task MaintainRegistration(CancellationToken token)
        {
            A2S_INFO a2sRes = await GetServerState(token);
            var res = await ServerBrowser.RegisterServerAsync(backend, localIp, new(serverInfo, a2sRes));
            double refreshBefore = res.RefreshBefore;
            string key = res.Key;
            RemoteInfo = res.Server;
            int heartBeatAfterSeconds = (int)(refreshBefore - DateTimeOffset.Now.ToUnixTimeSeconds() - 5);
            Task heartBeatDelay = Task.Delay(1000*heartBeatAfterSeconds, token);
            Task updateDelay = Task.Delay(updateIntervalMillis, token);
            while (true)
            {
                var fin = await Task.WhenAny(heartBeatDelay, updateDelay, token.WhenCanceled());
                token.ThrowIfCancellationRequested();
                if(fin == heartBeatDelay)
                {
                    refreshBefore = await ServerBrowser.HeartbeatAsync(backend, RemoteInfo, key);
                    heartBeatAfterSeconds = (int)(refreshBefore - DateTimeOffset.Now.ToUnixTimeSeconds() - 5);
                    heartBeatDelay = Task.Delay(1000 * heartBeatAfterSeconds, token);
                }
                else if(fin == updateDelay)
                {
                    if(RemoteInfo.Update(await GetServerState(token)))
                    {
                        //this is NOT an unnecessary assignment
                        refreshBefore = await ServerBrowser.UpdateServerAsync(backend, RemoteInfo, key);
                    }
                    updateDelay = Task.Delay(updateIntervalMillis, token);
                }
            }
        }
        private async void Run(CancellationToken token)
        {
            while (true)
            {
                try
                {
                    await MaintainRegistration(token);
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

        private async Task<A2S_INFO> GetServerState(CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var res = await A2S.InfoAsync(a2sLocation);
                    IsA2SOk = true;
                    return res;
                }
                catch (Exception e)
                {
                    IsA2SOk = false;
                    LastException = e;
                    await Task.Delay(1000, token); // try not to spam the network
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
                if (RegistrationThread != null && RegistrationThread.IsAlive)
                {
                    RegistrationThread.Join(); // Optional: You might want to add a timeout here
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
