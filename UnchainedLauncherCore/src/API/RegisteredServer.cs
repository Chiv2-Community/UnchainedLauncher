using System.Net;
using log4net;
using Newtonsoft.Json.Linq;
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
        private static readonly ILog logger = LogManager.GetLogger(nameof(RegisteredServer));
        // this thread object is used as the mutex
        private readonly Thread RegistrationThread;
        private readonly CancellationTokenSource shutDownSource;
        private bool disposed;
        //TODO: make these all properties.
        //WPF can only bind properties
        public C2ServerInfo ServerInfo { get; private set; }
        public readonly int updateIntervalMillis;
        public readonly string localIp;
        public readonly IServerBrowser backend;
        public readonly IA2S A2sEndpoint;
        public readonly int HeartbeatSecondsBeforeTimeout;
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

        public RegisteredServer(
            IServerBrowser backend,
            IA2S a2sEndpoint,
            C2ServerInfo serverInfo, 
            string localIp,
            int heartbeatSecondsBeforeTimeout = 5,
            int updateIntervalMillis = 1000
            )
        {
            this.ServerInfo = serverInfo;
            this.updateIntervalMillis = updateIntervalMillis;
            this.localIp = localIp;
            this.shutDownSource = new();
            this._IsA2SOkTCS = new(shutDownSource);

            this.backend = backend;
            this.A2sEndpoint = a2sEndpoint;

            logger.Info(
                $"Server '{serverInfo.Name}' will use backend at '{backend.Host}'\nPorts:"
                + serverInfo.Ports.ToString()
            );

            RegistrationThread = new(
                    () => Run(shutDownSource.Token)
                    )
            {
                IsBackground = true
            };
            RegistrationThread.Start();
        }

        /// <summary>
        /// wait until IsA2SOk is true
        /// </summary>
        /// <returns>
        /// A task that completes when IsA2SOk is true.
        /// </returns>
        public Task WhenA2SOk()
        {
            return _IsA2SOkTCS.Task;
        }

        private async Task MaintainRegistration(CancellationToken token)
        {
            A2sInfo a2sRes = await GetServerState(token);
            var res = await backend.RegisterServerAsync(localIp, new(ServerInfo, a2sRes));
            logger.Info($"Server '{this.ServerInfo.Name}' registered with backend.");
            this.IsRegistrationOk = true;
            RemoteInfo = res.Server;

            var heartBeatDelay = Task.Delay(TimeSpan.FromSeconds(res.RefreshBefore - DateTimeOffset.Now.ToUnixTimeSeconds() - HeartbeatSecondsBeforeTimeout), token);
            var updateDelay = Task.Delay(TimeSpan.FromMilliseconds(updateIntervalMillis), token);

            while (true) {
                // Wait for the first task to complete (heartbeat, update, or cancellation)
                var completedTask = await Task.WhenAny(heartBeatDelay, updateDelay, token.WhenCanceled());

                // Check for cancellation before proceeding
                token.ThrowIfCancellationRequested();

                // Handle the completed task
                if (completedTask == heartBeatDelay) {
                    logger.Info($"Server '{this.ServerInfo.Name}' doing heartbeat.");
                    var refreshBefore = await backend.HeartbeatAsync(RemoteInfo);
                    heartBeatDelay = Task.Delay(TimeSpan.FromSeconds(refreshBefore - DateTimeOffset.Now.ToUnixTimeSeconds() - HeartbeatSecondsBeforeTimeout), token);
                } else if (completedTask == updateDelay) {
                    // If the server state has changed, update the backend
                    if (RemoteInfo.Update(await GetServerState(token))) {
                        logger.Info($"Server '{this.ServerInfo.Name}' updating the backend with new state.");
                        await backend.UpdateServerAsync(RemoteInfo);
                    }
                    updateDelay = Task.Delay(TimeSpan.FromMilliseconds(updateIntervalMillis), token);
                } else {
                    break; // Cancellation requested
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
                    if (e.StatusCode == HttpStatusCode.NotFound)
                    {
                        logger.Error($"Server '{this.ServerInfo.Name}' got HTTP 404. Probably a missed heartbeat, will attempt to re-register.", e);
                    } else {
                        logger.Error($"Server '{this.ServerInfo.Name}' got status code {e.StatusCode} during registration loop.", e);
                        break;
                    }
                }
                catch (TimeoutException e)
                {
                    logger.Error($"Server '{this.ServerInfo.Name}' timed out.", e);
                    this.LastHttpException = e;
                }
                catch (OperationCanceledException) //propagate cancellation
                {
                    if (RemoteInfo != null)
                    {
                        // we want to try to be nice and neat, but if anything goes wrong then
                        // just give up here and let the heartbeat timeout clean things up on the
                        // server-side
                        try
                        {
                            await backend.DeleteServerAsync(RemoteInfo);
                        }
                        catch(Exception e) {
                            logger.Error($"Server '{this.ServerInfo.Name}' failed to delete itself on backend.", e);
                        }
                    }
                    break;
                }
                finally
                {
                    this.IsRegistrationOk = false;
                    this.IsA2SOk = false;
                    await Task.Delay(updateIntervalMillis, token);
                }
            }
        }

        private async Task<A2sInfo> GetServerState(CancellationToken token)
        {
            var repeatErrorCount = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    logger.Debug($"Server '{this.ServerInfo.Name}' is requesting the A2S state");
                    var res = await A2sEndpoint.InfoAsync();
                    logger.Debug($"Server '{this.ServerInfo.Name}' got A2S state. {res}");
                    IsA2SOk = true;
                    return res;
                }
                catch (Exception e)
                {
                    IsA2SOk = false;
                    if(e.GetType() != LastException?.GetType() || e.Message != LastException?.Message) {
                        logger.Error($"Server '{this.ServerInfo.Name}' failed to get A2S state.", e);
                    } else if(repeatErrorCount < 10){
                        logger.Debug($"A2S state request failed, but the error was the same as the last one. ({repeatErrorCount})", e);
                        repeatErrorCount++;
                    } else if (repeatErrorCount == 10) {
                        logger.Debug($"A2S state request failed, but the error was the same as the last one. ({repeatErrorCount}). Supressing logs until something changes.");
                        repeatErrorCount++;
                    }
                    LastException = e;
                    await Task.Delay(updateIntervalMillis, token); // try not to spam the network
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
