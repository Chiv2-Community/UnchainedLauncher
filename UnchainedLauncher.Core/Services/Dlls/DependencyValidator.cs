using UnchainedLauncher.Core.JsonModels.ModMetadata;

namespace UnchainedLauncher.Core.Services.Dlls {
    public static class DependencyValidator {
        public static void Validate(IReadOnlyList<Release> mods, IReadOnlyList<DllRelease> plugins) {
            foreach (var mod in mods) {
                foreach (var dependency in mod.Info.EffectivePluginDependencies) {
                    if (!plugins.Any(plugin => dependency.RepoUrl.Equals($"https://github.com/{plugin.Id}", StringComparison.OrdinalIgnoreCase)
                                               && PluginInfo.MatchesVersion(plugin.Version, dependency.Version)))
                        throw new InvalidOperationException($"Enable a compatible registered plugin for {mod.Info.Name}: {dependency.RepoUrl} {dependency.Version}");
                }
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
