using StructuredINI;

namespace UnchainedLauncher.Core.INIModels.Engine;

[INISection("/Script/OnlineSubsystemUtils.IpNetDriver")]
public record IpNetDriver(
    int NetServerMaxTickRate = 60,
    int MaxClientRate = 100000,
    int MaxInternetClientRate = 100000,
    float InitialConnectTimeout = 60,
    float ConnectionTimeout = 60,
    int LanServerMaxTickRate = 60,
    float RelevantTimeout = 5,
    int SpawnPrioritySeconds = 1,
    float ServerTravelPause = 4
);