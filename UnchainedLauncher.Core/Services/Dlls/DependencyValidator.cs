using UnchainedLauncher.Core.JsonModels.ModMetadata;

namespace UnchainedLauncher.Core.Services.Dlls {
    public static class DependencyValidator {
        public static void Validate(IReadOnlyList<Release> mods, IReadOnlyList<DllRelease> plugins) {
            var pluginsByMod = mods
                .GroupBy(mod => mod.Info.RepoUrl, StringComparer.OrdinalIgnoreCase)
                .Select(group => (
                    Info: group.First().Info,
                    MissingDependencies: group
                        .SelectMany(mod => mod.Info.EffectivePluginDependencies)
                        .Where(dependency => !plugins.Any(plugin =>
                            dependency.RepoUrl.Equals($"https://github.com/{plugin.Id}", StringComparison.OrdinalIgnoreCase)
                            && PluginInfo.MatchesVersion(plugin.Version, dependency.Version)))
                        .Distinct()
                        .ToList()))
                .Where(group => group.MissingDependencies.Count != 0)
                .ToArray();

            if (pluginsByMod.Length != 0) {
                var modText =
                    pluginsByMod.Length == 1
                        ? "One mod requires"
                        : "Some mods require";

                var pluginText =
                    pluginsByMod.SelectMany(group => group.MissingDependencies).Distinct().Count() == 1
                        ? "a DLL plugin"
                        : "DLL plugins";

                var details = pluginsByMod
                    .OrderBy(group => group.Info.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(group => group.Info.RepoUrl, StringComparer.OrdinalIgnoreCase)
                    .Select(group => $"{group.Info.Name}:\n" + string.Join("\n", group.MissingDependencies
                        .OrderBy(dependency => dependency.RepoName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(dependency => dependency.RepoUrl, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(dependency => dependency.Version, StringComparer.OrdinalIgnoreCase)
                        .Select(dependency => $"\t- {dependency.RepoName} {dependency.Version}")));

                throw new InvalidOperationException($"{modText} {pluginText}. Enable them in the Plugin Manager.\n\n{string.Join("\n\n", details)}");
            }

            var missingMod = plugins
                .SelectMany(plugin => (plugin.Info?.RequiredMods ?? [])
                    .Select(dependency => (Plugin: plugin, Dependency: dependency)))
                .FirstOrDefault(requirement => !mods.Any(mod =>
                    requirement.Dependency.RepoUrl.Equals(mod.Info.RepoUrl, StringComparison.OrdinalIgnoreCase)
                    && mod.Version != null
                    && PluginInfo.MatchesVersion(mod.Version.ToString(), requirement.Dependency.Version)));

            if (missingMod.Plugin != null) {
                throw new InvalidOperationException(
                    $"Enable a compatible mod for {missingMod.Plugin.Id}: {missingMod.Dependency.RepoUrl} {missingMod.Dependency.Version}");
            }
        }
    }
}