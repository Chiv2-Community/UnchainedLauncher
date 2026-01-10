using CommunityToolkit.Mvvm.Input;
using PropertyChanged;
using StructuredINI.Codecs;
using System;
using System.Linq;
using UnchainedLauncher.GUI.ViewModels.ServersTab.IniSections;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab.Sections {
    [AddINotifyPropertyChangedInterface]
    public partial class BalanceSectionVM {
        public BalanceSectionVM(TBLGameModeSectionVM gameMode) {
            GameMode = gameMode;
        }

        public TBLGameModeSectionVM GameMode { get; }

        public int StartOfMatchGracePeriodForAutoBalance {
            get => GameMode.StartOfMatchGracePeriodForAutoBalance;
            set => GameMode.StartOfMatchGracePeriodForAutoBalance = value;
        }

        public int StartOfMatchGracePeriodForTeamSwitching {
            get => GameMode.StartOfMatchGracePeriodForTeamSwitching;
            set => GameMode.StartOfMatchGracePeriodForTeamSwitching = value;
        }

        public bool AutoBalanceEnabled {
            get => GameMode.AutoBalanceEnabled;
            set => GameMode.AutoBalanceEnabled = value;
        }

        public bool TeamBalanceEnabled {
            get => GameMode.TeamBalanceEnabled;
            set => GameMode.TeamBalanceEnabled = value;
        }

        public bool UseStrictTeamBalanceEnforcement {
            get => GameMode.UseStrictTeamBalanceEnforcement;
            set => GameMode.UseStrictTeamBalanceEnforcement = value;
        }


        public Array CharacterClassValues { get; } = Enum.GetValues(typeof(CharacterClass));

        public CharacterClass? ClassLimitClassToAdd { get; set; }
        public int? ClassLimitPercentToAdd { get; set; }

        public int? AutoBalanceMinToAdd { get; set; }
        public int? AutoBalanceMaxToAdd { get; set; }
        public int? AutoBalanceDiffToAdd { get; set; }

        public int? TeamBalanceMinToAdd { get; set; }
        public int? TeamBalanceMaxToAdd { get; set; }
        public int? TeamBalanceDiffToAdd { get; set; }

        [RelayCommand]
        private void AddClassLimit() {
            if (ClassLimitClassToAdd == null) return;
            if (ClassLimitPercentToAdd == null) return;

            var percent = ClassLimitPercentToAdd.Value;
            if (percent < 0 || percent > 100) return;

            var classToAdd = ClassLimitClassToAdd.Value;

            var existing = GameMode.ClassLimits.FirstOrDefault(l => l.Class == classToAdd);
            if (existing != null) {
                existing.ClassLimitPercent = percent;
                return;
            }

            GameMode.ClassLimits.Add(new ClassLimitVM {
                Class = classToAdd,
                ClassLimitPercent = percent
            });
        }

        [RelayCommand]
        private void RemoveClassLimit(ClassLimitVM? limit) {
            if (limit == null) return;

            if (GameMode.ClassLimits.Remove(limit)) return;

            var byClass = GameMode.ClassLimits.FirstOrDefault(l => l.Class == limit.Class);
            if (byClass != null) {
                GameMode.ClassLimits.Remove(byClass);
            }
        }

        [RelayCommand]
        private void AddAutoBalanceOption() {
            if (AutoBalanceMinToAdd == null || AutoBalanceMaxToAdd == null || AutoBalanceDiffToAdd == null) return;

            GameMode.AutoBalanceOptions.Add(new AutoBalanceVM {
                MinNumPlayers = AutoBalanceMinToAdd.Value,
                MaxNumPlayers = AutoBalanceMaxToAdd.Value,
                AllowedNumPlayersDifference = AutoBalanceDiffToAdd.Value
            });

            AutoBalanceMinToAdd = null;
            AutoBalanceMaxToAdd = null;
            AutoBalanceDiffToAdd = null;
        }

        [RelayCommand]
        private void RemoveAutoBalanceOption(AutoBalanceVM? option) {
            if (option == null) return;
            GameMode.AutoBalanceOptions.Remove(option);
        }

        [RelayCommand]
        private void AddTeamBalanceOption() {
            if (TeamBalanceMinToAdd == null || TeamBalanceMaxToAdd == null || TeamBalanceDiffToAdd == null) return;

            GameMode.TeamBalanceOptions.Add(new AutoBalanceVM {
                MinNumPlayers = TeamBalanceMinToAdd.Value,
                MaxNumPlayers = TeamBalanceMaxToAdd.Value,
                AllowedNumPlayersDifference = TeamBalanceDiffToAdd.Value
            });

            TeamBalanceMinToAdd = null;
            TeamBalanceMaxToAdd = null;
            TeamBalanceDiffToAdd = null;
        }

        [RelayCommand]
        private void RemoveTeamBalanceOption(AutoBalanceVM? option) {
            if (option == null) return;
            GameMode.TeamBalanceOptions.Remove(option);
        }

        [AddINotifyPropertyChangedInterface]
        public class AutoBalanceVM {
            public int MinNumPlayers { get; set; }
            public int MaxNumPlayers { get; set; }
            public int AllowedNumPlayersDifference { get; set; }

            public AutoBalanceVM() { }

            public AutoBalanceVM(AutoBalance ab) {
                MinNumPlayers = ab.MinNumPlayers;
                MaxNumPlayers = ab.MaxNumPlayers;
                AllowedNumPlayersDifference = ab.AllowedNumPlayersDifference;
            }

            public AutoBalance ToModel() => new(MinNumPlayers, MaxNumPlayers, AllowedNumPlayersDifference);
        }

        [AddINotifyPropertyChangedInterface]
        public class ClassLimitVM {
            public CharacterClass Class { get; set; }
            public int ClassLimitPercent { get; set; }

            public ClassLimitVM() { }

            public ClassLimitVM(ClassLimit model) {
                Class = model.Class;
                ClassLimitPercent = (int)(model.ClassLimitPercent * 100);
            }

            public ClassLimit ToModel() => new(Class, ClassLimitPercent * 0.01m);
        }

    }
}