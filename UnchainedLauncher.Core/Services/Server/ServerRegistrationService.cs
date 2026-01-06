using log4net;
using PropertyChanged;
using System.ComponentModel;
using Unchained.ServerBrowser.Api;
using Unchained.ServerBrowser.Model;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Server.A2S;

namespace UnchainedLauncher.Core.Services.Server;

/// <summary>
/// Service that manages registering a server with the Server Browser backend, sending periodic heartbeats,
/// and updating the listing using A2S data via the generated HTTP client.
/// </summary>
[AddINotifyPropertyChangedInterface]
public class ServerRegistrationService(
    IDefaultApi api,
    IA2S a2S,
    ServerRegistrationOptions options,
    int updateIntervalMillis = 10000) {
    private static readonly ILog Logger = LogManager.GetLogger(nameof(ServerRegistrationService));

    public RegistrationState RegistrationState { get; private set; } = new AwaitingServerStartRegistrationState();

    private string LogPrefix => $"({options.Name}) ";
    
    private bool _stopped;
    private Task _lastIteration =  Task.CompletedTask;
    
    public async Task StartEventLoop()
    {
        RegistrationState = new AwaitingServerStartRegistrationState();
        _stopped = false;
        Logger.Info("Starting server registration loop");
        while (!_stopped) {
            _lastIteration = EventLoop(RegistrationState);
            await Task.Delay(updateIntervalMillis);
        }
        Logger.Info("Server registration loop stopped");
        
        RegistrationState = new StoppedState(RegistrationState.LastException);
    }
    
    public Task StopEventLoop() {
        Logger.Info("Stopping server registration loop");
        _stopped = true;
        return _lastIteration;
    }

    private async Task EventLoop(RegistrationState lastState) {
        switch (RegistrationState) {
            case AwaitingServerStartRegistrationState {}:
                try {
                    var a2sInfo = await a2S.InfoAsync();
                    RegistrationState = new PendingRegistrationState(
                        a2sInfo.Map,
                        a2sInfo.Players,
                        a2sInfo.MaxPlayers
                    );

                    EventLoop(RegistrationState); // Fast-followup call of the EventLoop, because it is time to register.
                }
                catch (Exception ex) {
                    Logger.Info($"Server still starting up. Trying again...");
                    Logger.Debug($"This error is expected within 5 minutes of starting the server", ex);
                    RegistrationState = RegistrationState with { LastException = ex };
                }
                break;
            case PendingRegistrationState { CurrentMap: var currentMap, Players: var players, MaxPlayers: var maxPlayers}:
                try {
                    var registrationResult = await Register(BuildRegistrationRequest(currentMap, players, maxPlayers));
                    RegistrationState = new ActiveRegistrationState(
                        registrationResult.Server.UniqueId,
                        registrationResult.Key,
                        currentMap,
                        players,
                        maxPlayers,
                        registrationResult.RefreshBefore,
                        RegistrationState.LastException
                    );
                }
                catch(Exception ex) {
                    Logger.Error($"Failed to register server: {ex.Message}");
                    RegistrationState = RegistrationState with { LastException = ex };
                }

                break;
            case ActiveRegistrationState { UniqueId: var uniqueId, RegistrationKey: var registrationKey, CurrentMap: var currentMap, Players: var players, MaxPlayers: var maxPlayers, RefreshBeforeTimestamp: var refreshBefore}:
                try {
                    var a2sInfo = await a2S.InfoAsync();
                    if (a2sInfo.Map != currentMap || a2sInfo.Players != players || a2sInfo.MaxPlayers != maxPlayers) {
                        var updateResult = await Update(uniqueId, registrationKey, new UpdateRegisteredServer(a2sInfo.Players, a2sInfo.MaxPlayers, a2sInfo.Map));

                        if (!updateResult) {
                            Logger.Error($"Failed to update server listing. Assuming Registration was lost.");
                            RegistrationState = new AwaitingServerStartRegistrationState(RegistrationState.LastException);
                            break;
                        }
                        

                        RegistrationState = ((RegistrationState as ActiveRegistrationState)!) with {
                            CurrentMap = a2sInfo.Map, Players = a2sInfo.Players, MaxPlayers = a2sInfo.MaxPlayers
                        };
                    }
                    
                    // Subtract refreshBefore timestamp (double in seconds) from the current timestamp
                    var remainingTimeTilExpiration = 
                        refreshBefore - DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                    if (remainingTimeTilExpiration < updateIntervalMillis * 3) {
                        var heartbeatResult = await Heartbeat(uniqueId, registrationKey);
                        if (heartbeatResult.HasValue) {
                            RegistrationState = ((RegistrationState as ActiveRegistrationState)!) with {
                                RefreshBeforeTimestamp = heartbeatResult.Value
                            };
                            
                            remainingTimeTilExpiration = heartbeatResult.Value - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                            Logger.Debug($"{LogPrefix}Heartbeat sent successfully. Next update in {remainingTimeTilExpiration} seconds.");
                        }
                        else {
                            Logger.Error($"{LogPrefix}Failed to send heartbeat. Assuming Registration was lost.");
                            RegistrationState = new AwaitingServerStartRegistrationState(RegistrationState.LastException);
                        }
                    }
                }
                catch (Exception ex) {
                    Logger.Error($"Failed to update server listing. Assuming Registration was lost.", ex);
                    RegistrationState = new AwaitingServerStartRegistrationState(ex);
                }
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private async Task<RegistrationResponse> Register(ServerRegistrationRequest req)
    {
        Logger.Debug("Sending registration request to backend API");
        var apiRes = await api.ApiV1ServersPostAsync(req);
        var model = apiRes.Created();
        if (model == null)
        {
            Logger.Error($"Server registration returned a {apiRes.StatusCode} response: {apiRes}");
            throw new InvalidOperationException("Server registration returned a non-OK response");
        }
        
        Logger.Info($"Successfully registered server with ID {model.Server.UniqueId}");
        return model;
    }

    private async Task<bool> Update(string id, string key, UpdateRegisteredServer req)
    {
        try {
            Logger.Debug("Sending update request to backend API");
            var res = await api.ApiV1ServersUniqueIdPutAsync(key, id, req);
            var model = res.Ok();
            if (model == null) {
                Logger.Error($"Server update returned a {res.StatusCode} response: {res}");
                return false;
            }
            
            Logger.Info($"Successfully updated server listing");
            return true;
        }
        catch(Exception ex) {
            Logger.Error($"Failed to update server listing: {ex.Message}");
            return false;
        }
    }

    public async Task<double?> Heartbeat(string id, string key) {
        try {
            Logger.Debug("Sending heartbeat request to backend API");
            var res = await api.ApiV1ServersUniqueIdHeartbeatPostAsync(key, id);
            var model = res.Ok();
            if (model == null) {
                Logger.Error($"Heartbeat returned a {res.StatusCode} response: {res}");
                return null;
            }
            
            Logger.Info($"Successfully sent heartbeat");
            return model!.RefreshBefore;
        } catch(Exception ex) {
            Logger.Error($"Failed to send heartbeat: {ex.Message}");
            return null;
        }
    }

    private ServerRegistrationRequest BuildRegistrationRequest(String currentMap, int players, int maxPlayers) {
        var mods =
            options.Mods
                .Select(mod => new Mod(mod.ModuleName, mod.Org, mod.Version))
                .ToList();
        return new ServerRegistrationRequest(
            ports: options.Ports,
            name: options.Name,
            description: options.Description,
            currentMap: currentMap,
            playerCount: players,
            maxPlayers: maxPlayers,
            passwordProtected: options.PasswordProtected,
            mods: mods,
            localIpAddress: options.LocalIp
        );
    }
}

/// <summary>
/// Minimal server configuration needed to register with the Server Browser backend.
/// Replaces the old C2ServerInfo dependency that lived under Core.API.ServerBrowser.
/// </summary>
public record ServerRegistrationOptions(
    string Name,
    string Description,
    bool PasswordProtected,
    Chivalry2Ports Ports,
    string LocalIp,
    IReadOnlyList<ReleaseCoordinates> Mods
);

public abstract record RegistrationState(Exception? LastException = null);
public record AwaitingServerStartRegistrationState(Exception? ex = null) : RegistrationState(ex);
public record PendingRegistrationState(string CurrentMap, int Players, int MaxPlayers, Exception? ex = null): RegistrationState(ex);
public record ActiveRegistrationState(
    string UniqueId,
    string RegistrationKey,
    string CurrentMap,
    int Players,
    int MaxPlayers,
    double RefreshBeforeTimestamp,
    Exception? ex = null
): RegistrationState(ex);
public record StoppedState(Exception? ex = null) : RegistrationState(ex);