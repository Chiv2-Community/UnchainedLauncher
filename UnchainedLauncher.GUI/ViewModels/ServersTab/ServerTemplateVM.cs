using UnchainedLauncher.Core.Services.Mods;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab {

    public record SavedServerTemplate(
        ServerInfoFormData ServerInfo
    // TODO: add whatever is required to recreate the mod manager state here
    );

    public class ServerTemplateVM {
        public ServerInfoFormVM Form { get; }
        public IModManager ModManager { get; }

        // TODO: serialization/deserialization for save/reload
        public ServerTemplateVM(IModManager modManager) {
            Form = new ServerInfoFormVM();
            ModManager = modManager;
        }

        public ServerTemplateVM(SavedServerTemplate saved, IModManager modManager) {
            // TODO: create maps using mods selected is mod manager
            Form = new ServerInfoFormVM(data: saved.ServerInfo);
            ModManager = modManager;
        }

        public SavedServerTemplate Saved() {
            return new SavedServerTemplate(Form.Data);
        }
    }
}