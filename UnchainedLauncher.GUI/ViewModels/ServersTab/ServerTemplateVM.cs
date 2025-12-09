using PropertyChanged;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab {

    public record SavedServerTemplate(
        ServerInfoFormData ServerInfo,
        List<ReleaseCoordinates>? EnabledServerModList
    );

    [AddINotifyPropertyChangedInterface]
    public partial class ServerTemplateVM {
        public ServerInfoFormVM Form { get; }
        public ObservableCollection<ReleaseCoordinates> EnabledServerModList { get; }
        public ObservableCollection<Release> AvailableMods { get; }

        public ServerTemplateVM(SavedServerTemplate saved, ObservableCollection<ReleaseCoordinates> enabledServerModList, IModManager modManager) {
            Form = new ServerInfoFormVM(data: saved.ServerInfo);
            EnabledServerModList = enabledServerModList;
            AvailableMods = new ObservableCollection<Release>(modManager.GetEnabledModReleases());

            modManager.ModDisabled += RemoveAvailableMod;
            modManager.ModEnabled += AddAvailableMod;
        }

        public void EnableServerMod(Release release) => EnabledServerModList.Add(ReleaseCoordinates.FromRelease(release));
        public void DisableServerMod(Release release) => EnabledServerModList.Remove(ReleaseCoordinates.FromRelease(release));


        public void AddAvailableMod(Release release, string? previousVersion) => AvailableMods.Add(release);
        public void RemoveAvailableMod(Release release) => AvailableMods.Remove(release);

        public SavedServerTemplate Saved() {
            var savedTemplate = new SavedServerTemplate(
                Form.Data,
                EnabledServerModList.ToList()
            );
            return savedTemplate;
        }
    }
}