using CommunityToolkit.Mvvm.Input;
using PropertyChanged;
using StructuredINI.Codecs;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using UnchainedLauncher.Core.INIModels.Game;
using UnchainedLauncher.GUI.ViewModels.ServersTab.Sections;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab.IniSections {
    [AddINotifyPropertyChangedInterface]
    public partial class TBLGameModeSectionVM {
        public string ServerName { get; set; } = "";
        public string ServerIdentifier { get; set; } = "";

        public bool BotBackfillEnabled { get; set; }
        public int BotBackfillLowPlayers { get; set; }
        public int BotBackfillLowBots { get; set; }
        public int BotBackfillHighPlayers { get; set; }
        public int BotBackfillHighBots { get; set; }

        public float MinTimeBeforeStartingMatch { get; set; }
        public float MaxTimeBeforeStartingMatch { get; set; }
        public int IdleKickTimerSpectate { get; set; }
        public int IdleKickTimerDisconnect { get; set; }

        public ObservableCollection<string> MapList { get; } = new();
        public string? MapToAdd { get; set; }
        public int MapListIndex { get; set; }

        public bool HorseCompatibleServer { get; set; }
        
        public ObservableCollection<BalanceSectionVM.ClassLimitVM> ClassLimits { get; } = new();
        public ObservableCollection<BalanceSectionVM.AutoBalanceVM> TeamBalanceOptions { get; } = new();
        public ObservableCollection<BalanceSectionVM.AutoBalanceVM> AutoBalanceOptions { get; } = new();
        public int StartOfMatchGracePeriodForAutoBalance { get; set; }
        public int StartOfMatchGracePeriodForTeamSwitching { get; set; }
        public bool UseStrictTeamBalanceEnforcement { get; set; }

        public int DefaultMaxPlayers { get; set; }

        public bool AutoBalanceEnabled { get; set; }
        public bool TeamBalanceEnabled { get; set; }


        public TBLGameModeSectionVM() {
        }

        private void EnsureMapListIndexValid() {
            if (MapList.Count == 0) {
                MapListIndex = -1;
                return;
            }

            if (MapListIndex < 0) {
                MapListIndex = 0;
                return;
            }

            if (MapListIndex >= MapList.Count) {
                MapListIndex = MapList.Count - 1;
            }
        }

        [RelayCommand]
        private void AddMap() {
            if (string.IsNullOrWhiteSpace(MapToAdd)) return;
            var map = MapToAdd.Trim();
            MapList.Add(map);
            EnsureMapListIndexValid();
            MapToAdd = null;
        }

        [RelayCommand]
        private void RemoveMap(string? map) {
            if (string.IsNullOrWhiteSpace(map)) return;
            var idx = MapList.IndexOf(map);
            if (idx < 0) return;

            MapList.RemoveAt(idx);

            if (MapList.Count == 0) {
                MapListIndex = -1;
                return;
            }

            if (idx < MapListIndex) {
                MapListIndex = Math.Max(MapListIndex - 1, 0);
                return;
            }

            EnsureMapListIndexValid();
        }
        
        public void LoadFrom(TBLGameMode model) {
            ServerName = model.ServerName;
            ServerIdentifier = model.ServerIdentifier;
            BotBackfillEnabled = model.BotBackfillEnabled;
            BotBackfillLowPlayers = model.BotBackfillLowPlayers;
            BotBackfillLowBots = model.BotBackfillLowBots;
            BotBackfillHighPlayers = model.BotBackfillHighPlayers;
            BotBackfillHighBots = model.BotBackfillHighBots;
            MinTimeBeforeStartingMatch = model.MinTimeBeforeStartingMatch;
            MaxTimeBeforeStartingMatch = model.MaxTimeBeforeStartingMatch;
            IdleKickTimerSpectate = model.IdleKickTimerSpectate;
            IdleKickTimerDisconnect = model.IdleKickTimerDisconnect;

            MapList.Clear();
            foreach (var map in model.MapList) {
                MapList.Add(map);
            }

            MapListIndex = model.MapListIndex;
            EnsureMapListIndexValid();
            HorseCompatibleServer = model.bHorseCompatibleServer;

            ClassLimits.Clear();
            foreach (var limit in model.ClassLimits) {
                ClassLimits.Add(new BalanceSectionVM.ClassLimitVM(limit));
            }

            TeamBalanceOptions.Clear();
            if (model.TeamBalanceOptions.Length == 0) {
                TeamBalanceOptions.Add(new BalanceSectionVM.AutoBalanceVM());
            }

            foreach (var opt in model.TeamBalanceOptions) {
                TeamBalanceOptions.Add(new BalanceSectionVM.AutoBalanceVM(opt));
            }

            AutoBalanceOptions.Clear();
            if (model.AutoBalanceOptions.Length == 0) {
                AutoBalanceOptions.Add(new BalanceSectionVM.AutoBalanceVM(new AutoBalance(0, DefaultMaxPlayers, 8)));
            }
            foreach (var opt in model.AutoBalanceOptions) {
                AutoBalanceOptions.Add(new BalanceSectionVM.AutoBalanceVM(opt));
            }

            StartOfMatchGracePeriodForAutoBalance = model.StartOfMatchGracePeriodForAutoBalance;
            StartOfMatchGracePeriodForTeamSwitching = model.StartOfMatchGracePeriodForTeamSwitching;
            UseStrictTeamBalanceEnforcement = model.bUseStrictTeamBalanceEnforcement;
        }

        public TBLGameMode ToModel() => new(
            ServerName,
            ServerIdentifier,
            BotBackfillEnabled,
            BotBackfillLowPlayers,
            BotBackfillLowBots,
            BotBackfillHighPlayers,
            BotBackfillHighBots,
            MinTimeBeforeStartingMatch,
            MaxTimeBeforeStartingMatch,
            IdleKickTimerSpectate,
            IdleKickTimerDisconnect,
            MapList.ToArray(),
            MapListIndex,
            HorseCompatibleServer,
            ClassLimits.Select(l => l.ToModel()).ToArray(),
            TeamBalanceOptions.Select(o => o.ToModel()).ToArray(),
            AutoBalanceOptions.Select(o => o.ToModel()).ToArray(),
            StartOfMatchGracePeriodForAutoBalance,
            StartOfMatchGracePeriodForTeamSwitching,
            UseStrictTeamBalanceEnforcement
        );
    }
}