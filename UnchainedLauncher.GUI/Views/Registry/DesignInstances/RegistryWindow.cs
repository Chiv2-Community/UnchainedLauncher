using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels.Registry;

namespace UnchainedLauncher.GUI.Views.Registry.DesignInstances {
    public static class RegistryWindowViewModelInstances {
        public static RegistryWindowVM DEFAULT => new RegistryWindowDesignVM();
    }

    public class RegistryWindowDesignVM : RegistryWindowVM {
        public RegistryWindowDesignVM() : base(
            new AggregateModRegistry(
                new GithubModRegistry(
                    "Chiv2-Community",
                    "C2ModRegistry"
                ),
                new LocalModRegistry("LocalModRegistryTesting1"),
                new LocalModRegistry("LocalModRegistryTesting2")
            ),
            new RegistryWindowService()
        ) { }
    }
}