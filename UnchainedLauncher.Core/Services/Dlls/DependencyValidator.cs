using UnchainedLauncher.Core.JsonModels.ModMetadata;

namespace UnchainedLauncher.Core.Services.Dlls {
    public static class DependencyValidator {
        public static void Validate(IReadOnlyList<Release> mods, IReadOnlyList<DllRelease> plugins) {
            var pluginsByMod = mods.GroupBy(mod => mod.Info.RepoUrl, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.First().Info, group => group
                    .SelectMany(mod => mod.Info.EffectivePluginDependencies)
                    .Where(dependency => !plugins.Any(plugin =>
                        dependency.RepoUrl.Equals($"https://github.com/{plugin.Id}", StringComparison.OrdinalIgnoreCase)
                        && PluginInfo.MatchesVersion(plugin.Version, dependency.Version)))
                    .Distinct().ToList())
                .Where(pair => pair.Value.Count != 0).ToDictionary(pair => pair.Key, pair => pair.Value);
            if (pluginsByMod.Count != 0) {
                var modText = pluginsByMod.Count == 1 ? "One mod requires" : "Some mods require";
                var pluginText = pluginsByMod.Values.SelectMany(dependencies => dependencies).Distinct().Count() == 1
                    ? "a DLL plugin" : "DLL plugins";
                var details = pluginsByMod.OrderBy(pair => pair.Key.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(pair => pair.Key.RepoUrl, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => $"{pair.Key.Name}:\n" + string.Join("\n", pair.Value
                        .OrderBy(dependency => dependency.RepoName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(dependency => dependency.RepoUrl, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(dependency => dependency.Version, StringComparer.OrdinalIgnoreCase)
                        .Select(dependency => $"\t- {dependency.RepoName} {dependency.Version}")));
                throw new InvalidOperationException($"{modText} {pluginText}. Enable them in the Plugin Manager.\n\n{string.Join("\n\n", details)}");
            }
            foreach (var plugin in plugins) {
                foreach (var dependency in plugin.Info?.RequiredMods ?? []) {
                    if (!mods.Any(mod => dependency.RepoUrl.Equals(mod.Info.RepoUrl, StringComparison.OrdinalIgnoreCase)
                                         && mod.Version != null && PluginInfo.MatchesVersion(mod.Version.ToString(), dependency.Version)))
                        throw new InvalidOperationException($"Enable a compatible mod for {plugin.Id}: {dependency.RepoUrl} {dependency.Version}");
                }
            }
        }
    }
}