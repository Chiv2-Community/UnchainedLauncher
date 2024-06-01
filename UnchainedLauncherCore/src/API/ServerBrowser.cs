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
        int game,
        int ping,
        int a2s
    );
    public record C2ServerInfo {
        public bool passwordProtected = false;
        public string name = "";
        public string description = "";
        public Ports ports = new(7777, 3075, 7071);
        public Mod[] mods = Array.Empty<Mod>();
    };
    // TODO: This part of the API should be stabilized
    // as-is, there are numerous different kinds of "server" objects depending
    // on where they're coming from/where they're going
    public record ServerInfo : C2ServerInfo{
        public String currentMap = "";
        public int playerCount = 0;
        public int maxPlayers = 0;
        public ServerInfo() { }
        public ServerInfo(C2ServerInfo info, A2S_INFO a2s) : base(info) {
            update(a2s);
        }
        public bool update(A2S_INFO a2s)
        {
            bool wasChanged = (
                maxPlayers != a2s.maxPlayers
                || playerCount != a2s.players
                || currentMap != a2s.map);
            maxPlayers = a2s.maxPlayers;
            playerCount = a2s.players;
            currentMap = a2s.map;
            return wasChanged;
        }
    }

    public record UniqueServerInfo : ServerInfo {
        public String uniqueId = "";
        public double lastHeartbeat = 0;
    };
    
    public record RegisterServerRequest : ServerInfo
    {
        public String localIpAddress;
        public RegisterServerRequest(ServerInfo info, string localIpAddress) : base(info)
        {
            this.localIpAddress = localIpAddress;
        }
    }

    public record ResponseServer : UniqueServerInfo
    {
        public String localIpAddress = "";
        public String ipAddress = "";
        public ResponseServer() { }
        public ResponseServer(UniqueServerInfo info, string localIpAddress, string ipAddress) : base(info)
        {
            this.localIpAddress=localIpAddress;
            this.ipAddress=ipAddress;
        }
    }
    public record RegisterServerResponse(
        double refreshBefore,
        String key,
        ResponseServer server
    );
    public record UpdateServerRequest(
        int playerCount,
        int maxPlayers,
        String currentMap
    );

    public record UpdateServerResponse(double refreshBefore, ResponseServer server);

    record getServersResponse(
        UniqueServerInfo[] servers  
    );
    //URI is expected to have the /api/v1/ stuff. These functions only append the immediately relevant paths
    public static class ServerBrowser
    {
        static readonly HttpClient httpc = new();
        private static readonly JsonSerializerOptions sOptions = new(){
                PropertyNamingPolicy = new SnakeCaseNamingPolicy(),
                IncludeFields = true
            };
        const int timeOutMillis = 1000;

        public static async Task<RegisterServerResponse> registerServerAsync(Uri uri, String localIp, ServerInfo info)
        {
            try
            {
                return await registerServerAsync_impl(uri, localIp, info);
            }catch(TaskCanceledException)
            {
                throw new TimeoutException("Request timed out.");
            }
        }
        public static async Task<double> updateServerAsync(Uri uri, UniqueServerInfo info, String key)
        {
            try
            {
                return await updateServerAsync_impl(uri, info, key);
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException("Request timed out.");
            }
        }

        public static async Task<double> heartbeatAsync(Uri uri, UniqueServerInfo info, String key)
        {
            try
            {
                return await heartbeatAsync_impl(uri, info, key);
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException("Request timed out.");
            }
        }

        public static async Task deleteServerAsync(Uri uri, UniqueServerInfo info, String key)
        {
            try
            {
                await deleteServerAsync_impl(uri, info, key);
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException("Request timed out.");
            }
        }

        private static async Task<RegisterServerResponse> registerServerAsync_impl(Uri uri, String localIp, ServerInfo info)
        {
            using CancellationTokenSource cs = new(timeOutMillis);
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

        private static async Task<double> updateServerAsync_impl(Uri uri, UniqueServerInfo info, String key)
        {
            using CancellationTokenSource cs = new(timeOutMillis);
            var reqContent = new UpdateServerRequest(info.playerCount, info.maxPlayers, info.currentMap);
            var content = JsonContent.Create(reqContent, options: sOptions);
            content.Headers.Add("x-chiv2-server-browser-key", key);
            var httpResponse = await httpc.PutAsync(uri + $"/servers/{info.uniqueId}", content, cs.Token);
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
                    return res.refreshBefore;
                }
            }
            catch (InvalidOperationException e)
            {
                throw new InvalidDataException("Failed to parse response from server", e);
            }
        }

        private static async Task<double> heartbeatAsync_impl(Uri uri, UniqueServerInfo info, String key)
        {
            using CancellationTokenSource cs = new(timeOutMillis);
            //var reqContent = new UpdateServerRequest(info.playerCount, info.maxPlayers, info.currentMap);
            var content = new StringContent("");
            content.Headers.Add("x-chiv2-server-browser-key", key);
            var httpResponse = await httpc.PostAsync(uri + $"/servers/{info.uniqueId}/heartbeat", content, cs.Token);
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
                    return res.refreshBefore;
                }
            }
            catch (InvalidOperationException e)
            {
                throw new InvalidDataException("Failed to parse response from server", e);
            }
        }

        private static async Task deleteServerAsync_impl(Uri uri, UniqueServerInfo info, String key)
        {
            using CancellationTokenSource cs = new(timeOutMillis);
            //var reqContent = new UpdateServerRequest(info.playerCount, info.maxPlayers, info.currentMap);
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Delete, uri + $"/servers/{info.uniqueId}");
            request.Headers.Add("x-chiv2-server-browser-key", key);
            //the DeleteAsync method does not take a content parameter
            //that's a problem, since that's the only way to set headers
            //without setting state on the HttpClient
            (await httpc.SendAsync(request, cs.Token)).EnsureSuccessStatusCode();
        }
    }
}
