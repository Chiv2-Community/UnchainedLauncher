using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.GUI.ViewModels.Registry;
using UnchainedLauncher.GUI.Views.Registry;

namespace UnchainedLauncher.GUI.Services {
    public interface ILocalModRegistryWindowService {
        LocalModRegistry Registry { get; }
        void Show();
        RegistryReleaseFormVM PromptAddRelease();
        RegistryReleaseFormVM PromptAddRelease(Release newRelease, string? previousPakPath = null);
    }
    public class LocalModRegistryWindowService: ILocalModRegistryWindowService {
        private LocalModRegistryWindow? _window;
        
        public LocalModRegistry Registry { get; }

        public LocalModRegistryWindowService(LocalModRegistry registry) {
            Registry = registry;
        }

        public void Show() {
            if (_window == null) {
                _window = new LocalModRegistryWindow(new LocalModRegistryWindowVM(this));
                _window.Closed += (_, __) => _window = null;
                _window.Show();
            } else {
                _window.DataContext = new LocalModRegistryWindowVM(this);
                _window.Activate();
            }
        }
        
        public RegistryReleaseFormVM PromptAddRelease(Release newRelease, string? previousPakPath = null) {
            var editableForm = new RegistryReleaseFormVM(newRelease, previousPakPath);
            var editWindow = new RegistryReleaseFormWindow();
            editWindow.DataContext = editableForm;
            editableForm.OnSubmit += editWindow.Close;
            editableForm.OnSubmit += async () => {
                var (res, p) = editableForm.ToRelease();
                await Registry.AddRelease(
                    res,
                    p
                );
            };
            editWindow.Show();
            return editableForm;
        }

        public RegistryReleaseFormVM PromptAddRelease() {
            var dummyRelease = new Release(
                "v0.0.0",
                "",
                "unknown",
                DateTime.Now,
                new ModManifest(
                    "Unchained-Mods/Example-Mod",
                    "MyFancyMod",
                    "Example Description",
                    null,
                    null,
                    ModType.Shared,
                    new List<string> { "Example Author" },
                    new List<Dependency>(),
                    new List<ModTag> { ModTag.Doodad },
                    new List<string> { "ffa_exampleMap", "tdm_exampleMap" },
                    new OptionFlags(true)
                )
            );

            return PromptAddRelease(dummyRelease);
        }
    }
}