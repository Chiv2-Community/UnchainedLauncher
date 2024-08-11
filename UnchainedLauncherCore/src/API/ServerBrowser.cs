using System.Net.Http.Json;
using System.Text.Json;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using PropertyChanged;
using static System.Net.WebRequestMethods;

namespace UnchainedLauncher.Core.API
{
    public record Ports(
        int Game,
        int Ping,
        int A2s
    );
    [AddINotifyPropertyChangedInterface]
    public record C2ServerInfo {
        public bool PasswordProtected { get; set; } = false;
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public Ports Ports { get; set; } = new(7777, 3075, 7071);
        // TODO: The selection of the Mod datatype here is potentially
        // incorrect. Change it to whatever is easier and works with the backend
        public Mod[] Mods { get; set; } = Array.Empty<Mod>(); 
    };
    // TODO: This part of the API should be stabilized
    // as-is, there are numerous different kinds of "server" objects depending
    // on where they're coming from/where they're going
    public record ServerInfo : C2ServerInfo{
        public String CurrentMap { get; set; } = "";
        public int PlayerCount { get; set; } = 0;
        public int MaxPlayers { get; set; } = 0;
        public ServerInfo() { }
        public ServerInfo(C2ServerInfo info, A2sInfo a2s) : base(info) {
            Update(a2s);
        }
        public bool Update(A2sInfo a2s)
        {
            bool wasChanged = (MaxPlayers, PlayerCount, CurrentMap) != (a2s.MaxPlayers, a2s.Players, a2s.Map);
            (MaxPlayers, PlayerCount, CurrentMap) = (a2s.MaxPlayers, a2s.Players, a2s.Map);
            return wasChanged;
        }
    }

    public record UniqueServerInfo : ServerInfo {
        public String UniqueId { get; set; } = "";
        public double LastHeartbeat { get; set; } = 0;
    };
    
    public record RegisterServerRequest : ServerInfo
    {
        public String LocalIpAddress { get; set; }
        public RegisterServerRequest(ServerInfo info, string localIpAddress) : base(info)
        {
            this.LocalIpAddress = localIpAddress;
        }
    }

    public record ResponseServer : UniqueServerInfo
    {
        public String LocalIpAddress { get; set; } = "";
        public String IpAddress { get; set; } = "";
        public ResponseServer() { }
        public ResponseServer(UniqueServerInfo info, string localIpAddress, string ipAddress) : base(info)
        {
            this.LocalIpAddress=localIpAddress;
            this.IpAddress=ipAddress;
        }
    }
    public record RegisterServerResponse(
        double RefreshBefore,
        String Key,
        ResponseServer Server
    );
    public record UpdateServerRequest(
        int PlayerCount,
        int MaxPlayers,
        String CurrentMap
    );

    public record UpdateServerResponse(double RefreshBefore, ResponseServer Server);

    record GetServersResponse(
        UniqueServerInfo[] Servers  
    );

    public interface IServerBrowser
    {
        public Task<RegisterServerResponse> RegisterServerAsync(String localIp, ServerInfo info, CancellationToken? ct = null);
        public Task<double> UpdateServerAsync(UniqueServerInfo info, CancellationToken? ct = null);
        public Task<double> HeartbeatAsync(UniqueServerInfo info, CancellationToken? ct = null);
        public Task DeleteServerAsync(UniqueServerInfo info, CancellationToken? ct = null);
    }

    /// <summary>
    /// Allows communication with the server browser backend for registering and updating server entries
    /// Should only be used with one server. The auth key sent with requests is the one received from the
    /// most recent RegisterServer call. Does not support touching servers that it
    /// didn't register itself.
    /// 
    /// URI is expected to have the /api/v1/ stuff.
    /// 
    /// NOT thread-safe
    /// </summary>
    public class ServerBrowser : IServerBrowser
    {
        protected HttpClient httpc;
        protected Uri backend_uri;
        protected string? _LastKey;
        public string? LastKey { 
            get { return _LastKey; }
            set { 
                _LastKey = value;
                httpc.DefaultRequestHeaders.Remove("x-chiv2-server-browser-key"); // remove old
                httpc.DefaultRequestHeaders.Add("x-chiv2-server-browser-key", value); // set new
            } 
        }
        protected static readonly JsonSerializerOptions sOptions = new(){
                PropertyNamingPolicy = new SnakeCaseNamingPolicy(),
                IncludeFields = true
            };
        public TimeSpan TimeOutMillis
        {
            get { return httpc.Timeout; }
            set { httpc.Timeout = value; }
        }

