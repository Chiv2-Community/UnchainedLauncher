using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.GUI.ViewModels.Registry;
using UnchainedLauncher.GUI.Views.Registry;

namespace UnchainedLauncher.GUI.Services {
    public interface IRegistryWindowService {
        void ShowAllRegistriesWindow(RegistryWindowVM registryWindowVM);
        RegistryReleaseFormVM PromptAddRelease(LocalModRegistry registry, RegistryReleaseFormVM registryReleaseFormVM);
    }

    public class RegistryWindowService : IRegistryWindowService {
        private RegistryWindow? _allRegistriesWindow;

        public void ShowAllRegistriesWindow(RegistryWindowVM registryWindowVM) {
            if (_allRegistriesWindow == null) {
                _allRegistriesWindow = new RegistryWindow(registryWindowVM);
                _allRegistriesWindow.Closed += (_, __) => _allRegistriesWindow = null;
                _allRegistriesWindow.Show();
            }
            else {
                _allRegistriesWindow.Activate();
            }
        }

        public RegistryReleaseFormVM PromptAddRelease(LocalModRegistry registry, RegistryReleaseFormVM registryReleaseFormVM) {
            var editWindow = new RegistryReleaseFormWindow();
            editWindow.DataContext = registryReleaseFormVM;
            registryReleaseFormVM.OnSubmit += editWindow.Close;
            registryReleaseFormVM.OnSubmit += async () => {
                var (res, p) = registryReleaseFormVM.ToRelease();
                await registry.AddRelease(
                    res,
                    p
                );
            };
            editWindow.Show();
            return registryReleaseFormVM;
        }
    }
}