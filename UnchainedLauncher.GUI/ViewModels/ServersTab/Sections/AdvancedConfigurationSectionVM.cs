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
            string? discordAdminChannelId = null,
            string? discordGeneralChannelId = null,
            string? discordDashboardChannelId = null,
            string? discordEventLogChannelId = null,
            string? discordAdminRoleId = null,
            bool discordMentionAdmins = true,
            bool useBackendBanlist = true
        ) {
            IpNetDriver = ipNetDriver;
            GameMode = gameMode;
            ShowInServerBrowser = showInServerBrowser;
            PlayerBotCount = playerBotCount;
            WarmupTime = warmupTime;
            AdditionalCLIArgs = additionalCLIArgs;
            UseBackendBanlist = useBackendBanlist;
            DiscordBotToken = discordBotToken ?? "";
            DiscordAdminChannelId = discordAdminChannelId ?? "";
            DiscordGeneralChannelId = discordGeneralChannelId ?? "";
            DiscordDashboardChannelId = discordDashboardChannelId ?? "";
            DiscordEventLogChannelId = discordEventLogChannelId ?? "";
            DiscordAdminRoleId = discordAdminRoleId ?? "";
            DiscordMentionAdmins = discordMentionAdmins;
        }

        public IpNetDriverSectionVM IpNetDriver { get; }
        public TBLGameModeSectionVM GameMode { get; }

        public int? PlayerBotCount { get; set; }
        public int? WarmupTime { get; set; }
        public bool ShowInServerBrowser { get; set; }
        public bool UseBackendBanlist { get; set; }

        public string AdditionalCLIArgs { get; set; }

        // Discord Integration
        public string DiscordBotToken { get; set; }
        public string DiscordAdminChannelId { get; set; }
        public string DiscordGeneralChannelId { get; set; }
        public string DiscordDashboardChannelId { get; set; }
        public string DiscordEventLogChannelId { get; set; }
        public string DiscordAdminRoleId { get; set; }
        public bool DiscordMentionAdmins { get; set; }

        /// <summary>
        /// Returns true if the Discord configuration is incomplete.
        /// Both bot token and at least one channel id must be defined together if any Discord field has a value.
        /// </summary>
        public bool HasDiscordConfigWarning {
            get {
                var hasBotToken = !string.IsNullOrEmpty(DiscordBotToken?.Trim());
                var hasAdminChannelId = !string.IsNullOrEmpty(DiscordAdminChannelId?.Trim());
                var hasGeneralChannelId = !string.IsNullOrEmpty(DiscordGeneralChannelId?.Trim());
                var hasDashboardChannelId = !string.IsNullOrEmpty(DiscordDashboardChannelId?.Trim());
                var hasEventLogChannelId = !string.IsNullOrEmpty(DiscordEventLogChannelId?.Trim());
                var hasAdminRoleId = !string.IsNullOrEmpty(DiscordAdminRoleId?.Trim());

                var hasAnyDiscordField = hasBotToken || hasAdminChannelId || hasGeneralChannelId || hasDashboardChannelId || hasEventLogChannelId || hasAdminRoleId;
                var hasAtLeastOneChannelId = hasAdminChannelId || hasGeneralChannelId || hasDashboardChannelId || hasEventLogChannelId;
                var hasRequiredFields = hasBotToken && hasAtLeastOneChannelId;

                return hasAnyDiscordField && !hasRequiredFields;
            }
        }

        public string DiscordConfigWarningMessage =>
            HasDiscordConfigWarning
                ? "Both Discord Bot Token and at least one channel ID must be provided for Discord integration to work."
                : "";

        /// <summary>
        /// Returns the DiscordIntegrationLaunchOptions if both required fields are configured,
        /// otherwise returns None.
        /// </summary>
        public Option<DiscordIntegrationLaunchOptions> DiscordIntegration {
            get {
                var botToken = DiscordBotToken?.Trim();
                var adminChannelId = DiscordAdminChannelId?.Trim();
                var generalChannelId = DiscordGeneralChannelId?.Trim();
                var dashboardChannelId = DiscordDashboardChannelId?.Trim();
                var eventLogChannelId = DiscordEventLogChannelId?.Trim();

                if (string.IsNullOrEmpty(botToken) || (string.IsNullOrEmpty(adminChannelId) && string.IsNullOrEmpty(generalChannelId) && string.IsNullOrEmpty(dashboardChannelId) && string.IsNullOrEmpty(eventLogChannelId)))
                    return None;

                return Some(new DiscordIntegrationLaunchOptions(
                    botToken,
                    Optional(adminChannelId).Filter(s => s.Length > 0),
                    Optional(generalChannelId).Filter(s => s.Length > 0),
                    Optional(dashboardChannelId).Filter(s => s.Length > 0),
                    Optional(eventLogChannelId).Filter(s => s.Length > 0),
                    Optional(DiscordAdminRoleId?.Trim()).Filter(s => s.Length > 0),
                    DiscordMentionAdmins
                ));
            }
        }
    }
}