        public ServerBrowser(Uri backend_uri, int TimeOutMillis = 4000, HttpClient? client = null) {
            if(client != null)
            {
                httpc = client;
            }
            else
            {
                httpc = new();
            }
            this.TimeOutMillis = TimeSpan.FromMilliseconds(TimeOutMillis);
            this.backend_uri = backend_uri;
        }

        public async Task<RegisterServerResponse> RegisterServerAsync(String localIp, ServerInfo info, CancellationToken? ct = null)
        {
            var reqContent = new RegisterServerRequest(info, localIp);
            var content = JsonContent.Create(reqContent, options: sOptions);
            var httpResponse = await httpc.PostAsync(backend_uri + "/servers", content, ct ?? CancellationToken.None);
            try
            {
                var res = await httpResponse.EnsureSuccessStatusCode()
                            .Content
                            .ReadFromJsonAsync<RegisterServerResponse>(sOptions, ct ?? CancellationToken.None);
                if(res == null)
                {
                    throw new InvalidDataException("Failed to parse response from server");
                }
                else
                {
                    LastKey = res.Key; // hold onto the key for later requests
                    return res;
                }
            }
            catch (InvalidOperationException e)
            {
                throw new InvalidDataException("Failed to parse response from server", e);
            }
        }

        public async Task<double> UpdateServerAsync(UniqueServerInfo info, CancellationToken? ct = null)
        {
            
            var reqContent = new UpdateServerRequest(info.PlayerCount, info.MaxPlayers, info.CurrentMap);
            var content = JsonContent.Create(reqContent, options: sOptions);
            var httpResponse = await httpc.PutAsync(backend_uri + $"/servers/{info.UniqueId}", content, ct ?? CancellationToken.None);
            try
            {
                var res = await httpResponse.EnsureSuccessStatusCode()
                            .Content
                            .ReadFromJsonAsync<UpdateServerResponse>(sOptions, ct ?? CancellationToken.None);
                if (res == null)
                {
                    throw new InvalidDataException("Failed to parse response from server");
                }
                else
                {
                    return res.RefreshBefore;
                }
            }
            catch (InvalidOperationException e)
            {
                throw new InvalidDataException("Failed to parse response from server", e);
            }
        }

        public async Task<double> HeartbeatAsync(UniqueServerInfo info, CancellationToken? ct = null)
        {
            
            //var reqContent = new UpdateServerRequest(info.playerCount, info.maxPlayers, info.currentMap);
            var content = new StringContent("");
            var httpResponse = await httpc.PostAsync(backend_uri + $"/servers/{info.UniqueId}/heartbeat", content, ct ?? CancellationToken.None);
            try
            {
                var res = await httpResponse.EnsureSuccessStatusCode()
                            .Content
                            .ReadFromJsonAsync<UpdateServerResponse>(sOptions, ct ?? CancellationToken.None);
                if (res == null)
                {
                    throw new InvalidDataException("Failed to parse response from server");
                }
                else
                {
                    return res.RefreshBefore;
                }
            }
            catch (InvalidOperationException e)
            {
                throw new InvalidDataException("Failed to parse response from server", e);
            }
        }

        public async Task DeleteServerAsync(UniqueServerInfo info, CancellationToken? ct = null)
        {
            //var reqContent = new UpdateServerRequest(info.playerCount, info.maxPlayers, info.currentMap);
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Delete, backend_uri + $"/servers/{info.UniqueId}");
            //the DeleteAsync method does not take a content parameter
            //that's a problem, since that's the only way to set headers
            //without setting state on the HttpClient
            //TODO: make this DeleteAsync
            (await httpc.SendAsync(request, ct ?? CancellationToken.None)).EnsureSuccessStatusCode();
        }
    }
}
