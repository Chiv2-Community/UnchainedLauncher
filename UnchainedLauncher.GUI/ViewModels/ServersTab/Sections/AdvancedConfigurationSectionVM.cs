using LanguageExt;
using PropertyChanged;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.GUI.ViewModels.ServersTab.IniSections;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab.Sections {
    using static LanguageExt.Prelude;

    [AddINotifyPropertyChangedInterface]
    public class AdvancedConfigurationSectionVM {
        public AdvancedConfigurationSectionVM(
            IpNetDriverSectionVM ipNetDriver,
            TBLGameModeSectionVM gameMode,
            bool showInServerBrowser,
            int? playerBotCount,
            int? warmupTime,
            string additionalCLIArgs,
            string? discordBotToken = null,
            string? discordChannelId = null,
            string? discordAdminChannelId = null,
            string? discordGeneralChannelId = null,
            string? discordAdminRoleId = null
        ) {
            IpNetDriver = ipNetDriver;
            GameMode = gameMode;
            ShowInServerBrowser = showInServerBrowser;
            PlayerBotCount = playerBotCount;
            WarmupTime = warmupTime;
            AdditionalCLIArgs = additionalCLIArgs;
            DiscordBotToken = discordBotToken ?? "";
            DiscordChannelId = discordChannelId ?? "";
            DiscordAdminChannelId = discordAdminChannelId ?? "";
            DiscordGeneralChannelId = discordGeneralChannelId ?? "";
            DiscordAdminRoleId = discordAdminRoleId ?? "";
        }

        public IpNetDriverSectionVM IpNetDriver { get; }
        public TBLGameModeSectionVM GameMode { get; }

        public int? PlayerBotCount { get; set; }
        public int? WarmupTime { get; set; }
        public bool ShowInServerBrowser { get; set; }

        public string AdditionalCLIArgs { get; set; }

        // Discord Integration
        public string DiscordBotToken { get; set; }
        public string DiscordChannelId { get; set; }
        public string DiscordAdminChannelId { get; set; }
        public string DiscordGeneralChannelId { get; set; }
        public string DiscordAdminRoleId { get; set; }

        /// <summary>
        /// Returns true if the Discord configuration is incomplete (one required field is set but not the other).
        /// Both bot token and channel id must be defined together, or neither.
        /// </summary>
        public bool HasDiscordConfigWarning {
            get {
                var hasBotToken = !string.IsNullOrEmpty(DiscordBotToken?.Trim());
                var hasChannelId = !string.IsNullOrEmpty(DiscordChannelId?.Trim());
                return hasBotToken != hasChannelId;
            }
        }

        public string DiscordConfigWarningMessage =>
            HasDiscordConfigWarning
                ? "Both Discord Bot Token and Channel ID must be provided for Discord integration to work."
                : "";

        /// <summary>
        /// Returns the DiscordIntegrationLaunchOptions if both required fields are configured,
        /// otherwise returns None.
        /// </summary>
        public Option<DiscordIntegrationLaunchOptions> DiscordIntegration {
            get {
                var botToken = DiscordBotToken?.Trim();
                var channelId = DiscordChannelId?.Trim();

                if (string.IsNullOrEmpty(botToken) || string.IsNullOrEmpty(channelId))
                    return None;

                return Some(new DiscordIntegrationLaunchOptions(
                    botToken,
                    channelId,
                    Optional(DiscordAdminChannelId?.Trim()).Filter(s => s.Length > 0),
                    Optional(DiscordGeneralChannelId?.Trim()).Filter(s => s.Length > 0),
                    Optional(DiscordAdminRoleId?.Trim()).Filter(s => s.Length > 0)
                ));
            }
        }
    }
}