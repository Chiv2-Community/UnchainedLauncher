using System;
using System.Collections.Generic;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels.Registry;

namespace UnchainedLauncher.GUI.Views.Registry.DesignInstances {
    public static class LocalModRegistryConfigWindowViewModelInstances {
        public static RegistryWindowService DEFAULT_WINDOW_SERVICE = new();

        public static LocalModRegistryDetailsVM DEFAULT => new LocalModRegistryDetailsDesignVM();

        public static RegistryReleaseFormVM DEFAULT_RELEASE => new RegistryReleaseFormDesignVM();
    }

    public class LocalModRegistryDetailsDesignVM : LocalModRegistryDetailsVM {
        public LocalModRegistryDetailsDesignVM() : base(
            new LocalModRegistry("LocalModRegistryTesting1"),
            LocalModRegistryConfigWindowViewModelInstances.DEFAULT_WINDOW_SERVICE
        ) {
        }
    }

    public class RegistryReleaseFormDesignVM : RegistryReleaseFormVM {
        public RegistryReleaseFormDesignVM() : base(
            new Release(
                "v1.0.0",
                "some hash",
                "pakName.pak",
                DateTime.Now,
                new ModInfo(
                    "some-repo-url.com",
                    "Example mod",
                    "Example Description",
                    null,
                    "https://example.com/icon.png",
                    new List<string> { "https://example.com/icon.png" },
                    new List<string> { "DrLong", "JayKobe6k" },
                    new List<Dependency>()
                ),
                null,
                "# Example Release Notes\n\n* Your release notes here"
            ),
            null
        ) {
        }
    }
}