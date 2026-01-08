using PropertyChanged;
using UnchainedLauncher.Core.INIModels.Game;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab.IniSections {
    [AddINotifyPropertyChangedInterface]
    public class ArenaGameModeSectionVM {
        public int RoundsToWin { get; set; }
        public int RoundTimeLimit { get; set; }
        public bool ClearWeaponsPostRound { get; set; }
        public bool ClearHorsesPostRound { get; set; }
        public bool ResetTaggedActorsPostRound { get; set; }
        public bool UsePreCountdownForCustomizationLoading { get; set; }
        public int TimeBetweenRounds { get; set; }
        public int TeamLives { get; set; }

        public void LoadFrom(ArenaGameMode model) {
            RoundsToWin = ToRoundsToWin(model.Rounds);
            RoundTimeLimit = model.RoundTimeLimit;
            ClearWeaponsPostRound = model.bClearWeaponsPostRound;
            ClearHorsesPostRound = model.bClearHorsesPostRound;
            ResetTaggedActorsPostRound = model.bResetTaggedActorsPostRound;
            UsePreCountdownForCustomizationLoading = model.bUsePreCountdownForCustomizationLoading;
            TimeBetweenRounds = model.TimeBetweenRounds;
            TeamLives = model.TeamLives;
        }

        public ArenaGameMode ToModel() => new(
            FromRoundsToWin(RoundsToWin),
            RoundTimeLimit,
            ClearWeaponsPostRound,
            ClearHorsesPostRound,
            ResetTaggedActorsPostRound,
            UsePreCountdownForCustomizationLoading,
            TimeBetweenRounds,
            TeamLives
        );

        private static int ToRoundsToWin(int rounds) => (rounds + 1) / 2;

        private static int FromRoundsToWin(int roundsToWin) {
            if (roundsToWin < 1) roundsToWin = 1;
            return (2 * roundsToWin) - 1;
        }
    }
}