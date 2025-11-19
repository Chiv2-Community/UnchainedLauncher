using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels.Registry;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class RegistryWindowViewModelInstances {
        private static RegistryWindowVM MakeDefault() {
            var aggregateRegistry = new AggregateModRegistry(
                new GithubModRegistry(
                    "Chiv2-Community",
                    "C2ModRegistry"
                ),
                new LocalModRegistry("LocalModRegistryTesting1"),
                new LocalModRegistry("LocalModRegistryTesting2")
            );


            return new RegistryWindowVM(aggregateRegistry, new RegistryWindowService());

        }

        public static RegistryWindowVM DEFAULT => MakeDefault();
    }
}