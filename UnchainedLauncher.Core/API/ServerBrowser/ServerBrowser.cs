using PropertyChanged;
using System.Net.Http.Json;
using System.Text.Json;
using System.Timers;
using UnchainedLauncher.Core.API.A2S;
using Timer = System.Threading.Timer;

namespace UnchainedLauncher.Core.API.ServerBrowser {
    // NOTE: some of these records have empty constructors that don't really seem useful.
    // They need to be there for serialization to work!

    public record PublicPorts(
        int Game,
        int Ping,
        int A2s
    );

    public record ServerBrowserMod(string Name, string Organization, string Version);

    [AddINotifyPropertyChangedInterface]
    public partial record C2ServerInfo {
        public bool PasswordProtected { get; set; } = false;
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public PublicPorts Ports { get; set; } = new(7777, 3075, 7071);
        // TODO: The selection of the Mod datatype here is potentially
        // incorrect. Change it to whatever is easier and works with the backend
        public ServerBrowserMod[] Mods { get; set; } = Array.Empty<ServerBrowserMod>();
    };

    // TODO: This part of the API should be stabilized
    // as-is, there are numerous different kinds of "server" objects depending
    // on where they're coming from/where they're going
    public record ServerInfo : C2ServerInfo {
        public string CurrentMap { get; set; } = "";
        public int PlayerCount { get; set; } = 0;
        public int MaxPlayers { get; set; } = 0;
        public ServerInfo() { }
        public ServerInfo(C2ServerInfo info, A2sInfo a2s) : base(info) {
            Update(a2s);
        }

        public bool Update(A2sInfo a2s) {
            bool wasChanged = (MaxPlayers, PlayerCount, CurrentMap) != (a2s.MaxPlayers, a2s.Players, a2s.Map);
            (MaxPlayers, PlayerCount, CurrentMap) = (a2s.MaxPlayers, a2s.Players, a2s.Map);
            return wasChanged;
        }
    }

    public record UniqueServerInfo : ServerInfo {
        public UniqueServerInfo(string UniqueId, double LastHeartbeat) {
            this.UniqueId = UniqueId;
            this.LastHeartbeat = LastHeartbeat;
        }
        public UniqueServerInfo(ServerInfo info, string UniqueId, double LastHeartbeat) : base(info) {
            this.UniqueId = UniqueId;
            this.LastHeartbeat = LastHeartbeat;
        }

        public UniqueServerInfo() { }
        public string UniqueId { get; set; } = "";
        public double LastHeartbeat { get; set; } = 0;
    };

    public record RegisterServerRequest : ServerInfo {
        public string LocalIpAddress { get; set; }
        public RegisterServerRequest(ServerInfo info, string localIpAddress) : base(info) {
            LocalIpAddress = localIpAddress;
        }
    }

    public record ResponseServer : UniqueServerInfo {
        public string LocalIpAddress { get; set; } = "";
        public string IpAddress { get; set; } = "";

        public ResponseServer() { }

        public ResponseServer(UniqueServerInfo info, string localIpAddress, string ipAddress) : base(info) {
            LocalIpAddress = localIpAddress;
            IpAddress = ipAddress;
        }
    }
    public record RegisterServerResponse(
        double RefreshBefore,
        string Key,
        ResponseServer Server
    );
    public record UpdateServerRequest(
        int PlayerCount,
        int MaxPlayers,
        string CurrentMap
    );

    public record UpdateServerResponse(double RefreshBefore, ResponseServer Server);

    record GetServersResponse(
        UniqueServerInfo[] Servers
    );

    public interface IServerBrowser : IDisposable {
        public string Host { get; }

        public Task<RegisterServerResponse> RegisterServerAsync(string localIp, ServerInfo info, CancellationToken? ct = null);
        public Task<double> UpdateServerAsync(UniqueServerInfo info, string key, CancellationToken? ct = null);
        public Task<double> HeartbeatAsync(UniqueServerInfo info, string key, CancellationToken? ct = null);
        public Task DeleteServerAsync(UniqueServerInfo info, string key, CancellationToken? ct = null);
    }

    /// <summary>
    /// Allows communication with the server browser backend for registering and updating server entries
    /// Should only be used with one server. The auth key sent with requests is the one received from the
    /// most recent RegisterServer call. Does not support touching servers that it
    /// didn't register itself.
    /// 
    /// URI is expected to have the /api/v1/ stuff.
    /// 
    /// IS thread-safe
    /// </summary>
    public class ServerBrowser : IServerBrowser {
        public const string KEY_HEADER = "x-chiv2-server-browser-key";
        protected HttpClient HttpClient;
        protected Uri backend_uri;
        private bool disposedValue;
        protected static readonly JsonSerializerOptions sOptions = new() {
            PropertyNamingPolicy = new SnakeCaseNamingPolicy(),
            IncludeFields = true
        };
        public TimeSpan TimeOutMillis {
            get { return HttpClient.Timeout; }
            set { HttpClient.Timeout = value; }
        }

        public string Host => backend_uri.Host;

        public ServerBrowser(Uri backend_uri, HttpClient client, int TimeOutMillis = 4000) {
            HttpClient = client;
            client.Timeout = TimeSpan.FromMilliseconds(TimeOutMillis);
            this.TimeOutMillis = TimeSpan.FromMilliseconds(TimeOutMillis);
            this.backend_uri = backend_uri;
        }

        public ServerBrowser(Uri backend_uri, int TimeOutMillis = 4000) : this(backend_uri, new HttpClient(), TimeOutMillis) { }

