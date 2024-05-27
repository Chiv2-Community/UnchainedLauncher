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

namespace UnchainedLauncher.Core.API.ServerBrowser
{
    // TODO: This part of the API should be stabilized
    // as-is, there are numerous different kinds of "server" objects depending
    // on where they're coming from/where they're going
    public record ServerInfo {
        public Ports ports;
        public String name;
        public String description;
        public bool passwordProtected;
        public String currentMap;
        public int playerCount;
        public int maxPlayers;
        //TODO: Make this reference the latest version always
        public JsonModels.Metadata.V3.Mod[] mods;
    }
    public record UniqueServerInfo : ServerInfo {
        public String uniqueId;
        public float lastHeartbeat;
    };
    
    public record RegisterServerRequest : ServerInfo
    {
        public String localIpAddress;
        public static RegisterServerRequest from(ServerInfo info, String localIPAddress)
        {
            return new RegisterServerRequest()
            {
                ports = info.ports,
                name = info.name,
                description = info.description,
                passwordProtected = info.passwordProtected,
                currentMap = info.currentMap,
                playerCount = info.playerCount,
                maxPlayers = info.maxPlayers,
                mods = info.mods,
                localIpAddress = localIPAddress
            };
        }
    }

    public record ResponseServer : UniqueServerInfo
    {
        String localIpAddress;
        String ipAddress;
    }
    public record Ports(
        int game,
        int ping,
        int a2s
    );
    public record RegisterServerResponse(
        float refreshBefore,
        String key,
        ResponseServer server
    );
    public record UpdateServerRequest(
        int playerCount,
        int maxPlayers,
        String currentMap
    );

    public record UpdateServerResponse(float refreshBefore, ResponseServer server);

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
        public static async Task<UpdateServerResponse> updateServerAsync(Uri uri, UniqueServerInfo info, String key)
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

        public static async Task<UpdateServerResponse> heartbeatAsync(Uri uri, UniqueServerInfo info, String key)
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

        public static async Task<HttpResponseMessage> deleteServerAsync(Uri uri, UniqueServerInfo info, String key)
        {
            try
            {
                return await deleteServerAsync_impl(uri, info, key);
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException("Request timed out.");
            }
        }

        private static async Task<RegisterServerResponse> registerServerAsync_impl(Uri uri, String localIp, ServerInfo info)
        {
            using CancellationTokenSource cs = new(timeOutMillis);
            var reqContent = RegisterServerRequest.from(info, localIp);
            var content = JsonContent.Create(reqContent, options: sOptions);
            var httpResponse = await httpc.PostAsync(uri + "/servers", content, cs.Token);
            RegisterServerResponse? response = await httpResponse.EnsureSuccessStatusCode()
                            .Content
                            .ReadFromJsonAsync<RegisterServerResponse>(sOptions, cs.Token);
            if(response == null)
            {
                throw new InvalidDataException("Failed to parse response from server");
            }
            else
            {
                return response;
            }
        }

        private static async Task<UpdateServerResponse> updateServerAsync_impl(Uri uri, UniqueServerInfo info, String key)
        {
            using CancellationTokenSource cs = new(timeOutMillis);
            var reqContent = new UpdateServerRequest(info.playerCount, info.maxPlayers, info.currentMap);
            var content = JsonContent.Create(reqContent, options: sOptions);
            content.Headers.Add("x-chiv2-server-browser-key", key);
            var httpResponse = await httpc.PutAsync(uri + $"/servers/{info.uniqueId}", content, cs.Token);
            UpdateServerResponse? response = await httpResponse.EnsureSuccessStatusCode()
                            .Content
                            .ReadFromJsonAsync<UpdateServerResponse>(sOptions, cs.Token);
            if (response == null)
            {
                throw new InvalidDataException("Failed to parse response from server");
            }
            else
            {
                return response;
            }
        }

        private static async Task<UpdateServerResponse> heartbeatAsync_impl(Uri uri, UniqueServerInfo info, String key)
        {
            using CancellationTokenSource cs = new(timeOutMillis);
            //var reqContent = new UpdateServerRequest(info.playerCount, info.maxPlayers, info.currentMap);
            var content = new StringContent("");
            content.Headers.Add("x-chiv2-server-browser-key", key);
            var httpResponse = await httpc.PostAsync(uri + $"/servers/{info.uniqueId}/heartbeat", content, cs.Token);
            UpdateServerResponse? response = await httpResponse.EnsureSuccessStatusCode()
                            .Content
                            .ReadFromJsonAsync<UpdateServerResponse>(sOptions, cs.Token);
            if (response == null)
            {
                throw new InvalidDataException("Failed to parse response from server");
            }
            else
            {
                return response;
            }
        }

        private static async Task<HttpResponseMessage> deleteServerAsync_impl(Uri uri, UniqueServerInfo info, String key)
        {
            using CancellationTokenSource cs = new(timeOutMillis);
            //var reqContent = new UpdateServerRequest(info.playerCount, info.maxPlayers, info.currentMap);
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Delete, uri + $"/servers/{info.uniqueId}");
            request.Headers.Add("x-chiv2-server-browser-key", key);
            //the DeleteAsync method does not take a content parameter
            //that's a problem, since that's the only way to set headers
            //without setting state on the HttpClient
            return await httpc.SendAsync(request, cs.Token);
        }
    }
}
