using System;
using System.Collections.Generic;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels.Registry;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class LocalModRegistryConfigWindowViewModelInstances {
        private static LocalModRegistryWindowService _windowService = new LocalModRegistryWindowService(new LocalModRegistry("exampleRegistry"));
            
        public static LocalModRegistryWindowVM DEFAULT =>
            new LocalModRegistryWindowVM(_windowService);

        public static RegistryReleaseFormVM DEFAULT_RELEASE => new RegistryReleaseFormVM(
            new Release(
                "v1.0.0",
                "some hash",
                "pakName.pak",
                DateTime.Now,
                new ModManifest(
                    "some-repo-url.com",
                    "Example mod",
                    "Example Description",
                    null,
                    null,
                    ModType.Shared,
                    new List<string> { "DrLong", "JayKobe6k" },
                    new List<Dependency>(),
                    new List<ModTag> { ModTag.Doodad, ModTag.Mutator },
                    new List<string> { "ffa_exampleMap", "tdm_exampleMap" },
                    new OptionFlags(true)
                    )
                ), null
            );
    }
}