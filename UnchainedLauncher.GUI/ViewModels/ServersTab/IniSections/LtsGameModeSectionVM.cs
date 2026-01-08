using PropertyChanged;
using UnchainedLauncher.Core.INIModels.Game;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab.IniSections {
    [AddINotifyPropertyChangedInterface]
    public class LtsGameModeSectionVM {
        public int PreCountdownDelay { get; set; }
        public int RoundsToWin { get; set; }

        public void LoadFrom(LTSGameMode model) {
            PreCountdownDelay = model.PreCountdownDelay;
            RoundsToWin = ToRoundsToWin(model.Rounds);
        }

        public LTSGameMode ToModel() => new(PreCountdownDelay, FromRoundsToWin(RoundsToWin));

        private static int ToRoundsToWin(int rounds) => (rounds + 1) / 2;

        private static int FromRoundsToWin(int roundsToWin) {
            if (roundsToWin < 1) roundsToWin = 1;
            return (2 * roundsToWin) - 1;
        }
    }
}