using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnchainedLauncher.Core.API;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using System.Runtime.Loader;

namespace UnchainedLauncher.Core.API
{
    public record Ports(
        int Game,
        int Ping,
        int A2s
    );
    public record C2ServerInfo {
        public bool PasswordProtected = false;
        public string Name = "";
        public string Description = "";
        public Ports Ports = new(7777, 3075, 7071);
        public Mod[] Mods = Array.Empty<Mod>();
    };
    // TODO: This part of the API should be stabilized
    // as-is, there are numerous different kinds of "server" objects depending
    // on where they're coming from/where they're going
    public record ServerInfo : C2ServerInfo{
        public String CurrentMap = "";
        public int PlayerCount = 0;
        public int MaxPlayers = 0;
        public ServerInfo() { }
        public ServerInfo(C2ServerInfo info, A2S_INFO a2s) : base(info) {
            Update(a2s);
        }
        public bool Update(A2S_INFO a2s)
        {
            bool wasChanged = (
                MaxPlayers != a2s.MaxPlayers
                || PlayerCount != a2s.Players
                || CurrentMap != a2s.Map);
            MaxPlayers = a2s.MaxPlayers;
            PlayerCount = a2s.Players;
            CurrentMap = a2s.Map;
            return wasChanged;
        }
    }

    public record UniqueServerInfo : ServerInfo {
        public String UniqueId = "";
        public double LastHeartbeat = 0;
    };
    
    public record RegisterServerRequest : ServerInfo
    {
        public String LocalIpAddress;
        public RegisterServerRequest(ServerInfo info, string localIpAddress) : base(info)
        {
            this.LocalIpAddress = localIpAddress;
        }
    }

    public record ResponseServer : UniqueServerInfo
    {
        public String LocalIpAddress = "";
        public String IpAddress = "";
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
    //URI is expected to have the /api/v1/ stuff. These functions only append the immediately relevant paths
    public static class ServerBrowser
    {
        static readonly HttpClient httpc = new();
        private static readonly JsonSerializerOptions sOptions = new(){
                PropertyNamingPolicy = new SnakeCaseNamingPolicy(),
                IncludeFields = true
            };
        const int TimeOutMillis = 1000;

        public static async Task<RegisterServerResponse> RegisterServerAsync(Uri uri, String localIp, ServerInfo info)
        {
            try
            {
                return await RegisterServerAsync_impl(uri, localIp, info);
            }catch(TaskCanceledException)
            {
                throw new TimeoutException("Request timed out.");
            }
        }
        public static async Task<double> UpdateServerAsync(Uri uri, UniqueServerInfo info, String key)
        {
            try
            {
                return await UpdateServerAsync_impl(uri, info, key);
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException("Request timed out.");
            }
        }

        public static async Task<double> HeartbeatAsync(Uri uri, UniqueServerInfo info, String key)
        {
            try
            {
                return await HeartbeatAsync_impl(uri, info, key);
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException("Request timed out.");
            }
        }

        public static async Task DeleteServerAsync(Uri uri, UniqueServerInfo info, String key)
        {
            try
            {
                await DeleteServerAsync_impl(uri, info, key);
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException("Request timed out.");
            }
        }

        private static async Task<RegisterServerResponse> RegisterServerAsync_impl(Uri uri, String localIp, ServerInfo info)
        {
            using CancellationTokenSource cs = new(TimeOutMillis);
            var reqContent = new RegisterServerRequest(info, localIp);
            var content = JsonContent.Create(reqContent, options: sOptions);
            var httpResponse = await httpc.PostAsync(uri + "/servers", content, cs.Token);
            try
            {
                var res = await httpResponse.EnsureSuccessStatusCode()
                            .Content
                            .ReadFromJsonAsync<RegisterServerResponse>(sOptions, cs.Token);
                if(res == null)
                {
                    throw new InvalidDataException("Failed to parse response from server");
                }
                else
                {
                    return res;
                }
            }
            catch (InvalidOperationException e)
            {
                throw new InvalidDataException("Failed to parse response from server", e);
            }
        }

        private static async Task<double> UpdateServerAsync_impl(Uri uri, UniqueServerInfo info, String key)
        {
            using CancellationTokenSource cs = new(TimeOutMillis);
            var reqContent = new UpdateServerRequest(info.PlayerCount, info.MaxPlayers, info.CurrentMap);
            var content = JsonContent.Create(reqContent, options: sOptions);
            content.Headers.Add("x-chiv2-server-browser-key", key);
            var httpResponse = await httpc.PutAsync(uri + $"/servers/{info.UniqueId}", content, cs.Token);
            try
            {
                var res = await httpResponse.EnsureSuccessStatusCode()
                            .Content
                            .ReadFromJsonAsync<UpdateServerResponse>(sOptions, cs.Token);
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

        private static async Task<double> HeartbeatAsync_impl(Uri uri, UniqueServerInfo info, String key)
        {
            using CancellationTokenSource cs = new(TimeOutMillis);
            //var reqContent = new UpdateServerRequest(info.playerCount, info.maxPlayers, info.currentMap);
            var content = new StringContent("");
            content.Headers.Add("x-chiv2-server-browser-key", key);
            var httpResponse = await httpc.PostAsync(uri + $"/servers/{info.UniqueId}/heartbeat", content, cs.Token);
            try
            {
                var res = await httpResponse.EnsureSuccessStatusCode()
                            .Content
                            .ReadFromJsonAsync<UpdateServerResponse>(sOptions, cs.Token);
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

        private static async Task DeleteServerAsync_impl(Uri uri, UniqueServerInfo info, String key)
        {
            using CancellationTokenSource cs = new(TimeOutMillis);
            //var reqContent = new UpdateServerRequest(info.playerCount, info.maxPlayers, info.currentMap);
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Delete, uri + $"/servers/{info.UniqueId}");
            request.Headers.Add("x-chiv2-server-browser-key", key);
            //the DeleteAsync method does not take a content parameter
            //that's a problem, since that's the only way to set headers
            //without setting state on the HttpClient
            (await httpc.SendAsync(request, cs.Token)).EnsureSuccessStatusCode();
        }
    }
}
