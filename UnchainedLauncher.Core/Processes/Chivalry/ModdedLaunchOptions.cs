using LanguageExt;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;

namespace UnchainedLauncher.Core.Processes.Chivalry
{
    public record ModdedLaunchOptions(
        string ServerBrowserBackend,
        Option<IEnumerable<Release>> EnabledMods,
        Option<string> SavedDirSuffix,
        Option<ServerLaunchOptions> ServerLaunchOptions
    ) {
        public IEnumerable<string> ToCLIArgs() {
            var args = new List<string> {
                $"--server-browser-backend {ServerBrowserBackend}"
            };
            ServerLaunchOptions.IfSome(opts => args.AddRange(opts.ToCLIArgs()));
            EnabledMods.IfSome(mods => args.AddRange(mods.Select(mod => $"--mod {mod.Manifest.RepoUrl}")));
            SavedDirSuffix.IfSome(suffix => args.Add($"--saved-dir-suffix {suffix}"));
            return args;
        }
    };
}