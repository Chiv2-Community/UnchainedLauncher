using System;
using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.GUI.Services {
    public static class ServerBlueprintIdentity {
        public static string Key(BlueprintDto blueprint) =>
            string.IsNullOrWhiteSpace(blueprint.ClassPath) ? blueprint.Path : blueprint.ClassPath;

        public static bool Same(BlueprintDto first, BlueprintDto second) =>
            string.Equals(Key(first), Key(second), StringComparison.OrdinalIgnoreCase);
    }
}