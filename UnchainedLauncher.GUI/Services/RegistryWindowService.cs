using Microsoft.Win32;
using System;
using System.Collections.Generic;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.GUI.ViewModels.Registry;
using UnchainedLauncher.GUI.Views.Registry;

namespace UnchainedLauncher.GUI.Services {
    public interface IRegistryWindowService {
        void ShowAllRegistriesWindow(RegistryWindowVM registryWindowVM);
        void ShowLocalRegistryWindow(LocalModRegistryWindowVM registryWindowVM);
        RegistryReleaseFormVM PromptAddRelease(LocalModRegistry registry, RegistryReleaseFormVM registryReleaseFormVM);
    }
    
    public class RegistryWindowService: IRegistryWindowService {
        private LocalModRegistryWindow? _localModRegistryWindow;
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

        public void ShowLocalRegistryWindow(LocalModRegistryWindowVM registryWindowVM) { 
            if (_localModRegistryWindow == null) {
                _localModRegistryWindow = new LocalModRegistryWindow(registryWindowVM);
                _localModRegistryWindow.Closed += (_, __) => _localModRegistryWindow = null;
                _localModRegistryWindow.Show();
            } else {
                _localModRegistryWindow.DataContext = registryWindowVM;
                _localModRegistryWindow.Activate();
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