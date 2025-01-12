using UnchainedLauncher.Core.Services.Mods;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab {
    public class ServerTemplateVM {
        public ServerInfoFormVM Form { get; }
        public IModManager ModManager { get; }

        // TODO: serialization/deserialization for save/reload
        public ServerTemplateVM(IModManager modManager) {
            Form = new ServerInfoFormVM();
            ModManager = modManager;
        }

    }
}
