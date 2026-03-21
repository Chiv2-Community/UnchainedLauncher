using PropertyChanged;
using UnchainedLauncher.Core.INIModels.Game;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab.IniSections {
    [AddINotifyPropertyChangedInterface]
    public class GameSessionSectionVM {
        public int MaxPlayers { get; set; }

        public void LoadFrom(GameSession model, bool desyncPatchEnabled) {
            var extraPlayers = desyncPatchEnabled ? 1 : 0;
            MaxPlayers = model.MaxPlayers - extraPlayers;
        }

        public GameSession ToModel(bool desyncPatchEnabled) {
            var extraPlayers = desyncPatchEnabled ? 1 : 0;
            return new GameSession(MaxPlayers + extraPlayers);
        }
    }
}