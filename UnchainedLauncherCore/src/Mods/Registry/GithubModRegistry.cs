using log4net;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncherCore.Mods.Registry {
    public class GithubModRegistry : ModRegistry {
        public string Organization { get; }
        public string RepoName { get; }

        public string PackageDBBaseUrl => $"https://raw.githubusercontent.com/{Organization}/{RepoName}/db/package_db";
        public string PackageDBPackageListUrl => $"{PackageDBBaseUrl}/mod_list_index.txt";

        public GithubModRegistry(string organization, string repoName) {
            Organization = organization;
            RepoName = repoName;
        }

        public override DownloadTask<IEnumerable<DownloadTask<Mod>>> GetAllMods() {
            return HttpHelpers.GetStringContentsAsync(PackageDBPackageListUrl)
                .ContinueWith(listString => listString.Split("\n"))
                .ContinueWith(packages => packages.Select(GetModMetadata));
        }

        public override DownloadTask<string> GetModMetadataString(string modPath) {
            return HttpHelpers.GetStringContentsAsync($"{PackageDBBaseUrl}/packages/{modPath}.json");
        }
    }
}
