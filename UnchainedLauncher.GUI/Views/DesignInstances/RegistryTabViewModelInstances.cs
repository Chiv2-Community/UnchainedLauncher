using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels.Registry;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class RegistryTabViewModelInstances {
        private static RegistryTabVM MakeDefault() {
            var aggregateRegistry = new AggregateModRegistry(
                new GithubModRegistry(
                    "Chiv2-Community",
                    "C2ModRegistry"
                ),
                new LocalModRegistry("LocalModRegistryTesting1"),
                new LocalModRegistry("LocalModRegistryTesting2")
            );
            
            
            return new RegistryTabVM(aggregateRegistry, x => new LocalModRegistryWindowService(x));

        }
        
        public static RegistryTabVM DEFAULT => MakeDefault();
    }
}