        public async Task<RegisterServerResponse> RegisterServerAsync(string localIp, ServerInfo info, CancellationToken? ct = null) {
            var reqContent = new RegisterServerRequest(info, localIp);
            var content = JsonContent.Create(reqContent, options: sOptions);
            var httpResponse = await HttpClient.PostAsync(backend_uri + "/servers", content, ct ?? CancellationToken.None);
            try {
                var res = await httpResponse.EnsureSuccessStatusCode()
                            .Content
                            .ReadFromJsonAsync<RegisterServerResponse>(sOptions, ct ?? CancellationToken.None);
                if (res == null) {
                    throw new InvalidDataException("Failed to parse RegisterServer response from server");
                }
                else {
                    return res;
                }
            }
            catch (InvalidOperationException e) {
                throw new InvalidDataException("Failed to parse RegisterServer response from server", e);
            }
        }

        public async Task<double> UpdateServerAsync(UniqueServerInfo info, string key, CancellationToken? ct = null) {

            var reqContent = new UpdateServerRequest(info.PlayerCount, info.MaxPlayers, info.CurrentMap);
            var content = JsonContent.Create(reqContent, options: sOptions);
            content.Headers.Add(KEY_HEADER, key);
            var httpResponse = await HttpClient.PutAsync(backend_uri + $"/servers/{info.UniqueId}", content, ct ?? CancellationToken.None);
            try {
                var res = await httpResponse.EnsureSuccessStatusCode()
                            .Content
                            .ReadFromJsonAsync<UpdateServerResponse>(sOptions, ct ?? CancellationToken.None);
                if (res == null) {
                    throw new InvalidDataException("Failed to parse Update response from server");
                }
                else {
                    return res.RefreshBefore;
                }
            }
            catch (InvalidOperationException e) {
                throw new InvalidDataException("Failed to parse Update response from server", e);
            }
        }

        public async Task<double> HeartbeatAsync(UniqueServerInfo info, string key, CancellationToken? ct = null) {

            //var reqContent = new UpdateServerRequest(info.playerCount, info.maxPlayers, info.currentMap);
            var content = new StringContent("");
            content.Headers.Add(KEY_HEADER, key);
            var httpResponse = await HttpClient.PostAsync(backend_uri + $"/servers/{info.UniqueId}/heartbeat", content, ct ?? CancellationToken.None);
            try {
                var res = await httpResponse.EnsureSuccessStatusCode()
                            .Content
                            .ReadFromJsonAsync<UpdateServerResponse>(sOptions, ct ?? CancellationToken.None);
                if (res == null) {
                    throw new InvalidDataException("Failed to parse Heartbeat response from server");
                }
                else {
                    return res.RefreshBefore;
                }
            }
            catch (InvalidOperationException e) {
                throw new InvalidDataException("Failed to parse Heartbeat response from server", e);
            }
        }

        public async Task DeleteServerAsync(UniqueServerInfo info, string key, CancellationToken? ct = null) {
            //var reqContent = new UpdateServerRequest(info.playerCount, info.maxPlayers, info.currentMap);
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Delete, backend_uri + $"/servers/{info.UniqueId}");
            request.Headers.Add(KEY_HEADER, key);
            //the DeleteAsync method does not take a content parameter.
            //that's a problem, since that's the only way to set headers
            //without setting state on the HttpClient
            (await HttpClient.SendAsync(request, ct ?? CancellationToken.None)).EnsureSuccessStatusCode();
        }

        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    HttpClient.Dispose();
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

    /// <summary>
    /// A server browser to use when you don't actually want to use a server browser.
    /// Its methods will never throw exceptions and always complete instantly.
    /// Really just a less useful testing mock made for the option of *not* registering
    /// with a server browser in production.
    /// </summary>
    public class NullServerBrowser : IServerBrowser {
        public double RefreshFrequency;
        private bool disposedValue;

        public NullServerBrowser(double refreshFrequency = 60) {
            RefreshFrequency = refreshFrequency;
        }
        public string Host => "none";

        private double GetRefreshBeforeTime() {
            return (double)(DateTimeOffset.Now.ToUnixTimeSeconds() + RefreshFrequency);
        }

        public Task DeleteServerAsync(UniqueServerInfo info, string key, CancellationToken? ct = null) {
            return Task.CompletedTask;
        }

        public Task<double> HeartbeatAsync(UniqueServerInfo info, string key, CancellationToken? ct = null) {
            return Task.FromResult(GetRefreshBeforeTime());
        }

        public Task<RegisterServerResponse> RegisterServerAsync(string localIp, ServerInfo info, CancellationToken? ct = null) {
            var uniqueInfo = new UniqueServerInfo(info, "Null_ID", DateTimeOffset.Now.ToUnixTimeSeconds());
            var responseServer = new ResponseServer(uniqueInfo, "127.0.0.1", "127.0.0.1");
            var response = new RegisterServerResponse(GetRefreshBeforeTime(), "backend_key", responseServer);
            return Task.FromResult(response);
        }

        public Task<double> UpdateServerAsync(UniqueServerInfo info, string key, CancellationToken? ct = null) {
            return Task.FromResult(GetRefreshBeforeTime());
        }

        // we don't actually have anything to dispose, but we still have to
        // satisfy the interface
        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                disposedValue = true;
            }
        }

        public void Dispose() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    // TODO: this
    /// <summary>
    /// Throttles requests to a backend so that it's not possible to spam it by accident
    /// </summary>
    //public class ThrottledServerBrowser : IServerBrowser
    //{
    //    protected readonly IServerBrowser browser;
    //    ThrottledServerBrowser(IServerBrowser browser, int maxFrequency)
    //    {
    //        this.browser = browser;
    //    }
    //}
}