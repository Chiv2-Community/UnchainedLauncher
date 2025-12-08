using log4net;
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
        List<ReleaseCoordinates>? EnabledModMarkerList
    );

    [AddINotifyPropertyChangedInterface]
    public partial class ServerTemplateVM {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(ServerTemplateVM));

        public ServerInfoFormVM Form { get; }
        public ObservableCollection<ReleaseCoordinates> EnabledModMarkerList { get; }
        public ObservableCollection<Release> AvailableMods { get; }

        public ServerTemplateVM(SavedServerTemplate saved, ObservableCollection<ReleaseCoordinates> enabledModList, IModManager modManager) {
            Form = new ServerInfoFormVM(data: saved.ServerInfo);
            EnabledModMarkerList = enabledModList;
            AvailableMods = new ObservableCollection<Release>(modManager.GetEnabledModReleases());

            modManager.ModDisabled += RemoveAvailableMod;
            modManager.ModEnabled += AddAvailableMod;
        }

        public void EnableModMarker(Release release) => EnabledModMarkerList.Add(ReleaseCoordinates.FromRelease(release));
        public void DisableModMarker(Release release) => EnabledModMarkerList.Remove(ReleaseCoordinates.FromRelease(release));


        public void AddAvailableMod(Release release, string? previousVersion) => AvailableMods.Add(release);
        public void RemoveAvailableMod(Release release) => AvailableMods.Remove(release);

        public SavedServerTemplate Saved() {
            var savedTemplate = new SavedServerTemplate(
                Form.Data,
                EnabledModMarkerList.ToList()
            );
            return savedTemplate;
        }
    }
}