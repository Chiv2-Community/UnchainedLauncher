using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab {

    public record SavedServerTemplate(
        ServerInfoFormData ServerInfo,
        ModManagerMetadata ModManagerMetadata
    );

    public class ServerTemplateVM {
        public ServerInfoFormVM Form { get; }
        public ModListVM ModList { get; }

        public ServerTemplateVM(ModListVM modList) {
            Form = new ServerInfoFormVM();
            ModList = modList;
        }

        public ServerTemplateVM(SavedServerTemplate saved, IModRegistry registry, IUserDialogueSpawner dialogueSpawner) {
            // TODO: create maps using mods selected is mod manager
            Form = new ServerInfoFormVM(data: saved.ServerInfo);

            ModList = new ModListVM(
                ModManagerCodec.ToClassType(saved.ModManagerMetadata, registry),
                dialogueSpawner
                );
        }

        public SavedServerTemplate Saved() {
            var savedTemplate = new SavedServerTemplate(
                Form.Data,
                ModManagerCodec.ToJsonType(ModList._modManager)
            );
            return savedTemplate;
        }
    }
}