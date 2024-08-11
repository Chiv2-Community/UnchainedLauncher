using System.Net;
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

    // TODO? separate this out a little bit so that maintaining A2S information doesn't
    // require being registered with a backend. this is definitely doable, but not super
    // critical because you need a backend to connect to a server properly anyways.
    [AddINotifyPropertyChangedInterface]
    public class RegisteredServer : IDisposable
    {
        // this thread object is used as the mutex
        private readonly Thread RegistrationThread;
        private readonly CancellationTokenSource shutDownSource;
        private bool disposed;
        //TODO: make these all properties.
        //WPF can only bind properties
        public C2ServerInfo serverInfo { get; private set; }
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
        // I don't like this property because it's only used to delete a server
        // after the stack unwinds up to a point where it's convenient
        // after the cancellation token is signaled
        private string? key;
        private Exception? _LastException;
        public Exception? LastException
        {
            get { lock (RegistrationThread) { return _LastException; } }
            private set { lock (RegistrationThread) { _LastException = value; } }
        }
        private Exception? _LastHttpException;
        public Exception? LastHttpException
        {
            get { lock (RegistrationThread) { return _LastHttpException; } }
            private set { lock (RegistrationThread) { _LastHttpException = value; } }
        }
        private bool _IsRegistrationOk = false;
        public bool IsRegistrationOk
        {
            get { lock (RegistrationThread) { return _IsRegistrationOk; } }
            private set { lock (RegistrationThread) { _IsRegistrationOk = value; } }
        }

        TaskCompletionSource<bool> _IsA2SOkTCS;
        private bool _IsA2SOk = false;
        public bool IsA2SOk
        {
            get { lock (RegistrationThread) { return _IsA2SOk; } }
            private set { 
                lock (RegistrationThread) {
                    bool oldV = _IsA2SOk;
                    _IsA2SOk = value;
                    if (_IsA2SOk) // if it became true, then signal the tasks
                    {
                        _IsA2SOkTCS.TrySetResult(true);
                    }
                    else if(oldV != value) 
                    {
                        // if it became false, AND it was previously true, reset the task
                        // so that future requestors have to wait
                        _IsA2SOkTCS = new(shutDownSource);
                    }
                }
            }
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
            this.shutDownSource = new();
            this._IsA2SOkTCS = new(shutDownSource);


            RegistrationThread = new(
                    () => Run(shutDownSource.Token)
                    )
            {
                IsBackground = true
            };
            RegistrationThread.Start();
        }

        // return a task that completes when IsA2SOk is true
        public Task WhenA2SOk()
        {
            return _IsA2SOkTCS.Task;
        }

        private async Task MaintainRegistration(CancellationToken token)
        {
            A2S_INFO a2sRes = await GetServerState(token);
            var res = await ServerBrowser.RegisterServerAsync(backend, localIp, new(serverInfo, a2sRes));
            this.IsRegistrationOk = true;
            double refreshBefore = res.RefreshBefore;
            string key = res.Key;
            this.key = key;
            RemoteInfo = res.Server;
            Task updateDelay = Task.Delay(updateIntervalMillis, token);
            while (true)
            {
                int heartBeatAfterSeconds = (int)(refreshBefore - DateTimeOffset.Now.ToUnixTimeSeconds() - 5);
                Task heartBeatDelay = Task.Delay(1000 * heartBeatAfterSeconds, token);
                var fin = await Task.WhenAny(heartBeatDelay, updateDelay, token.WhenCanceled());
                token.ThrowIfCancellationRequested();
                if(fin == heartBeatDelay)
                {
                    refreshBefore = await ServerBrowser.HeartbeatAsync(backend, RemoteInfo, key);
                }
                else if(fin == updateDelay)
                {
                    if(RemoteInfo.Update(await GetServerState(token)))
                    {
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
                    this.LastHttpException = e;
                    if (e.StatusCode != HttpStatusCode.NotFound)
                    {
                        break;
                    }
                }
                catch (TimeoutException e)
                {
                    this.LastHttpException = e;
                }
                catch (OperationCanceledException) //propagate cancellation
                {
                    if (RemoteInfo != null && this.key != null)
                    {
                        try
                        {
                            await ServerBrowser.DeleteServerAsync(backend, RemoteInfo, this.key);
                        }
                        catch { }
                        // we want to try to be nice and neat, but if anything goes wrong then
                        // just give up here and let the heartbeat timeout clean things up on the
                        // server-side

                    }
                    break;
                }
                finally
                {
                    this.IsRegistrationOk = false;
                    this.IsA2SOk = false;
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
                    await Task.Delay(500, token); // try not to spam the network
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
                    // TODO? might want to add a timeout here
                    // we handle cancelation pretty cleanly within the thread, though,
                    // so it should be fine
                    RegistrationThread.Join(); 
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
