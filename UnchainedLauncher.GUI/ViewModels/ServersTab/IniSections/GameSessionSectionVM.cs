using PropertyChanged;
using UnchainedLauncher.Core.INIModels.Game;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab.IniSections {
    [AddINotifyPropertyChangedInterface]
    public class GameSessionSectionVM {
        public int MaxPlayers { get; set; }

        public void LoadFrom(GameSession model) {
            MaxPlayers = model.MaxPlayers;
        }

        public GameSession ToModel() => new(MaxPlayers);
    }
}
