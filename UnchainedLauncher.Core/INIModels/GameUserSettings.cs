using StructuredINI;
using StructuredINI.Codecs;

namespace UnchainedLauncher.Core.INIModels.GameUserSettings;

[INISection("/Script/TBL.TBLGameUserSettings")]
public record TBLGameUserSettings(
    int MaxFPS = 80,
    float FrameRateLimit = 80
);